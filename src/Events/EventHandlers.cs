using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace CS2_Admin.Events;

public class EventHandlers
{
    private readonly ISwiftlyCore _core;
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminManager;
    private readonly GroupDbManager _groupManager;
    private readonly PlayerIpDbManager _playerIpManager;
    private readonly RecentPlayersTracker _recentPlayersTracker;
    private readonly PermissionsConfig _permissions;
    private readonly TagsConfig _tags;
    private readonly CommandsConfig _commands;
    private readonly MultiServerConfig _multiServerConfig;
    
    private readonly Dictionary<int, DateTime> _muteWarnTimestamps = new();
    private readonly Dictionary<int, DateTime> _gagWarnTimestamps = new();
    private readonly Dictionary<int, RecentPlayerInfo> _connectedPlayerSnapshots = new();
    private readonly Dictionary<ulong, string> _lastKnownAdminTags = new();
    
    private Guid _chatHookGuid = Guid.Empty;
    private CancellationTokenSource? _expiryCheckCts;
    private CancellationTokenSource? _banEnforceCts;
    private CancellationTokenSource? _tagRefreshCts;
    private volatile bool _databaseReady;
    private int _isBanEnforcementRunning;

    public event Action<int>? OnPlayerDisconnected;

    public EventHandlers(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminManager,
        GroupDbManager groupManager,
        PlayerIpDbManager playerIpManager,
        RecentPlayersTracker recentPlayersTracker,
        PermissionsConfig permissions,
        TagsConfig tags,
        CommandsConfig commands,
        MultiServerConfig multiServerConfig)
    {
        _core = core;
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminManager = adminManager;
        _groupManager = groupManager;
        _playerIpManager = playerIpManager;
        _recentPlayersTracker = recentPlayersTracker;
        _permissions = permissions;
        _tags = tags;
        _commands = commands;
        _multiServerConfig = multiServerConfig;
    }
    
    public void RegisterHooks()
    {
        // Hook into chat to block gagged players
        _chatHookGuid = _core.Command.HookClientChat(OnClientChat);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] Chat hook registered");
        
        // Start periodic expiry check (every 30 seconds)
        _expiryCheckCts = _core.Scheduler.RepeatBySeconds(30f, CheckExpiredPunishments);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] Expiry check timer started");

        // Enforce active bans periodically so bypass through late authorize timing cannot persist.
        _banEnforceCts = _core.Scheduler.RepeatBySeconds(5f, EnforceOnlineBans);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] Ban enforcement timer started");

        if (_tags.Enabled)
        {
            _tagRefreshCts = _core.Scheduler.RepeatBySeconds(15f, () =>
            {
                if (_databaseReady)
                {
                    _ = RefreshTagsForAllOnlinePlayersAsync();
                }
            });
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Tag refresh timer started");
        }
    }

    public void SetDatabaseReady(bool ready)
    {
        _databaseReady = ready;
        if (!ready)
        {
            return;
        }

        _ = HandleDatabaseReadyAsync();
    }
    
    public void UnregisterHooks()
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

        _tagRefreshCts?.Cancel();
        _tagRefreshCts = null;
    }
    
    private void CheckExpiredPunishments()
    {
        if (!_databaseReady)
        {
            return;
        }

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var steamId = player.SteamID;
            var playerId = player.PlayerID;
            
            // Check mute expiry
            var cachedMute = _muteManager.GetActiveMuteFromCache(steamId);
            if (cachedMute != null && cachedMute.IsExpired)
            {
                // Mute has expired - notify player and remove mute
                player.SendChat($" \x04{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["mute_expired"]}");
                player.VoiceFlags = VoiceFlagValue.Normal;
                _muteManager.ClearCache();
                _muteWarnTimestamps.Remove(playerId);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Mute expired for player {SteamId}", steamId);
            }
            
            // Check gag expiry
            var cachedGag = _gagManager.GetActiveGagFromCache(steamId);
            if (cachedGag != null && cachedGag.IsExpired)
            {
                // Gag has expired - notify player
                player.SendChat($" \x04{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gag_expired"]}");
                _gagManager.ClearCache();
                _gagWarnTimestamps.Remove(playerId);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Gag expired for player {SteamId}", steamId);
            }
        }
        
        // Cleanup expired punishments in database
        _ = Task.Run(async () =>
        {
            await _banManager.CleanupExpiredBansAsync();
            await _muteManager.UpdateExpiredMutesAsync();
            await _gagManager.CleanupExpiredGagsAsync();
            await _warnManager.UpdateExpiredWarnsAsync();
        });
    }

    public void OnClientSteamAuthorize(IOnClientSteamAuthorizeEvent @event)
    {
        var playerId = @event.PlayerId;
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || !player.IsValid)
            return;

        var steamId = player.SteamID;
        var playerName = player.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
        var playerIp = player.IPAddress;
        _connectedPlayerSnapshots[playerId] = new RecentPlayerInfo(
            steamId,
            playerName,
            playerIp,
            DateTime.UtcNow);
        ScheduleDeferredBanRecheck(playerId, 1.5f);
        ScheduleDeferredBanRecheck(playerId, 5f);

        _core.Logger.LogInformationIfEnabled("[CS2_Admin] OnClientSteamAuthorize fired for {Name} ({SteamId})", playerName, steamId);

        _ = Task.Run(async () =>
        {
            try
            {
                // Persist latest SteamID <-> IP mapping early, even if player gets kicked by ban checks.
                await _playerIpManager.UpsertPlayerIpAsync(
                    steamId,
                    playerName,
                    playerIp);

                // Match T3 behavior: cleanup before authorize check.
                await _banManager.CleanupExpiredBansAsync();

                // Check if player is banned
                var isKickedByBan = await TryKickIfBannedAsync(playerId, steamId, playerIp, "authorize-initial");
                if (isKickedByBan)
                {
                    return;
                }

                if (!_databaseReady)
                {
                    _core.Logger.LogWarningIfEnabled("[CS2_Admin] Database is not fully ready. Skipping non-ban authorize steps for {SteamId}.", steamId);
                    return;
                }

                // Check if player is muted and apply voice mute
                var mute = await _muteManager.GetActiveMuteAsync(steamId);
                if (mute != null && mute.IsActive)
                {
                    _core.Scheduler.NextTick(() =>
                    {
                        var mutedPlayer = _core.PlayerManager.GetPlayer(playerId);
                        if (mutedPlayer?.IsValid != true)
                        {
                            return;
                        }

                        mutedPlayer.VoiceFlags = VoiceFlagValue.Muted;
                        _core.Logger.LogInformationIfEnabled("[CS2_Admin] Applied mute to player {SteamId}", steamId);
                    });
                }

                // Load admin permissions
                var admin = await _adminManager.GetAdminAsync(steamId);
                if (admin != null && admin.IsActive)
                {
                    var flags = await _adminManager.GetEffectiveFlagsAsync(steamId);
                    if (flags.Length == 0)
                    {
                        var fallback = _groupManager.ExpandFlags(admin.GroupList)
                            .Where(f => !string.IsNullOrWhiteSpace(f))
                            .Select(f => f.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        flags = fallback;
                    }

                    var hasRoot = flags.Any(f => string.Equals(f.Trim(), _permissions.AdminRoot, StringComparison.OrdinalIgnoreCase));
                    foreach (var flag in flags)
                    {
                        _core.Permission.AddPermission(steamId, flag.Trim());
                    }

                    if (hasRoot)
                    {
                        foreach (var bypassPermission in _permissions.RootBypassPermissions)
                        {
                            if (!string.IsNullOrWhiteSpace(bypassPermission))
                            {
                                _core.Permission.AddPermission(steamId, bypassPermission.Trim());
                            }
                        }
                    }

                    _core.Logger.LogInformationIfEnabled("[CS2_Admin] Loaded admin permissions for {SteamId}: {Flags}", steamId, string.Join(",", flags));
                }

                if (_tags.Enabled)
                {
                    var resolvedTag = await ResolveTagForPlayerAsync(steamId, admin);
                    _core.Scheduler.NextTick(() =>
                    {
                        var target = _core.PlayerManager.GetPlayer(playerId);
                        if (target?.IsValid == true)
                        {
                            PlayerUtils.SetScoreTagReliable(_core, target.PlayerID, resolvedTag);
                        }
                    });

                    // Re-apply shortly after authorize to prevent transient DB/cache timing from leaving PLAYER tag.
                    ScheduleDeferredTagRefresh(playerId, steamId, 2f);
                    ScheduleDeferredTagRefresh(playerId, steamId, 8f);
                }

                // After permissions are loaded, notify all online admins about this player's history
                await SendJoinPunishmentSummaryAsync(playerId, steamId);
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error in OnClientSteamAuthorize: {Message}", ex.Message);
            }
        });
    }

    private void ScheduleDeferredBanRecheck(int playerId, float delaySeconds)
    {
        _core.Scheduler.DelayBySeconds(delaySeconds, () =>
        {
            var player = _core.PlayerManager.GetPlayer(playerId);
            if (player?.IsValid != true)
            {
                return;
            }

            var steamId = player.SteamID;
            var ipAddress = player.IPAddress;
            _ = Task.Run(async () => await TryKickIfBannedAsync(playerId, steamId, ipAddress, $"authorize-delayed-{delaySeconds:0.##}s"));
        });
    }

    private async Task<bool> TryKickIfBannedAsync(int playerId, ulong steamId, string? ipAddress, string source)
    {
        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] ban check source={Source} playerId={PlayerId} steamid={SteamId} ip={Ip}", source, playerId, steamId, ipAddress ?? "-");

        var ban = await _banManager.GetActiveBanAsync(steamId, ipAddress, _multiServerConfig.Enabled)
                  ?? await _banManager.GetActiveBanForEnforcementAsync(steamId, ipAddress);
        if (ban == null || !ban.IsActive)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] ban check clear source={Source} steamid={SteamId}", source, steamId);
            return false;
        }

        var stillOnline = await RunOnMainThreadAsync(() =>
        {
            var current = _core.PlayerManager.GetPlayer(playerId);
            return current?.IsValid == true;
        });
        if (!stillOnline)
        {
            return false;
        }

        var reason = BuildBanKickReason(ban);

        _core.Scheduler.NextTick(() =>
        {
            var current = _core.PlayerManager.GetPlayer(playerId);
            if (current?.IsValid == true)
            {
                current.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
            }
        });

        _core.Logger.LogInformationIfEnabled("[CS2_Admin] Banned player {SteamId} kicked from source={Source}.", steamId, source);
        return true;
    }

    private string BuildBanKickReason(Ban ban)
    {
        try
        {
            if (ban.IsPermanent)
            {
                return PluginLocalizer.Get(_core)["ban_kick_reason_permanent", ban.Reason];
            }

            var remainingMinutes = (int)Math.Ceiling(Math.Max(1, ban.TimeRemaining?.TotalMinutes ?? 1));
            return PluginLocalizer.Get(_core)["ban_kick_reason_minutes", remainingMinutes, ban.Reason];
        }
        catch
        {
            if (ban.IsPermanent)
            {
                return $"You are permanently banned. Reason: {ban.Reason}";
            }

            var remainingMinutes = (int)Math.Ceiling(Math.Max(1, ban.TimeRemaining?.TotalMinutes ?? 1));
            return $"You are banned for {remainingMinutes} more minutes. Reason: {ban.Reason}";
        }
    }

    private void EnforceOnlineBans()
    {
        if (Interlocked.Exchange(ref _isBanEnforcementRunning, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var onlinePlayers = await RunOnMainThreadAsync(() =>
                    _core.PlayerManager
                        .GetAllPlayers()
                        .Where(p => p.IsValid && !p.IsFakeClient)
                        .Select(p => (p.PlayerID, p.SteamID, p.IPAddress))
                        .ToList());

                foreach (var player in onlinePlayers)
                {
                    await TryKickIfBannedAsync(player.PlayerID, player.SteamID, player.IPAddress, "periodic-enforce");
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

    private async Task SendJoinPunishmentSummaryAsync(int playerId, ulong steamId)
    {
        try
        {
            var totalBans = await _banManager.GetTotalBansAsync(steamId);
            var totalMutes = await _muteManager.GetTotalMutesAsync(steamId);
            var totalGags = await _gagManager.GetTotalGagsAsync(steamId);
            var totalWarns = await _warnManager.GetTotalWarnsAsync(steamId);

            var joiningState = await RunOnMainThreadAsync(() =>
            {
                var player = _core.PlayerManager.GetPlayer(playerId);
                return (player?.IPAddress, player?.Controller.PlayerName);
            });
            var activeBan = await _banManager.GetActiveBanAsync(steamId, joiningState.IPAddress, _multiServerConfig.Enabled);
            var activeMute = await _muteManager.GetActiveMuteAsync(steamId);
            var activeGag = await _gagManager.GetActiveGagAsync(steamId);
            var activeWarn = await _warnManager.GetActiveWarnAsync(steamId);
            var joiningAdmin = await _adminManager.GetAdminAsync(steamId);
            var shouldShowToJoiningPlayer = joiningAdmin != null && joiningAdmin.IsActive;
            if (!shouldShowToJoiningPlayer)
            {
                var joiningFlags = await _adminManager.GetEffectiveFlagsAsync(steamId);
                shouldShowToJoiningPlayer = joiningFlags.Any(f => !string.IsNullOrWhiteSpace(f));
            }

            // Runtime permission cache may lag behind DB on startup/hot-reload.
            // Build a DB-based fallback list so active admins still receive summaries.
            var dbAdminViewers = new HashSet<ulong>();
            var onlineSnapshots = await RunOnMainThreadAsync(() =>
                _core.PlayerManager
                    .GetAllPlayers()
                    .Where(p => p.IsValid && !p.IsFakeClient)
                    .Select(p => p.SteamID)
                    .Distinct()
                    .ToList());
            foreach (var viewerSteamId in onlineSnapshots)
            {
                var viewerAdmin = await _adminManager.GetAdminAsync(viewerSteamId);
                if (viewerAdmin != null && viewerAdmin.IsActive)
                {
                    dbAdminViewers.Add(viewerSteamId);
                }
            }

            var playerName = joiningState.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];

            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Preparing join summary for {Name} ({SteamId})", playerName, steamId);

            var summary = $"Bans:\x10{totalBans}\x01 Mutes:\x10{totalMutes}\x01 Gags:\x10{totalGags}\x01 Warns:\x10{totalWarns}\x01";

            var activeParts = new List<string>();
            if (activeBan != null && activeBan.IsActive) activeParts.Add(PluginLocalizer.Get(_core)["join_active_ban"]);
            if (activeMute != null && activeMute.IsActive) activeParts.Add(PluginLocalizer.Get(_core)["join_active_mute"]);
            if (activeGag != null && activeGag.IsActive) activeParts.Add(PluginLocalizer.Get(_core)["join_active_gag"]);
            if (activeWarn != null && activeWarn.IsActive) activeParts.Add(PluginLocalizer.Get(_core)["join_active_warn"]);

            var activeLine = activeParts.Count > 0
                ? $"\x10{string.Join(", ", activeParts)}\x01"
                : $"\x09{PluginLocalizer.Get(_core)["join_active_none"]}\x01";

            var message = $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["join_summary", playerName, steamId, totalBans, totalMutes, totalGags, totalWarns, activeLine]}";

            _core.Scheduler.NextTick(() =>
            {
                int notified = 0;
                var notifiedSteamIds = new HashSet<ulong>();

                foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
                {
                    var canViewSummary =
                        (!string.IsNullOrWhiteSpace(_permissions.ListPlayers) && _core.Permission.PlayerHasPermission(p.SteamID, _permissions.ListPlayers)) ||
                        (!string.IsNullOrWhiteSpace(_permissions.AdminMenu) && _core.Permission.PlayerHasPermission(p.SteamID, _permissions.AdminMenu)) ||
                        _core.Permission.PlayerHasPermission(p.SteamID, _permissions.AdminRoot) ||
                        dbAdminViewers.Contains(p.SteamID);

                    if (canViewSummary)
                    {
                        notified++;
                        notifiedSteamIds.Add(p.SteamID);
                        p.SendChat(message);
                    }
                }

                if (shouldShowToJoiningPlayer)
                {
                    var joiningPlayer = _core.PlayerManager.GetPlayer(playerId);
                    if (joiningPlayer?.IsValid == true && !notifiedSteamIds.Contains(joiningPlayer.SteamID))
                    {
                        joiningPlayer.SendChat(message);
                        notified++;
                    }
                }

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Join summary for {Name} ({SteamId}) sent to {Count} admins.", playerName, steamId, notified);
            });
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending join punishment summary: {Message}", ex.Message);
        }
    }

    public void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var playerId = @event.PlayerId;
        
        _muteWarnTimestamps.Remove(playerId);
        _gagWarnTimestamps.Remove(playerId);

        RecentPlayerInfo? snapshot = null;
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player?.IsValid == true)
        {
            snapshot = new RecentPlayerInfo(
                player.SteamID,
                player.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                player.IPAddress,
                DateTime.UtcNow);
        }
        else if (_connectedPlayerSnapshots.TryGetValue(playerId, out var cached))
        {
            snapshot = cached with { LastSeenAt = DateTime.UtcNow };
        }

        if (snapshot != null)
        {
            _recentPlayersTracker.Add(snapshot);
            _ = Task.Run(async () =>
            {
                await _playerIpManager.UpsertPlayerIpAsync(snapshot.SteamId, snapshot.Name, snapshot.IpAddress);
            });
        }

        _connectedPlayerSnapshots.Remove(playerId);

        OnPlayerDisconnected?.Invoke(playerId);
    }

    public HookResult OnPlayerSpeak(int playerId)
    {
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player?.IsValid != true)
            return HookResult.Continue;

        var steamId = player.SteamID;
        var cachedMute = _muteManager.GetActiveMuteFromCache(steamId);

        if (cachedMute != null && cachedMute.IsActive)
        {
            // Show warning message with cooldown
            bool shouldShowMessage = !_muteWarnTimestamps.ContainsKey(playerId) ||
                                   (DateTime.UtcNow - _muteWarnTimestamps[playerId]).TotalSeconds >= 5;

            if (shouldShowMessage)
            {
                if (cachedMute.IsPermanent)
                {
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["muted_warning_permanent"]}");
                }
                else
                {
                    var remainingMinutes = (int)Math.Ceiling(cachedMute.TimeRemaining!.Value.TotalMinutes);
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["muted_warning_minutes", remainingMinutes]}");
                }
                _muteWarnTimestamps[playerId] = DateTime.UtcNow;
            }

            player.VoiceFlags = VoiceFlagValue.Muted;
            return HookResult.Stop;
        }

        // Check database asynchronously for cache miss
        _ = Task.Run(async () =>
        {
            var mute = await _muteManager.GetActiveMuteAsync(steamId);
            _core.Scheduler.NextTick(() =>
            {
                var current = _core.PlayerManager.GetPlayer(playerId);
                if (current?.IsValid != true)
                {
                    return;
                }

                current.VoiceFlags = mute != null && mute.IsActive
                    ? VoiceFlagValue.Muted
                    : VoiceFlagValue.Normal;
            });
        });

        return HookResult.Continue;
    }

    public bool CheckGag(ulong steamId, int playerId, IPlayer player)
    {
        var cachedGag = _gagManager.GetActiveGagFromCache(steamId);

        if (cachedGag != null && cachedGag.IsActive)
        {
            // Show warning message with cooldown
            bool shouldShowMessage = !_gagWarnTimestamps.ContainsKey(playerId) ||
                                   (DateTime.UtcNow - _gagWarnTimestamps[playerId]).TotalSeconds >= 5;

            if (shouldShowMessage)
            {
                if (cachedGag.IsPermanent)
                {
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_warning_permanent"]}");
                }
                else
                {
                    var remainingMinutes = (int)Math.Ceiling(cachedGag.TimeRemaining!.Value.TotalMinutes);
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_warning_minutes", remainingMinutes]}");
                }
                _gagWarnTimestamps[playerId] = DateTime.UtcNow;
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] gag block (voice/chat precheck) steamid={SteamId} playerId={PlayerId}", steamId, playerId);
            }

            return true; // Block message
        }

        // Check database asynchronously for cache miss
        _ = Task.Run(async () =>
        {
            await _gagManager.GetActiveGagAsync(steamId);
        });

        return false;
    }

    public HookResult OnRoundStart(EventRoundStart @event)
    {
        // Clear warning timestamps on round start
        _muteWarnTimestamps.Clear();
        _gagWarnTimestamps.Clear();

        if (_databaseReady)
        {
            _ = RefreshAdminStateForAllOnlinePlayersAsync();
        }

        return HookResult.Continue;
    }

    public async Task RefreshAdminStateForAllOnlinePlayersAsync()
    {
        if (!_databaseReady)
        {
            return;
        }

        var snapshots = await RunOnMainThreadAsync(() =>
            _core.PlayerManager
                .GetAllPlayers()
                .Where(p => p.IsValid && !p.IsFakeClient)
                .Select(p => (p.PlayerID, p.SteamID))
                .ToList());

        await RefreshAdminStateForSnapshotsAsync(snapshots);
    }

    private async Task RefreshAdminStateForSnapshotsAsync(IReadOnlyList<(int PlayerID, ulong SteamID)> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        // Warm group cache once to make group->flag and tag fallback deterministic.
        await _groupManager.GetAllGroupsAsync();
        var allAdmins = await _adminManager.GetAllAdminsAsync();
        var adminsBySteamId = allAdmins.ToDictionary(a => a.SteamId, a => a);

        var managedPermissions = await BuildManagedPermissionsAsync();
        var resolvedTags = new Dictionary<int, string>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            foreach (var permission in managedPermissions)
            {
                _core.Permission.RemovePermission(snapshot.SteamID, permission);
            }

            var admin = await _adminManager.GetAdminAsync(snapshot.SteamID);
            if (admin == null && adminsBySteamId.TryGetValue(snapshot.SteamID, out var scannedAdmin))
            {
                admin = scannedAdmin;
            }
            var effectiveFlags = await _adminManager.GetEffectiveFlagsAsync(snapshot.SteamID);
            if (effectiveFlags.Length == 0 && admin != null && admin.IsActive)
            {
                effectiveFlags = _groupManager.ExpandFlags(admin.GroupList)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => f.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var hasRoot = false;
            foreach (var flag in effectiveFlags)
            {
                if (string.IsNullOrWhiteSpace(flag))
                {
                    continue;
                }

                var normalizedFlag = flag.Trim();
                _core.Permission.AddPermission(snapshot.SteamID, normalizedFlag);
                if (string.Equals(normalizedFlag, _permissions.AdminRoot, StringComparison.OrdinalIgnoreCase))
                {
                    hasRoot = true;
                }
            }

            if (hasRoot)
            {
                foreach (var bypassPermission in _permissions.RootBypassPermissions)
                {
                    if (!string.IsNullOrWhiteSpace(bypassPermission))
                    {
                        _core.Permission.AddPermission(snapshot.SteamID, bypassPermission.Trim());
                    }
                }
            }

            if (_tags.Enabled)
            {
                resolvedTags[snapshot.PlayerID] = await ResolveTagForPlayerAsync(snapshot.SteamID, admin);
            }
        }

        if (!_tags.Enabled)
        {
            return;
        }

        _core.Scheduler.NextTick(() =>
        {
            foreach (var pair in resolvedTags)
            {
                PlayerUtils.SetScoreTagReliable(_core, pair.Key, pair.Value);
            }
        });
    }

    public Task RefreshTagsForAllOnlinePlayersAsync()
    {
        return RefreshAdminStateForAllOnlinePlayersAsync();
    }

    private async Task<string> ResolveTagForPlayerAsync(ulong steamId, Admin? admin = null)
    {
        if (!_tags.Enabled)
        {
            return _tags.PlayerTag;
        }

        admin ??= await _adminManager.GetAdminAsync(steamId);
        if (admin == null)
        {
            admin = (await _adminManager.GetAllAdminsAsync())
                .FirstOrDefault(a => a.SteamId == steamId && a.IsActive);
        }

        if (admin == null || !admin.IsActive)
        {
            var hasRootPermission = _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
            if (hasRootPermission)
            {
                if (_lastKnownAdminTags.TryGetValue(steamId, out var knownTag) && !string.IsNullOrWhiteSpace(knownTag))
                {
                    return knownTag;
                }

                return "ADMIN";
            }

            _lastKnownAdminTags.Remove(steamId);
            return _tags.PlayerTag;
        }

        var tag = await _adminManager.GetPrimaryGroupNameAsync(steamId);
        if (string.IsNullOrWhiteSpace(tag))
        {
            tag = _groupManager.GetPrimaryGroupNameSync(admin.GroupList);
        }

        if (string.IsNullOrWhiteSpace(tag) && admin.GroupList.Count > 0)
        {
            tag = admin.GroupList[0];
        }

        if (string.IsNullOrWhiteSpace(tag) && _lastKnownAdminTags.TryGetValue(steamId, out var cachedTag))
        {
            tag = cachedTag;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            tag = "ADMIN";
        }

        _lastKnownAdminTags[steamId] = tag;
        return tag;
    }

    private void ScheduleDeferredTagRefresh(int playerId, ulong steamId, float delaySeconds)
    {
        if (!_tags.Enabled)
        {
            return;
        }

        _core.Scheduler.DelayBySeconds(delaySeconds, () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_databaseReady)
                    {
                        return;
                    }

                    var stillOnline = await RunOnMainThreadAsync(() =>
                    {
                        var live = _core.PlayerManager.GetPlayer(playerId);
                        return live?.IsValid == true && live.SteamID == steamId;
                    });
                    if (!stillOnline)
                    {
                        return;
                    }

                    var admin = await _adminManager.GetAdminAsync(steamId);
                    var resolvedTag = await ResolveTagForPlayerAsync(steamId, admin);

                    _core.Scheduler.NextTick(() =>
                    {
                        var live = _core.PlayerManager.GetPlayer(playerId);
                        if (live?.IsValid == true && live.SteamID == steamId)
                        {
                            PlayerUtils.SetScoreTagReliable(_core, live.PlayerID, resolvedTag);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _core.Logger.LogWarningIfEnabled("[CS2_Admin] Deferred tag refresh failed for {SteamId}: {Message}", steamId, ex.Message);
                }
            });
        });
    }
    
    /// <summary>
    /// Chat hook handler - blocks gagged players from chatting
    /// </summary>
    public HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        _ = _commands;
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        // Gagged players can still execute commands; only normal chat is restricted.
        if (!string.IsNullOrWhiteSpace(text) && (text.StartsWith("!") || text.StartsWith("/")))
        {
            return HookResult.Continue;
        }
        
        var steamId = player.SteamID;
        
        // Check gag from cache first
        var cachedGag = _gagManager.GetActiveGagFromCache(steamId);
        if (cachedGag != null && cachedGag.IsActive)
        {
            // Show warning message with cooldown
            bool shouldShowMessage = !_gagWarnTimestamps.ContainsKey(playerId) ||
                                   (DateTime.UtcNow - _gagWarnTimestamps[playerId]).TotalSeconds >= 5;

            if (shouldShowMessage)
            {
                if (cachedGag.IsPermanent)
                {
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_chat_warning_permanent"]}");
                }
                else
                {
                    var remainingMinutes = (int)Math.Ceiling(cachedGag.TimeRemaining!.Value.TotalMinutes);
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_chat_warning_minutes", remainingMinutes]}");
                }
                _gagWarnTimestamps[playerId] = DateTime.UtcNow;
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] gag chat blocked steamid={SteamId} playerId={PlayerId}", steamId, playerId);
            }

            var playerName = player.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
            var strippedText = string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
            var preview = strippedText.Length <= 220 ? strippedText : $"{strippedText[..220]}...";

            foreach (var admin in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
            {
                var canSee =
                    _core.Permission.PlayerHasPermission(admin.SteamID, _permissions.AdminRoot) ||
                    (!string.IsNullOrWhiteSpace(_permissions.AdminMenu) && _core.Permission.PlayerHasPermission(admin.SteamID, _permissions.AdminMenu)) ||
                    (!string.IsNullOrWhiteSpace(_permissions.ListPlayers) && _core.Permission.PlayerHasPermission(admin.SteamID, _permissions.ListPlayers));

                if (!canSee)
                {
                    continue;
                }

                admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_chat_admin_visible", playerName, preview]}");
            }

            return HookResult.Stop; // Block the chat message
        }
        
        // Check database asynchronously for cache miss (for next time)
        _ = Task.Run(async () =>
        {
            await _gagManager.GetActiveGagAsync(steamId);
        });

        return HookResult.Continue;
    }

    private async Task<HashSet<string>> BuildManagedPermissionsAsync()
    {
        var managedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var groups = await _groupManager.GetAllGroupsAsync();
        foreach (var group in groups)
        {
            foreach (var permission in SplitPermissions(group.Flags))
            {
                managedPermissions.Add(permission);
            }
        }

        var admins = await _adminManager.GetAllAdminsAsync();
        foreach (var admin in admins)
        {
            foreach (var permission in SplitPermissions(admin.Flags))
            {
                managedPermissions.Add(permission);
            }
        }

        managedPermissions.Add(_permissions.AdminRoot);

        foreach (var bypassPermission in _permissions.RootBypassPermissions)
        {
            if (!string.IsNullOrWhiteSpace(bypassPermission))
            {
                managedPermissions.Add(bypassPermission.Trim());
            }
        }

        return managedPermissions;
    }

    private static IEnumerable<string> SplitPermissions(string rawPermissions)
    {
        return rawPermissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));
    }

    private async Task HandleDatabaseReadyAsync()
    {
        try
        {
            // Players may have authorized before DB became ready.
            // Re-apply admin state and emit summaries once DB is available.
            await RefreshAdminStateForAllOnlinePlayersAsync();

            var snapshots = await RunOnMainThreadAsync(() =>
                _core.PlayerManager
                    .GetAllPlayers()
                    .Where(p => p.IsValid && !p.IsFakeClient)
                    .Select(p => (p.PlayerID, p.SteamID))
                    .ToList());

            foreach (var snapshot in snapshots)
            {
                await SendJoinPunishmentSummaryAsync(snapshot.PlayerID, snapshot.SteamID);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error while processing online players after DB became ready: {Message}", ex.Message);
        }
    }

    private Task<T> RunOnMainThreadAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _core.Scheduler.NextTick(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}


