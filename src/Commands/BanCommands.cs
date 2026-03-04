using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace CS2_Admin.Commands;

public class BanCommands
{
    private readonly ISwiftlyCore _core;
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly PlayerIpDbManager _playerIpDbManager;
    private readonly RecentPlayersTracker _recentPlayersTracker;
    private readonly DiscordWebhook _discord;
    private readonly PermissionsConfig _permissions;
    private readonly CommandsConfig _commands;
    private readonly MessagesConfig _messagesConfig;
    private readonly SanctionMenuConfig _sanctions;
    private readonly MultiServerConfig _multiServerConfig;

    public BanCommands(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        PlayerIpDbManager playerIpDbManager,
        RecentPlayersTracker recentPlayersTracker,
        DiscordWebhook discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        MessagesConfig messagesConfig,
        SanctionMenuConfig sanctions,
        MultiServerConfig multiServerConfig)
    {
        _core = core;
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _adminLogManager = adminLogManager;
        _playerIpDbManager = playerIpDbManager;
        _recentPlayersTracker = recentPlayersTracker;
        _discord = discord;
        _permissions = permissions;
        _commands = commands;
        _messagesConfig = messagesConfig;
        _sanctions = sanctions;
        _multiServerConfig = multiServerConfig;
    }

    public void OnBanCommand(ICommandContext context) => HandleOnlineBan(context, false);
    public void OnIpBanCommand(ICommandContext context) => HandleOnlineBan(context, true);

    private void HandleOnlineBan(ICommandContext context, bool ipMode)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, ipMode ? _commands.IpBan : _commands.Ban);

        if (!HasPermission(context, _permissions.Ban))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply(ipMode
                ? "Usage: ipban <player|ip> <minutes> [reason]"
                : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["ban_usage"]}");
            return;
        }

        var targetArg = args[0];
        if (!int.TryParse(args[1], out var duration) || duration < -1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_duration"]}");
            return;
        }

        var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : PluginLocalizer.Get(_core)["no_reason"];
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var isGlobal = ResolveGlobalMode();
        var resolvedTarget = PlayerUtils.FindPlayerByTarget(_core, targetArg);
        var targetSnapshot = resolvedTarget == null
            ? null
            : new OnlineTargetSnapshot(
                resolvedTarget.SteamID,
                resolvedTarget.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                resolvedTarget.IPAddress);

        _ = Task.Run(async () =>
        {
            if (targetSnapshot != null)
            {
                if (ipMode && string.IsNullOrWhiteSpace(targetSnapshot.IpAddress))
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_no_ip"]}"));
                    return;
                }

                if (!await ValidateCanPunishAsync(context, targetSnapshot.SteamId))
                {
                    return;
                }

                var existing = await _banManager.GetActiveBanAsync(targetSnapshot.SteamId, targetSnapshot.IpAddress, _multiServerConfig.Enabled);
                if (existing != null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_already_banned", targetSnapshot.Name]}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                bool addResult;
                if (ipMode)
                {
                    addResult = await _banManager.AddIpBanAsync(targetSnapshot.IpAddress!, targetSnapshot.Name, duration, reason, isGlobal, targetSnapshot.SteamId);
                }
                else
                {
                    addResult = await _banManager.AddBanAsync(targetSnapshot.SteamId, targetSnapshot.Name, duration, reason, isGlobal);
                }

                if (!addResult)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["ban_failed"]}"));
                    return;
                }

                var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanently"] : PluginLocalizer.Get(_core)["duration_for_minutes", duration];
                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["banned_notification", adminName, targetSnapshot.Name, durationText, reason]}");
                    }

                    var onlineTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSnapshot.SteamId);
                    if (onlineTarget != null)
                    {
                        var durationDisplay = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanent"] : PluginLocalizer.Get(_core)["duration_minutes", duration];
                        PlayerUtils.SendNotification(
                            onlineTarget,
                            _messagesConfig,
                            PluginLocalizer.Get(_core)["banned_personal_html", durationDisplay, reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["banned_personal_chat", durationText, reason]}");

                        _core.Scheduler.DelayBySeconds(3f, () =>
                        {
                            var playerToKick = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSnapshot.SteamId);
                            playerToKick?.Kick($"Banned: {reason}", ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
                        });
                    }
                });

                await _discord.SendBanNotificationAsync(adminName, targetSnapshot.Name, duration, reason);
                await _adminLogManager.AddLogAsync(ipMode ? "ipban" : "ban", adminName, adminSteamId, targetSnapshot.SteamId, targetSnapshot.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", targetSnapshot.Name);
                return;
            }

            if (!ipMode && PlayerUtils.TryParseSteamId(targetArg, out var offlineSteamId))
            {
                await AddOfflineSteamBanAsync(context, offlineSteamId, duration, reason, adminName, adminSteamId, isGlobal);
                return;
            }

            if (ipMode)
            {
                await AddOfflineIpBanAsync(context, targetArg, duration, reason, adminName, adminSteamId, isGlobal);
                return;
            }

            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}"));
        });
    }

    public void OnAddBanCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.AddBan);

        if (!HasPermission(context, _permissions.AddBan))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addban_usage"]}");
            return;
        }

        if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_steamid"]}");
            return;
        }

        if (!int.TryParse(args[1], out var duration) || duration < -1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_duration"]}");
            return;
        }

        var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : PluginLocalizer.Get(_core)["no_reason"];
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var isGlobal = ResolveGlobalMode();

        _ = AddOfflineSteamBanAsync(context, targetSteamId, duration, reason, adminName, adminSteamId, isGlobal);
    }

    public void OnUnbanCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Unban);

        if (!HasPermission(context, _permissions.Ban))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unban_usage"]}");
            return;
        }

        var reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : PluginLocalizer.Get(_core)["no_reason"];
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            _banManager.SetAdminContext(adminName, adminSteamId);
            var success = false;
            ulong? targetSteamId = null;
            string? targetIp = null;
            var affectedRows = 0;

            if (PlayerUtils.TryParseSteamId(args[0], out var steamId))
            {
                var steamAffected = await _banManager.UnbanSteamByIdWithCountAsync(steamId, reason);
                affectedRows += steamAffected;
                targetSteamId = steamId;

                var knownIps = await _playerIpDbManager.GetAllKnownIpsAsync(steamId);
                foreach (var knownIp in knownIps)
                {
                    _banManager.SetAdminContext(adminName, adminSteamId);
                    var ipAffected = await _banManager.UnbanIpWithCountAsync(knownIp, reason);
                    affectedRows += ipAffected;
                }

                if (knownIps.Count > 0)
                {
                    targetIp = string.Join(",", knownIps);
                }

                success = affectedRows > 0;
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] unban requested steamid={SteamId} knownIps={IpCount} affected={AffectedRows}", steamId, knownIps.Count, affectedRows);
            }
            else
            {
                affectedRows = await _banManager.UnbanIpWithCountAsync(args[0], reason);
                success = affectedRows > 0;
                targetIp = args[0];
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] unban requested ip={Ip} affected={AffectedRows}", args[0], affectedRows);
            }

            _core.Scheduler.NextTick(() =>
            {
                context.Reply(success
                    ? $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 unban success"
                    : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 unban failed");
            });

            if (success)
            {
                await _discord.SendUnbanNotificationAsync(adminName, targetSteamId?.ToString() ?? targetIp ?? "-", reason);
                await _adminLogManager.AddLogAsync("unban", adminName, adminSteamId, targetSteamId, targetIp, $"reason={reason}");
            }
        });
    }

    public void OnLastBanCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.LastBan))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var recent = _recentPlayersTracker.GetRecent();
        if (context.Sender == null)
        {
            if (recent.Count == 0)
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_no_recent_players"]}");
                return;
            }

            context.Reply(PluginLocalizer.Get(_core)["lastban_console_header"]);
            foreach (var item in recent)
            {
                context.Reply($"- {item.Name} | {item.SteamId} | {item.IpAddress} | {item.LastSeenAt:yyyy-MM-dd HH:mm:ss}");
            }
            return;
        }

        var menuBuilder = _core.MenusAPI.CreateBuilder();
        menuBuilder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_last_players"]);

        if (recent.Count == 0)
        {
            var empty = new ButtonMenuOption(PluginLocalizer.Get(_core)["lastban_no_recent_players"]) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            menuBuilder.AddOption(empty);
        }
        else
        {
            foreach (var item in recent)
            {
                var option = new SubmenuMenuOption(
                    $"{item.Name} ({item.SteamId})",
                    () => BuildLastActionMenu(context.Sender!, item));
                menuBuilder.AddOption(option);
            }
        }

        _core.MenusAPI.OpenMenuForPlayer(context.Sender, menuBuilder.Build());
    }

    private SwiftlyS2.Shared.Menus.IMenuAPI BuildLastActionMenu(IPlayer admin, RecentPlayerInfo target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_last_actions", target.Name]);
        var hasAction = false;

        if (HasPlayerPermission(admin, _permissions.Ban))
        {
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_ban"], () => BuildLastDurationMenu(admin, target, LastSanctionAction.Ban)));
            hasAction = true;
        }

        if (HasPlayerPermission(admin, _permissions.Ban))
        {
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_ipban"], () => BuildLastDurationMenu(admin, target, LastSanctionAction.IpBan)));
            hasAction = true;
        }

        if (HasPlayerPermission(admin, _permissions.Warn))
        {
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_warn"], () => BuildLastDurationMenu(admin, target, LastSanctionAction.Warn)));
            hasAction = true;
        }

        if (HasPlayerPermission(admin, _permissions.Mute))
        {
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_mute"], () => BuildLastDurationMenu(admin, target, LastSanctionAction.Mute)));
            hasAction = true;
        }

        if (HasPlayerPermission(admin, _permissions.Gag))
        {
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_gag"], () => BuildLastDurationMenu(admin, target, LastSanctionAction.Gag)));
            hasAction = true;
        }

        if (!hasAction)
        {
            var noAction = new ButtonMenuOption(PluginLocalizer.Get(_core)["menu_no_actions_available"]) { CloseAfterClick = true };
            noAction.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(noAction);
        }

        return builder.Build();
    }

    private SwiftlyS2.Shared.Menus.IMenuAPI BuildLastDurationMenu(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_select_duration"]);

        foreach (var item in _sanctions.Durations)
        {
            var btn = new ButtonMenuOption(item.Name) { CloseAfterClick = true };
            btn.Click += (_, _) =>
            {
                _core.Scheduler.NextTick(() => OpenLastReasonMenu(admin, target, action, item.Minutes));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void OpenLastReasonMenu(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action, int duration)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_select_reason"]);

        foreach (var reason in GetReasonsForLastAction(action))
        {
            var option = new ButtonMenuOption(reason) { CloseAfterClick = true };
            option.Click += (_, _) =>
            {
                _ = ApplyLastSanctionAsync(admin, target, action, duration, reason);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private async Task AddOfflineSteamBanAsync(
        ICommandContext context,
        ulong targetSteamId,
        int duration,
        string reason,
        string adminName,
        ulong adminSteamId,
        bool isGlobal)
    {
        var existingBan = await _banManager.GetActiveBanAsync(targetSteamId, null, _multiServerConfig.Enabled);
        if (existingBan != null)
        {
            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["steamid_already_banned", targetSteamId]}"));
            return;
        }

        _banManager.SetAdminContext(adminName, adminSteamId);
        var ok = await _banManager.AddBanAsync(targetSteamId, targetSteamId.ToString(), duration, reason, isGlobal);
        if (!ok)
        {
            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addban_failed"]}"));
            return;
        }

        _core.Scheduler.NextTick(() =>
        {
            var durationDisplay = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanent"] : PluginLocalizer.Get(_core)["duration_minutes", duration];
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addban_success", targetSteamId, durationDisplay]}");
        });

        await _discord.SendBanNotificationAsync(adminName, targetSteamId.ToString(), duration, reason);
        await _adminLogManager.AddLogAsync("addban", adminName, adminSteamId, targetSteamId, null, $"duration={duration};global={isGlobal};reason={reason}");
    }

    private async Task AddOfflineIpBanAsync(
        ICommandContext context,
        string ipAddress,
        int duration,
        string reason,
        string adminName,
        ulong adminSteamId,
        bool isGlobal)
    {
        _banManager.SetAdminContext(adminName, adminSteamId);
        var ok = await _banManager.AddIpBanAsync(ipAddress, ipAddress, duration, reason, isGlobal);
        _core.Scheduler.NextTick(() => context.Reply(ok ? "ipban success" : "ipban failed"));
        if (ok)
        {
            await _adminLogManager.AddLogAsync("ipban", adminName, adminSteamId, null, ipAddress, $"duration={duration};global={isGlobal};reason={reason}");
        }
    }

    private IReadOnlyList<string> GetReasonsForLastAction(LastSanctionAction action)
    {
        return _sanctions.Reasons;
    }

    private async Task ApplyLastSanctionAsync(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action, int duration, string reason)
    {
        if (!await ValidateCanPunishLastTargetAsync(admin, target))
        {
            return;
        }

        var adminName = admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = admin.SteamID;
        var isGlobal = ResolveGlobalMode();

        switch (action)
        {
            case LastSanctionAction.Ban:
            {
                var existing = await _banManager.GetActiveBanAsync(target.SteamId, target.IpAddress, _multiServerConfig.Enabled);
                if (existing != null)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["steamid_already_banned", target.SteamId]}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _banManager.AddBanAsync(target.SteamId, target.Name, duration, reason, isGlobal);
                if (!ok)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_failed", PluginLocalizer.Get(_core)["menu_ban"]]}"));
                    return;
                }

                await _adminLogManager.AddLogAsync("lastban_ban", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", target.Name);
                await _discord.SendBanNotificationAsync(adminName, target.Name, duration, reason);
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_applied", PluginLocalizer.Get(_core)["menu_ban"], target.Name]}"));
                return;
            }
            case LastSanctionAction.IpBan:
            {
                if (string.IsNullOrWhiteSpace(target.IpAddress))
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_no_ip"]}"));
                    return;
                }

                var existing = await _banManager.GetActiveBanAsync(0, target.IpAddress, _multiServerConfig.Enabled);
                if (existing != null)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_ip_already_banned", target.IpAddress]}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _banManager.AddIpBanAsync(target.IpAddress, target.Name, duration, reason, isGlobal, target.SteamId);
                if (!ok)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_failed", PluginLocalizer.Get(_core)["menu_ipban"]]}"));
                    return;
                }

                await _adminLogManager.AddLogAsync("lastban_ipban", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", target.Name);
                await _discord.SendBanNotificationAsync(adminName, target.Name, duration, reason);
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_applied", PluginLocalizer.Get(_core)["menu_ipban"], target.Name]}"));
                return;
            }
            case LastSanctionAction.Warn:
            {
                _warnManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _warnManager.AddWarnAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_failed", PluginLocalizer.Get(_core)["menu_warn"]]}"));
                    return;
                }

                await _adminLogManager.AddLogAsync("lastban_warn", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _discord.SendWarnNotificationAsync(adminName, target.Name, duration, reason);
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_applied", PluginLocalizer.Get(_core)["menu_warn"], target.Name]}"));
                return;
            }
            case LastSanctionAction.Mute:
            {
                var existing = await _muteManager.GetActiveMuteAsync(target.SteamId);
                if (existing != null)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_already_muted", target.Name]}"));
                    return;
                }

                _muteManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _muteManager.AddMuteAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_failed", PluginLocalizer.Get(_core)["menu_mute"]]}"));
                    return;
                }

                await _adminLogManager.AddLogAsync("lastban_mute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _discord.SendMuteNotificationAsync(adminName, target.Name, duration, reason);
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_applied", PluginLocalizer.Get(_core)["menu_mute"], target.Name]}"));
                return;
            }
            case LastSanctionAction.Gag:
            {
                var existing = await _gagManager.GetActiveGagAsync(target.SteamId);
                if (existing != null)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_already_gagged", target.Name]}"));
                    return;
                }

                _gagManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _gagManager.AddGagAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_failed", PluginLocalizer.Get(_core)["menu_gag"]]}"));
                    return;
                }

                await _adminLogManager.AddLogAsync("lastban_gag", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _discord.SendGagNotificationAsync(adminName, target.Name, duration, reason);
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["lastban_action_applied", PluginLocalizer.Get(_core)["menu_gag"], target.Name]}"));
                return;
            }
            default:
                return;
        }
    }

    private async Task<bool> ValidateCanPunishLastTargetAsync(IPlayer admin, RecentPlayerInfo target)
    {
        var adminImm = await _adminDbManager.GetEffectiveImmunityAsync(admin.SteamID);
        var targetImm = await _adminDbManager.GetEffectiveImmunityAsync(target.SteamId);
        if (targetImm >= adminImm && targetImm > 0)
        {
            _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_immunity"]}"));
            return false;
        }

        return true;
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        if (!context.IsSentByPlayer || context.Sender == null)
        {
            return true;
        }

        var adminImm = await _adminDbManager.GetEffectiveImmunityAsync(context.Sender.SteamID);
        var targetImm = await _adminDbManager.GetEffectiveImmunityAsync(targetSteamId);
        if (targetImm >= adminImm && targetImm > 0)
        {
            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_immunity"]}"));
            return false;
        }

        return true;
    }

    private bool ResolveGlobalMode()
    {
        if (!_multiServerConfig.Enabled)
        {
            return false;
        }

        return _multiServerConfig.GlobalBansByDefault;
    }

    private enum LastSanctionAction
    {
        Ban,
        IpBan,
        Warn,
        Mute,
        Gag
    }

    private sealed record OnlineTargetSnapshot(ulong SteamId, string Name, string? IpAddress);

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
        {
            return true;
        }

        return HasPlayerPermission(context.Sender!, permission);
    }

    private bool HasPlayerPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _permissions.AdminRoot);
    }
}


