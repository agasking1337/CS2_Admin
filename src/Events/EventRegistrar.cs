using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using System.Collections.Concurrent;

namespace CS2_Admin.Events;

public class EventRegistrar
{
    private readonly ISwiftlyCore _core;
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly GroupDbManager _groupDbManager;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly MultiServerConfig _multiServerConfig;
    private readonly TagsConfig _tags;
    private readonly PermissionsConfig _permissions;
    private readonly ChatTagConfigManager _chatTagConfigManager;
    private readonly PlayerNameHistoryManager _playerNameHistoryManager;
    private readonly PlayerSessionManager _playerSessionManager;
    private readonly DiscordBotService? _discord;

    private readonly ConcurrentDictionary<int, DateTime> _gagWarnTimestamps = new();
    private readonly ConcurrentDictionary<int, byte> _connectNotificationsSent = new();
    private Guid _chatHookGuid = Guid.Empty;
    private CancellationTokenSource? _expiryCheckCts;
    private CancellationTokenSource? _banEnforceCts;
    private volatile bool _databaseReady;
    private int _isBanEnforcementRunning;
    private readonly ConcurrentDictionary<ulong, string> _lastKnownAdminTags = new();
    private readonly HashSet<string> _commandAliases;
    private readonly HashSet<string> _silentCommandAliases;

    private Action<IOnClientPutInServerEvent>? _onClientPutInServer;
    private Action<IOnClientSteamAuthorizeEvent>? _onClientSteamAuthorize;
    private Action<IOnClientDisconnectedEvent>? _onClientDisconnected;
    private Func<EventPlayerConnectFull, HookResult>? _onPlayerConnectFull;
    private Func<EventPlayerDisconnect, HookResult>? _onPlayerDisconnect;
    private Func<EventRoundStart, HookResult>? _onRoundStart;
    private Func<EventPlayerTeam, HookResult>? _onPlayerTeam;

    public EventRegistrar(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        GroupDbManager groupDbManager,
        PlayerSanctionStateService sanctionStateService,
        MultiServerConfig multiServerConfig,
        TagsConfig tags,
        PermissionsConfig permissions,
        ChatTagConfigManager chatTagConfigManager,
        PlayerNameHistoryManager playerNameHistoryManager,
        PlayerSessionManager playerSessionManager,
        DiscordBotService? discord,
        CommandsConfig? commandsConfig = null)
    {
        _core = core;
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _groupDbManager = groupDbManager;
        _sanctionStateService = sanctionStateService;
        _multiServerConfig = multiServerConfig;
        _tags = tags;
        _permissions = permissions;
        _chatTagConfigManager = chatTagConfigManager;
        _playerNameHistoryManager = playerNameHistoryManager;
        _playerSessionManager = playerSessionManager;
        _discord = discord;
        _commandAliases = CommandAliasResolver.BuildSet(commandsConfig);
        _silentCommandAliases = new HashSet<string>(
            commandsConfig?.Hide.Where(alias => !string.IsNullOrWhiteSpace(alias)).Select(alias => alias.Trim()) ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public void SetDatabaseReady(bool ready) => _databaseReady = ready;

    public void OnClientPutInServer(Action<IOnClientPutInServerEvent> handler) => _onClientPutInServer = handler;
    public void OnClientSteamAuthorize(Action<IOnClientSteamAuthorizeEvent> handler) => _onClientSteamAuthorize = handler;
    public void OnClientDisconnected(Action<IOnClientDisconnectedEvent> handler) => _onClientDisconnected = handler;
    public void OnPlayerConnectFull(Func<EventPlayerConnectFull, HookResult> handler) => _onPlayerConnectFull = handler;
    public void OnPlayerDisconnect(Func<EventPlayerDisconnect, HookResult> handler) => _onPlayerDisconnect = handler;
    public void OnRoundStart(Func<EventRoundStart, HookResult> handler) => _onRoundStart = handler;
    public void OnPlayerTeam(Func<EventPlayerTeam, HookResult> handler) => _onPlayerTeam = handler;

    public void RegisterAll()
    {
        if (_onClientPutInServer != null)
            _core.Event.OnClientPutInServer += e => _onClientPutInServer(e);

        if (_onClientSteamAuthorize != null)
            _core.Event.OnClientSteamAuthorize += e => _onClientSteamAuthorize(e);

        if (_onClientDisconnected != null)
            _core.Event.OnClientDisconnected += e => _onClientDisconnected(e);

        if (_onPlayerConnectFull != null)
            _core.GameEvent.HookPost<EventPlayerConnectFull>(e => _onPlayerConnectFull(e));

        if (_onPlayerDisconnect != null)
            _core.GameEvent.HookPost<EventPlayerDisconnect>(e => _onPlayerDisconnect(e));

        if (_onRoundStart != null)
            _core.GameEvent.HookPost<EventRoundStart>(e => _onRoundStart(e));

        if (_onPlayerTeam != null)
            _core.GameEvent.HookPost<EventPlayerTeam>(e => _onPlayerTeam(e));

        _core.Event.OnMapLoad += e => ServerIdentity.SetCurrentMapName(e.MapName);

        _chatHookGuid = _core.Command.HookClientChat(OnClientChat);

        _expiryCheckCts = _core.Scheduler.RepeatBySeconds(30f, CheckExpiredPunishments);

        _banEnforceCts = _core.Scheduler.RepeatBySeconds(5f, EnforceOnlineBans);
    }

    public void UnregisterAll()
    {
        if (_chatHookGuid != Guid.Empty)
        {
            _core.Command.UnhookClientChat(_chatHookGuid);
            _chatHookGuid = Guid.Empty;
        }

        _expiryCheckCts?.Cancel();
        _expiryCheckCts = null;

        _banEnforceCts?.Cancel();
        _banEnforceCts = null;
    }

    private HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        if (_databaseReady)
        {
            _ = _playerNameHistoryManager.ObserveNameAsync(player.SteamID, player.Controller.PlayerName);
            _ = _playerSessionManager.TouchSessionAsync(player.SteamID, player.Controller.PlayerName, playerId, player.IPAddress);
        }

        if (IsSilentCommand(text))
            return HookResult.Stop;

        if (!string.IsNullOrWhiteSpace(text))
            _ = _discord?.SendChatNotificationAsync(player, text, teamOnly);

        if (!string.IsNullOrWhiteSpace(text) && (text.StartsWith("!") || text.StartsWith("/"))
            && CommandAliasResolver.IsCommandPrefix(_commandAliases, text))
        {
            return HookResult.Handled;
        }

        var steamId = player.SteamID;

        // Gag durumu için TTL'siz snapshot (PlayerSanctionStateService) otoritedir.
        // 30 sn'lik GagManager cache'i yalnızca snapshot henüz yüklenmemişse yedek olarak
        // kullanılır. Önceki kod sadece 30 sn'lik cache'e baktığı için cache düştüğünde
        // gaglı oyuncu mesaj yazabiliyordu (cache miss = engellenmeyen mesaj).
        var cachedGag = _sanctionStateService.GetCachedGag(steamId) ?? _gagManager.GetActiveGagFromCache(steamId);
        if (cachedGag != null && cachedGag.IsActive)
        {
            bool shouldShowMessage = !_gagWarnTimestamps.ContainsKey(playerId) ||
                                   (DateTime.UtcNow - _gagWarnTimestamps[playerId]).TotalSeconds >= 5;

            if (shouldShowMessage)
            {
                var message = cachedGag.IsPermanent
                    ? LocalizerHelper.Get(_core, "gagged_chat_warning_permanent")
                    : LocalizerHelper.Get(_core, "gagged_chat_warning_minutes", (int)Math.Ceiling(cachedGag.TimeRemaining!.Value.TotalMinutes));

                player.SendChat($" \x02{LocalizerHelper.Get(_core, "prefix")}\x01 {message}");
                _gagWarnTimestamps[playerId] = DateTime.UtcNow;
            }

            var playerName = player.Controller.PlayerName ?? LocalizerHelper.Get(_core, "unknown");
            var strippedText = string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
            var preview = strippedText.Length <= 220 ? strippedText : $"{strippedText[..220]}...";

            foreach (var admin in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
            {
                var canSee =
                    _core.Permission.PlayerHasPermission(admin.SteamID, _permissions.AdminRoot) ||
                    (!string.IsNullOrWhiteSpace(_permissions.AdminMenu) && _core.Permission.PlayerHasPermission(admin.SteamID, _permissions.AdminMenu)) ||
                    (!string.IsNullOrWhiteSpace(_permissions.ListPlayers) && _core.Permission.PlayerHasPermission(admin.SteamID, _permissions.ListPlayers));

                if (!canSee)
                    continue;

                admin.SendChat($" \x02{LocalizerHelper.Get(_core, "prefix")}\x01 {LocalizerHelper.Get(_core, "gagged_chat_admin_visible", playerName, preview)}");
            }

            return HookResult.Stop;
        }

        // Snapshot henüz yüklenmemişse (örn. bağlanmanın hemen ardından) otoriteyi doldur.
        // Snapshot zaten varsa (gag olsun olmasın) gereksiz DB sorgusu yapılmaz.
        if (_sanctionStateService.GetCachedState(steamId) == null)
        {
            _ = _sanctionStateService.RefreshAsync(steamId, player.IPAddress);
        }


        if (_chatTagConfigManager.Config.ChatEnabled && !string.IsNullOrWhiteSpace(text))
        {
            if (DebugSettings.LoggingEnabled)
            {
                var preTag = ResolveChatGroupTag(player);
                _core.Logger.LogInformationIfEnabled(
                    "[CS2Admin][Debug][ChatTag] sender={Name} steamid={SteamId} teamOnly={TeamOnly} resolvedTag='{Tag}' chatEnabled={Enabled} text='{Text}'",
                    player.Controller.PlayerName, steamId, teamOnly, preTag, _chatTagConfigManager.Config.ChatEnabled, text);
            }
            BroadcastFormattedChat(player, text, teamOnly);
            // Stop kullanmamız gerek — Handled oyunun default chat mesajını engellemez,
            // böylece hem "[ Owner ] SSonic: ." hem de oyunun "[HERKES] SSonic [İZLEYİCİ]: ."
            // mesajı aynı anda gözükür (duplicate).
            return HookResult.Stop;
        }

        if (DebugSettings.LoggingEnabled && !string.IsNullOrWhiteSpace(text))
        {
            _core.Logger.LogInformationIfEnabled(
                "[CS2Admin][Debug][ChatTag] chat tag broadcast SKIPPED | sender={Name} steamid={SteamId} reason='{Reason}'",
                player.Controller.PlayerName, steamId,
                !_chatTagConfigManager.Config.ChatEnabled ? "ChatEnabled=false in tags.json" : "empty text");
        }

        return HookResult.Continue;
    }

    private bool IsSilentCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || (text[0] != '!' && text[0] != '/'))
            return false;

        var alias = text[1..]
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return alias != null && _silentCommandAliases.Contains(alias);
    }

    private void BroadcastFormattedChat(IPlayer sender, string rawText, bool teamOnly)
    {
        var text = rawText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var groupTag = ResolveChatGroupTag(sender);
        var style = _chatTagConfigManager.GetStyleForGroup(groupTag);
        var senderName = sender.Controller.PlayerName ?? LocalizerHelper.Get(_core, "unknown");
        var scopePrefix = teamOnly ? $"{style.ChatColor}{LocalizerHelper.Get(_core, "chat_team_prefix")} " : string.Empty;
        var formatted = $" \x01{scopePrefix}{style.ChatColor}[ {style.TagColor}{groupTag} {style.ChatColor}] {style.NameColor}{senderName}{style.ChatColor}: {text}";
        var senderTeam = sender.Controller.TeamNum;

        if (DebugSettings.LoggingEnabled)
        {
            _core.Logger.LogInformationIfEnabled(
                "[CS2Admin][Debug][ChatTag] broadcast | sender={Name} teamOnly={TeamOnly} groupTag='{Tag}' chatColor='{Chat}' tagColor='{TagC}' nameColor='{NameC}' targets={Targets} formatted='{Formatted}'",
                senderName, teamOnly, groupTag, style.ChatColor, style.TagColor, style.NameColor, teamOnly ? "team" : "all", formatted);
        }

        foreach (var target in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
        {
            if (teamOnly && target.Controller.TeamNum != senderTeam)
            {
                continue;
            }

            target.SendChat(formatted);
        }
    }

    private string ResolveChatGroupTag(IPlayer player)
    {
        var steamId = player.SteamID;
        if (_lastKnownAdminTags.TryGetValue(steamId, out var knownTag) && !string.IsNullOrWhiteSpace(knownTag))
        {
            return knownTag.Trim();
        }

        try
        {
            var admin = _adminDbManager.GetAdminFromCache(steamId);
            if (DebugSettings.LoggingEnabled)
            {
                _core.Logger.LogInformationIfEnabled(
                    "[CS2Admin][Debug][ChatTag] resolve | steamid={SteamId} cacheHit={Cache} groupList={Groups}",
                    steamId, admin != null, admin?.GroupList != null ? string.Join(",", admin.GroupList) : "-");
            }
            if (admin != null)
            {
                string primaryGroup = string.Empty;
                if (admin.GroupList.Count > 0)
                    primaryGroup = _groupDbManager.GetPrimaryGroupNameSync(admin.GroupList) ?? admin.GroupList[0];

                if (!string.IsNullOrWhiteSpace(primaryGroup))
                {
                    var resolved = primaryGroup.Trim();
                    _lastKnownAdminTags[steamId] = resolved;
                    return resolved;
                }
            }
        }
        catch (Exception ex)
        {
            if (DebugSettings.LoggingEnabled)
                _core.Logger.LogWarningIfEnabled("[CS2Admin][Debug][ChatTag] resolve-cache-error | steamid={SteamId} err={Msg}", steamId, ex.Message);
        }

        var fromScoreboard = ExtractTagFromScoreboard(player.Controller.Clan);
        if (!string.IsNullOrWhiteSpace(fromScoreboard))
        {
            if (DebugSettings.LoggingEnabled)
                _core.Logger.LogInformationIfEnabled("[CS2Admin][Debug][ChatTag] resolve-from-scoreboard | steamid={SteamId} tag='{Tag}'", steamId, fromScoreboard);
            return fromScoreboard;
        }

        if (_core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot))
        {
            if (DebugSettings.LoggingEnabled)
                _core.Logger.LogInformationIfEnabled("[CS2Admin][Debug][ChatTag] resolve-from-adminroot | steamid={SteamId}", steamId);
            return "ADMIN";
        }

        if (DebugSettings.LoggingEnabled)
            _core.Logger.LogInformationIfEnabled("[CS2Admin][Debug][ChatTag] resolve-fallback | steamid={SteamId} tag='{Tag}'", steamId, _chatTagConfigManager.Config.PlayerTag);
        return _chatTagConfigManager.Config.PlayerTag;
    }

    private static string ExtractTagFromScoreboard(string? rawClan)
    {
        if (string.IsNullOrWhiteSpace(rawClan))
        {
            return string.Empty;
        }

        var normalized = rawClan.Replace("\u200B", string.Empty).Trim();
        var segments = normalized.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(segments[0], @"^#?\d+$"))
        {
            return segments[1].Trim();
        }

        if (segments.Length >= 1)
        {
            return segments[^1].Trim();
        }

        return string.Empty;
    }

    private string FormatColors(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Replace("[default]", "\x01", StringComparison.OrdinalIgnoreCase)
                    .Replace("[white]", "\x01", StringComparison.OrdinalIgnoreCase)
                    .Replace("[darkred]", "\x02", StringComparison.OrdinalIgnoreCase)
                    .Replace("[lightpurple]", "\x03", StringComparison.OrdinalIgnoreCase)
                    .Replace("[green]", "\x04", StringComparison.OrdinalIgnoreCase)
                    .Replace("[lime]", "\x05", StringComparison.OrdinalIgnoreCase)
                    .Replace("[lightgreen]", "\x05", StringComparison.OrdinalIgnoreCase)
                    .Replace("[red]", "\x07", StringComparison.OrdinalIgnoreCase)
                    .Replace("[gray]", "\x08", StringComparison.OrdinalIgnoreCase)
                    .Replace("[grey]", "\x08", StringComparison.OrdinalIgnoreCase)
                    .Replace("[yellow]", "\x09", StringComparison.OrdinalIgnoreCase)
                    .Replace("[gold]", "\x10", StringComparison.OrdinalIgnoreCase)
                    .Replace("[silver]", "\x0A", StringComparison.OrdinalIgnoreCase)
                    .Replace("[lightblue]", "\x0B", StringComparison.OrdinalIgnoreCase)
                    .Replace("[blue]", "\x0C", StringComparison.OrdinalIgnoreCase)
                    .Replace("[darkblue]", "\x0C", StringComparison.OrdinalIgnoreCase)
                    .Replace("[bluegrey]", "\x0A", StringComparison.OrdinalIgnoreCase)
                    .Replace("[purple]", "\x0E", StringComparison.OrdinalIgnoreCase)
                    .Replace("[magenta]", "\x0E", StringComparison.OrdinalIgnoreCase)
                    .Replace("[lightred]", "\x0F", StringComparison.OrdinalIgnoreCase)
                    .Replace("[olive]", "\x09", StringComparison.OrdinalIgnoreCase);
    }

    private void CheckExpiredPunishments()
    {
        if (!_databaseReady)
            return;

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var steamId = player.SteamID;
            var playerId = player.PlayerID;

            var cachedMute = _sanctionStateService.GetCachedMute(steamId) ?? _muteManager.GetActiveMuteFromCache(steamId);
            if (cachedMute != null && cachedMute.IsExpired)
            {
                player.SendChat($" \x04{LocalizerHelper.Get(_core, "prefix")}\x01 {LocalizerHelper.Get(_core, "mute_expired")}");
                player.VoiceFlags = VoiceFlagValue.Normal;
                _muteManager.ClearCache();
                _sanctionStateService.Invalidate(steamId);
                _gagWarnTimestamps.TryRemove(playerId, out _);
            }

            var cachedGag = _sanctionStateService.GetCachedGag(steamId) ?? _gagManager.GetActiveGagFromCache(steamId);
            if (cachedGag != null && cachedGag.IsExpired)
            {
                player.SendChat($" \x04{LocalizerHelper.Get(_core, "prefix")}\x01 {LocalizerHelper.Get(_core, "gag_expired")}");
                _gagManager.ClearCache();
                _sanctionStateService.Invalidate(steamId);
                _gagWarnTimestamps.TryRemove(playerId, out _);
            }
        }

        _ = Task.Run(async () =>
        {
            await _banManager.CleanupExpiredBansAsync();
            await _muteManager.UpdateExpiredMutesAsync();
            await _gagManager.CleanupExpiredGagsAsync();
            await _warnManager.UpdateExpiredWarnsAsync();
        });
    }

    private void EnforceOnlineBans()
    {
        if (Interlocked.Exchange(ref _isBanEnforcementRunning, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var onlinePlayers = _core.PlayerManager
                    .GetAllPlayers()
                    .Where(p => p.IsValid && !p.IsFakeClient)
                    .Select(p => (p.PlayerID, p.SteamID, p.IPAddress))
                    .ToList();

                foreach (var player in onlinePlayers)
                {
                    var ban = await _banManager.GetActiveBanAsync(player.SteamID, player.IPAddress, _multiServerConfig.Enabled)
                              ?? await _banManager.GetActiveBanForEnforcementAsync(player.SteamID, player.IPAddress);

                    if (ban == null || !ban.IsActive)
                        continue;

                    var reason = BuildBanKickReason(ban);
                    var pid = player.PlayerID;

                    _core.Scheduler.NextTick(() =>
                    {
                        var current = _core.PlayerManager.GetPlayer(pid);
                        if (current?.IsValid == true)
                            current.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
                    });
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error during periodic ban enforcement: {Message}", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _isBanEnforcementRunning, 0);
            }
        });
    }

    private string BuildBanKickReason(Ban ban)
    {
        try
        {
            if (ban.IsPermanent)
                return LocalizerHelper.Get(_core, "ban_kick_reason_permanent", ban.Reason);

            var remainingMinutes = (int)Math.Ceiling(Math.Max(1, ban.TimeRemaining?.TotalMinutes ?? 1));
            return LocalizerHelper.Get(_core, "ban_kick_reason_minutes", remainingMinutes, ban.Reason);
        }
        catch
        {
            if (ban.IsPermanent)
                return $"You are permanently banned. Reason: {ban.Reason}";

            var remainingMinutes = (int)Math.Ceiling(Math.Max(1, ban.TimeRemaining?.TotalMinutes ?? 1));
            return $"You are banned for {remainingMinutes} more minutes. Reason: {ban.Reason}";
        }
    }
}
