using System.Collections.Concurrent;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Utils;

public enum DiscordVerifyResult
{
    Success,
    InvalidCode,
    DiscordAlreadyLinked,
    Disabled,
    DbError
}

public readonly record struct DiscordVerifyOutcome(DiscordVerifyResult Result, ulong SteamId);

public class DiscordVerifyService
{
    private const string PanelCustomId = "verify_open";

    // 0/O ve 1/I/L gibi karışan karakterler çıkarıldı; oyuncular kodu Discord'a elle yazıyor.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly bool _enabled;
    private readonly string _verifyChannelId;
    private readonly string _guildId;
    private readonly string _verifiedRoleId;
    private readonly TimeSpan _codeTtl;

    private readonly ConcurrentDictionary<string, PendingVerification> _pendingByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ulong, string> _pendingBySteamId = new();

    private DiscordLinkDbManager? _linkDbManager;
    private DiscordMessageStateDbManager? _messageStateDbManager;
    private string? _panelMessageId;
    private readonly SemaphoreSlim _panelLock = new(1, 1);

    public DiscordVerifyService(ISwiftlyCore core, DiscordRestClient restClient, DiscordFileConfig config)
    {
        _core = core;
        _restClient = restClient;
        _enabled = config.VerifyEnabled;
        _verifyChannelId = config.VerifyChannelId ?? string.Empty;
        _guildId = config.GuildId ?? string.Empty;
        _verifiedRoleId = config.VerifiedRoleId ?? string.Empty;
        _codeTtl = TimeSpan.FromMinutes(Math.Clamp(config.VerifyCodeExpiryMinutes, 1, 1440));
    }

    public event Action<ulong, bool>? LinkChanged;

    public bool IsEnabled => _enabled;
    public int CodeExpiryMinutes => (int)_codeTtl.TotalMinutes;

    public void SetDatabaseManagers(DiscordLinkDbManager linkDbManager, DiscordMessageStateDbManager messageStateDbManager)
    {
        _linkDbManager = linkDbManager;
        _messageStateDbManager = messageStateDbManager;
    }

    public string CreateCode(ulong steamId)
    {
        if (_pendingBySteamId.TryRemove(steamId, out var oldCode))
        {
            _pendingByCode.TryRemove(oldCode, out _);
        }

        var code = GenerateCode();
        var expiresAt = DateTime.UtcNow + _codeTtl;
        _pendingByCode[code] = new PendingVerification(steamId, expiresAt);
        _pendingBySteamId[steamId] = code;
        CleanupExpiredCodes();
        return code;
    }

    public Task<DiscordLink?> GetLinkBySteamIdAsync(ulong steamId)
    {
        return _linkDbManager == null
            ? Task.FromResult<DiscordLink?>(null)
            : _linkDbManager.GetBySteamIdAsync(steamId);
    }

    public async Task<DiscordVerifyOutcome> CompleteVerificationAsync(string? rawCode, ulong discordId, string discordName, string? guildId)
    {
        if (!_enabled || _linkDbManager == null)
        {
            return new DiscordVerifyOutcome(DiscordVerifyResult.Disabled, 0);
        }

        var code = (rawCode ?? string.Empty).Trim();
        if (code.Length == 0
            || !_pendingByCode.TryRemove(code, out var pending)
            || pending.ExpiresAt < DateTime.UtcNow)
        {
            return new DiscordVerifyOutcome(DiscordVerifyResult.InvalidCode, 0);
        }

        _pendingBySteamId.TryRemove(pending.SteamId, out _);
        var steamId = pending.SteamId;

        var existingDiscordLink = await _linkDbManager.GetByDiscordIdAsync(discordId);
        if (existingDiscordLink != null && existingDiscordLink.SteamId != steamId)
        {
            return new DiscordVerifyOutcome(DiscordVerifyResult.DiscordAlreadyLinked, steamId);
        }

        var serverId = ServerIdentity.GetServerId(_core);
        if (!await _linkDbManager.LinkAsync(steamId, discordId, discordName, serverId))
        {
            return new DiscordVerifyOutcome(DiscordVerifyResult.DbError, steamId);
        }

        LinkChanged?.Invoke(steamId, true);

        var effectiveGuildId = !string.IsNullOrWhiteSpace(guildId) ? guildId : _guildId;
        if (!string.IsNullOrWhiteSpace(effectiveGuildId) && !string.IsNullOrWhiteSpace(_verifiedRoleId))
        {
            _ = _restClient.AddGuildMemberRoleAsync(effectiveGuildId, discordId.ToString(), _verifiedRoleId);
        }

        NotifyLinkedInGame(steamId, discordName);
        return new DiscordVerifyOutcome(DiscordVerifyResult.Success, steamId);
    }

    public async Task<bool> UnlinkAsync(ulong steamId)
    {
        if (_linkDbManager == null)
        {
            return false;
        }

        var existing = await _linkDbManager.GetBySteamIdAsync(steamId);
        if (existing == null)
        {
            return false;
        }

        if (!await _linkDbManager.UnlinkBySteamIdAsync(steamId))
        {
            return false;
        }

        LinkChanged?.Invoke(steamId, false);

        if (!string.IsNullOrWhiteSpace(_guildId) && !string.IsNullOrWhiteSpace(_verifiedRoleId))
        {
            _ = _restClient.RemoveGuildMemberRoleAsync(_guildId, existing.DiscordId.ToString(), _verifiedRoleId);
        }

        return true;
    }

    public async Task PublishVerifyPanelAsync()
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_verifyChannelId))
            return;

        await _panelLock.WaitAsync();
        try
        {
            // The channel ID is stable even when the detected server IP or port changes.
            var messageKey = $"verify:channel:{_verifyChannelId}";
            var dbMessageId = _messageStateDbManager == null
                ? null
                : await _messageStateDbManager.GetMessageIdAsync(messageKey);
            var previousMessageId = !string.IsNullOrWhiteSpace(dbMessageId)
                ? dbMessageId
                : _panelMessageId;

            if (string.IsNullOrWhiteSpace(previousMessageId))
            {
                var found = await _restClient.FindVerifyPanelAsync(_verifyChannelId, ServerIdentity.GetServerId(_core));
                if (!found.Success)
                    return; // Do not post blindly when existing messages cannot be checked.
                previousMessageId = found.MessageId;
            }

            var embed = BuildPanelEmbed();
            var components = BuildPanelComponents();
            if (!string.IsNullOrWhiteSpace(previousMessageId))
            {
                var updated = await _restClient.UpdateEmbedAsync(_verifyChannelId, previousMessageId, embed, components: components);
                if (updated == true)
                {
                    _panelMessageId = previousMessageId;
                    if (_messageStateDbManager != null && dbMessageId != previousMessageId)
                        await _messageStateDbManager.UpsertMessageIdAsync(messageKey, _verifyChannelId, previousMessageId);
                    return;
                }
                if (updated == false)
                    return;

                // 404: check for another existing panel before sending a replacement.
                var found = await _restClient.FindVerifyPanelAsync(_verifyChannelId, ServerIdentity.GetServerId(_core));
                if (!found.Success)
                    return;
                if (!string.IsNullOrWhiteSpace(found.MessageId) && found.MessageId != previousMessageId)
                {
                    var recovered = await _restClient.UpdateEmbedAsync(_verifyChannelId, found.MessageId, embed, components: components);
                    if (recovered != true)
                        return;
                    _panelMessageId = found.MessageId;
                    if (_messageStateDbManager != null)
                        await _messageStateDbManager.UpsertMessageIdAsync(messageKey, _verifyChannelId, found.MessageId);
                    return;
                }
            }

            var newMessageId = await _restClient.SendEmbedAsync(_verifyChannelId, embed, components: components);
            if (string.IsNullOrWhiteSpace(newMessageId))
                return;
            _panelMessageId = newMessageId;
            if (_messageStateDbManager != null)
                await _messageStateDbManager.UpsertMessageIdAsync(messageKey, _verifyChannelId, newMessageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error publishing verify panel: {Message}", ex.Message);
        }
        finally
        {
            _panelLock.Release();
        }
    }

    private object BuildPanelEmbed()
    {
        var serverName = ServerIdentity.GetServerId(_core);
        return new
        {
            title = T("discord_verify_panel_title", "Link your Steam account"),
            description = T("discord_verify_panel_description",
                "1. Join the server and type `!verify` in chat\n2. Click the button below\n3. Enter the code shown in-game\n\nYour Steam account will be linked to your Discord account."),
            color = 0x5865F2,
            footer = new { text = $"CS2_Admin | {serverName}" }
        };
    }

    private object[] BuildPanelComponents()
    {
        return new object[]
        {
            new
            {
                type = 1,
                components = new object[]
                {
                    new
                    {
                        type = 2,
                        style = 1,
                        label = T("discord_verify_button", "Verify Account"),
                        custom_id = PanelCustomId,
                        emoji = new { name = "🔗" }
                    }
                }
            }
        };
    }

    private void NotifyLinkedInGame(ulong steamId, string discordName)
    {
        _core.Scheduler.NextTick(() =>
        {
            var player = _core.PlayerManager.GetAllPlayers()
                .FirstOrDefault(p => p.IsValid && !p.IsFakeClient && p.SteamID == steamId);
            player?.SendChat($" \x02{T("prefix", "CS2_Admin")}\x01 {T("verify_linked_ingame", "Your account has been linked to Discord user {0}.", discordName)}");
        });
    }

    private static string GenerateCode()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
        }
        return new string(chars);
    }

    private void CleanupExpiredCodes()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _pendingByCode)
        {
            if (kvp.Value.ExpiresAt < now
                && _pendingByCode.TryRemove(kvp.Key, out var removed)
                && _pendingBySteamId.TryGetValue(removed.SteamId, out var current)
                && string.Equals(current, kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                _pendingBySteamId.TryRemove(removed.SteamId, out _);
            }
        }
    }

    private string T(string key, string fallback, params object[] args)
    {
        return args.Length == 0
            ? global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback)
            : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback, args);
    }

    private readonly record struct PendingVerification(ulong SteamId, DateTime ExpiresAt);
}
