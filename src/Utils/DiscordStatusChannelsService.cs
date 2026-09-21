using CS2_Admin.Database;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordStatusChannelsService
{
    private static readonly TimeSpan MinRenameInterval = TimeSpan.FromSeconds(310);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);

    private const int ChannelTypeVoice = 2;
    private const int ChannelTypeCategory = 4;
    private const int MaxChannelNameLength = 100;
    private const string DenyConnectPermission = "1048576";

    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly string _guildId;
    private readonly string _categoryNameTemplate;
    private readonly string _serverName;
    private readonly string _serverPublicIp;

    private DiscordMessageStateDbManager? _messageStateDbManager;

    private readonly ChannelSlot _categorySlot = new(ChannelTypeCategory, "category");
    private readonly ChannelSlot _playersSlot = new(ChannelTypeVoice, "players");
    private readonly ChannelSlot _mapSlot = new(ChannelTypeVoice, "map");

    private List<DiscordGuildChannel>? _guildChannelsCache;
    private bool _guildChannelsFetched;
    private DateTime _nextCreateAttemptUtc = DateTime.MinValue;

    private bool _stateLoaded;
    private int _updateInProgress;

    private sealed class ChannelSlot
    {
        public ChannelSlot(int type, string keySuffix)
        {
            Type = type;
            KeySuffix = keySuffix;
        }

        public int Type { get; }
        public string KeySuffix { get; }
        public string? ChannelId { get; set; }
        public string? LastAppliedName { get; set; }
        public DateTime NotBeforeUtc { get; set; } = DateTime.MinValue;
    }

    public DiscordStatusChannelsService(ISwiftlyCore core, DiscordRestClient restClient,
        string guildId, string categoryNameTemplate, string serverName, string serverPublicIp)
    {
        _core = core;
        _restClient = restClient;
        _guildId = guildId;
        _categoryNameTemplate = categoryNameTemplate;
        _serverName = serverName;
        _serverPublicIp = serverPublicIp;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_categoryNameTemplate)
        && !string.IsNullOrWhiteSpace(_guildId);

    public void SetDatabaseManagers(DiscordMessageStateDbManager? messageStateDbManager)
    {
        _messageStateDbManager = messageStateDbManager;
    }

    public async Task UpdateAsync()
    {
        if (!IsEnabled || _messageStateDbManager == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _updateInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await UpdateCoreAsync();
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error updating Discord status channels: {Message}", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _updateInProgress, 0);
        }
    }

    private async Task UpdateCoreAsync()
    {
        var instanceKey = GetInstanceKey();
        await EnsureStateLoadedAsync(instanceKey);

        _guildChannelsCache = null;
        _guildChannelsFetched = false;

        var mapName = ServerIdentity.GetCurrentMap(_core);
        var playerCount = _core.PlayerManager.GetAllPlayers().Count(p => p.IsValid && !p.IsFakeClient);
        var maxPlayers = ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap);
        var playersLabel = T("discord_status_channel_players", "Players");
        var mapLabel = T("discord_status_channel_map", "Map");

        var desiredCategoryName = SanitizeChannelName(ResolveCategoryName());
        var desiredPlayersName = SanitizeChannelName($"» 🌐 」{playersLabel}: {playerCount}/{maxPlayers}");
        var desiredMapName = IsUnknownMapName(mapName)
            ? null
            : SanitizeChannelName($"» 🌐 」{mapLabel}: {mapName}");

        if (string.IsNullOrWhiteSpace(desiredCategoryName) || string.IsNullOrWhiteSpace(desiredPlayersName))
        {
            return;
        }

        if (!await EnsureSlotAsync(_categorySlot, desiredCategoryName, null, instanceKey,
            c => c.Type == ChannelTypeCategory
                && string.Equals(c.Name, desiredCategoryName, StringComparison.Ordinal)))
        {
            return;
        }

        var playersPrefix = $"» 🌐 」{playersLabel}:";
        var categoryId = _categorySlot.ChannelId;
        if (!await EnsureSlotAsync(_playersSlot, desiredPlayersName, categoryId, instanceKey,
            c => c.Type == ChannelTypeVoice
                && string.Equals(c.ParentId, categoryId, StringComparison.Ordinal)
                && c.Name != null
                && c.Name.StartsWith(playersPrefix, StringComparison.Ordinal)))
        {
            return;
        }

        if (desiredMapName != null)
        {
            var mapPrefix = $"» 🌐 」{mapLabel}:";
            await EnsureSlotAsync(_mapSlot, desiredMapName, categoryId, instanceKey,
                c => c.Type == ChannelTypeVoice
                    && string.Equals(c.ParentId, categoryId, StringComparison.Ordinal)
                    && c.Name != null
                    && c.Name.StartsWith(mapPrefix, StringComparison.Ordinal));
        }
    }

    private async Task<bool> EnsureSlotAsync(ChannelSlot slot, string desiredName, string? parentId,
        string instanceKey, Func<DiscordGuildChannel, bool> adoptMatch)
    {
        if (!string.IsNullOrWhiteSpace(slot.ChannelId))
        {
            if (await ApplyChannelRenameAsync(slot, desiredName))
            {
                return true;
            }

            slot.ChannelId = null;
            slot.LastAppliedName = null;
        }

        var guildChannels = await GetGuildChannelsAsync();
        if (guildChannels == null)
        {
            return false;
        }

        var existing = guildChannels.FirstOrDefault(adoptMatch);
        if (existing != null)
        {
            slot.ChannelId = existing.Id;
            slot.LastAppliedName = existing.Name;
            await PersistAsync(StateKey(instanceKey, slot), parentId ?? _guildId, existing.Id);
            return await ApplyChannelRenameAsync(slot, desiredName);
        }

        var createdId = await TryCreateChannelAsync(desiredName, slot.Type, parentId);
        if (string.IsNullOrWhiteSpace(createdId))
        {
            return false;
        }

        slot.ChannelId = createdId;
        slot.LastAppliedName = desiredName;
        await PersistAsync(StateKey(instanceKey, slot), parentId ?? _guildId, createdId);
        return true;
    }

    private async Task<bool> ApplyChannelRenameAsync(ChannelSlot slot, string desiredName)
    {
        var now = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(slot.ChannelId)
            || string.Equals(slot.LastAppliedName, desiredName, StringComparison.Ordinal)
            || now < slot.NotBeforeUtc)
        {
            return true;
        }

        var result = await _restClient.UpdateChannelNameAsync(slot.ChannelId, desiredName);
        switch (result.Status)
        {
            case DiscordChannelUpdateStatus.Success:
                slot.LastAppliedName = desiredName;
                slot.NotBeforeUtc = now + MinRenameInterval;
                return true;
            case DiscordChannelUpdateStatus.NotFound:
                return false;
            case DiscordChannelUpdateStatus.RateLimited:
                slot.NotBeforeUtc = now + TimeSpan.FromSeconds(Math.Max(5, result.RetryAfterSeconds));
                return true;
            default:
                slot.NotBeforeUtc = now + RetryInterval;
                return true;
        }
    }

    private async Task<List<DiscordGuildChannel>?> GetGuildChannelsAsync()
    {
        if (_guildChannelsFetched)
        {
            return _guildChannelsCache;
        }

        _guildChannelsFetched = true;
        _guildChannelsCache = await _restClient.GetGuildChannelsAsync(_guildId);
        return _guildChannelsCache;
    }

    private async Task<string?> TryCreateChannelAsync(string name, int type, string? parentId)
    {
        var now = DateTime.UtcNow;
        if (now < _nextCreateAttemptUtc)
        {
            return null;
        }

        _nextCreateAttemptUtc = now + RetryInterval;

        object[] permissionOverwrites =
        [
            new { id = _guildId, type = 0, allow = "0", deny = DenyConnectPermission }
        ];

        object payload = type == ChannelTypeCategory
            ? new { name, type }
            : new { name, type, parent_id = parentId, permission_overwrites = permissionOverwrites };

        var createdId = await _restClient.CreateGuildChannelAsync(_guildId, payload);
        if (!string.IsNullOrWhiteSpace(createdId))
        {
            _nextCreateAttemptUtc = DateTime.MinValue;
        }

        return createdId;
    }

    private async Task EnsureStateLoadedAsync(string instanceKey)
    {
        if (_stateLoaded || _messageStateDbManager == null)
        {
            return;
        }

        _stateLoaded = true;
        _categorySlot.ChannelId ??= await _messageStateDbManager.GetMessageIdAsync(StateKey(instanceKey, _categorySlot));
        _playersSlot.ChannelId ??= await _messageStateDbManager.GetMessageIdAsync(StateKey(instanceKey, _playersSlot));
        _mapSlot.ChannelId ??= await _messageStateDbManager.GetMessageIdAsync(StateKey(instanceKey, _mapSlot));
    }

    private Task PersistAsync(string stateKey, string parentId, string channelId)
    {
        return _messageStateDbManager?.UpsertMessageIdAsync(stateKey, parentId, channelId)
            ?? Task.CompletedTask;
    }

    private string StateKey(string instanceKey, ChannelSlot slot)
        => $"status-channel:{_guildId}:{instanceKey}:{slot.KeySuffix}";

    private string GetInstanceKey()
    {
        var port = ServerIdentity.GetPort(_core);
        return string.IsNullOrWhiteSpace(_serverPublicIp)
            ? $"port-{port}"
            : $"{_serverPublicIp.Trim()}:{port}";
    }

    private string ResolveCategoryName()
    {
        var template = _categoryNameTemplate.Trim();
        return template.Contains("{SERVER}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{SERVER}", GetServerLabel(), StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private static string? SanitizeChannelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return trimmed.Length <= MaxChannelNameLength ? trimmed : trimmed[..MaxChannelNameLength];
    }

    private string GetServerLabel()
    {
        if (!string.IsNullOrWhiteSpace(_serverName))
        {
            return _serverName.Trim();
        }

        var configuredName = ServerIdentity.GetName(_core);
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName;
        }

        var serverId = ServerIdentity.GetServerId(_core);
        return string.IsNullOrWhiteSpace(serverId) ? "Server" : serverId;
    }

    private static bool IsUnknownMapName(string? mapName)
    {
        return string.IsNullOrWhiteSpace(mapName)
            || string.Equals(mapName.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapName.Trim(), "-", StringComparison.OrdinalIgnoreCase);
    }

    private string T(string key, string fallback, params object[] args)
    {
        return args.Length == 0
            ? global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback)
            : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback, args);
    }
}
