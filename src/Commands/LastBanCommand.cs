using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class LastBanCommand : BanCommandBase
{
    private readonly IReadOnlyList<string> _lastBanAliases;

    public LastBanCommand(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        PlayerIpDbManager playerIpDbManager,
        PlayerSessionManager playerSessionManager,
        RecentPlayersTracker recentPlayersTracker,
        DiscordBotService discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messages,
        SanctionMenuConfig sanctions,
        MultiServerConfig multiServerConfig,
        int banType,
        PlayerSanctionStateService sanctionStateService,
        PermissionService permissionService,
        IReadOnlyList<string> lastBanAliases)
        : base(core, banManager, muteManager, gagManager, warnManager, adminDbManager, adminLogManager,
            playerIpDbManager, playerSessionManager, recentPlayersTracker, discord, permissions, commands,
            tags, messages, sanctions, multiServerConfig, banType, sanctionStateService, permissionService)
    {
        _lastBanAliases = lastBanAliases;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.LastBan))
            {
                Reply(context, "no_permission");
                return;
            }

            var recent = _recentPlayersTracker.GetRecent();
            if (recent.Count > 0)
            {
                ShowLastBanTargets(context, recent);
                return;
            }

            var fallbackRecent = await _playerSessionManager.GetRecentDisconnectedPlayersAsync();
            ShowLastBanTargets(context, fallbackRecent);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "LastBan command failed");
        }
    }

    private void ShowLastBanTargets(ICommandContext context, IReadOnlyList<RecentPlayerInfo> recent)
    {
        if (context.Sender == null)
        {
            if (recent.Count == 0)
            {
                ReplyRaw(context, T("lastban_no_recent_players", "No recent disconnected players found."));
                return;
            }

            ReplyRaw(context, T("lastban_console_header", "Recent disconnected players:"));
            foreach (var item in recent)
            {
                context.Reply($"- {item.Name} | {item.SteamId} | {item.IpAddress} | {item.LastSeenAt:yyyy-MM-dd HH:mm:ss}");
            }
            return;
        }

        if (!context.Sender.IsValid)
        {
            return;
        }

        var menuBuilder = Core.MenusAPI.CreateBuilder();
        menuBuilder.Design.SetMenuTitle(T("menu_last_players", "Recent Disconnected Players"));

        if (recent.Count == 0)
        {
            var empty = new ButtonMenuOption(T("lastban_no_recent_players", "No recent disconnected players found.")) { CloseAfterClick = true };
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

        Core.MenusAPI.OpenMenuForPlayer(context.Sender, menuBuilder.Build());
    }

    private SwiftlyS2.Shared.Menus.IMenuAPI BuildLastActionMenu(IPlayer admin, RecentPlayerInfo target)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_last_actions", $"Actions for {target.Name}", target.Name));
        var hasAction = false;

        if (HasPerm(admin, Permissions.Ban))
        {
            builder.AddOption(new SubmenuMenuOption(L("menu_ban"), () => BuildLastDurationMenu(admin, target, LastSanctionAction.Ban)));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Ban))
        {
            builder.AddOption(new SubmenuMenuOption(L("menu_ipban"), () => BuildLastDurationMenu(admin, target, LastSanctionAction.IpBan)));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Warn))
        {
            builder.AddOption(new SubmenuMenuOption(L("menu_warn"), () => BuildLastDurationMenu(admin, target, LastSanctionAction.Warn)));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Mute))
        {
            builder.AddOption(new SubmenuMenuOption(L("menu_mute"), () => BuildLastDurationMenu(admin, target, LastSanctionAction.Mute)));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Gag))
        {
            builder.AddOption(new SubmenuMenuOption(L("menu_gag"), () => BuildLastDurationMenu(admin, target, LastSanctionAction.Gag)));
            hasAction = true;
        }

        if (!hasAction)
        {
            var noAction = new ButtonMenuOption(L("menu_no_actions_available")) { CloseAfterClick = true };
            noAction.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(noAction);
        }

        return builder.Build();
    }

    private SwiftlyS2.Shared.Menus.IMenuAPI BuildLastDurationMenu(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration", "Select Duration"));

        foreach (var item in _sanctions.Durations)
        {
            var btn = new ButtonMenuOption(item.Name) { CloseAfterClick = true };
            btn.Click += (_, _) =>
            {
                OpenLastReasonMenu(admin, target, action, item.Minutes);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void OpenLastReasonMenu(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action, int duration)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.BindToParent(BuildLastDurationMenu(admin, target, action));
        builder.Design.SetMenuTitle(T("menu_select_reason", "Select Reason"));

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

        Core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
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

        var adminName = admin.Controller.PlayerName ?? L("console_name");
        var adminSteamId = admin.SteamID;
        var isGlobal = ResolveGlobalMode();

        switch (action)
        {
            case LastSanctionAction.Ban:
            {
                var existing = await _banManager.GetActiveBanFreshAsync(target.SteamId, target.IpAddress, _multiServerConfig.Enabled);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("steamid_already_banned", target.SteamId)}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _banManager.AddBanAsync(target.SteamId, target.Name, duration, reason, isGlobal, target.IpAddress);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_failed", L("menu_ban"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_ban", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_applied", L("menu_ban"), target.Name)}"));
                return;
            }
            case LastSanctionAction.IpBan:
            {
                if (string.IsNullOrWhiteSpace(target.IpAddress))
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_no_ip")}"));
                    return;
                }

                var existing = await _banManager.GetActiveBanFreshAsync(0, target.IpAddress, _multiServerConfig.Enabled);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_ip_already_banned", target.IpAddress)}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _banManager.AddIpBanAsync(target.IpAddress, target.Name, duration, reason, isGlobal, target.SteamId);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_failed", L("menu_ipban"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_ipban", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_applied", L("menu_ipban"), target.Name)}"));
                return;
            }
            case LastSanctionAction.Warn:
            {
                _warnManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _warnManager.AddWarnAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_failed", L("menu_warn"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_warn", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_applied", L("menu_warn"), target.Name)}"));
                return;
            }
            case LastSanctionAction.Mute:
            {
                var existing = await _muteManager.GetActiveMuteFreshAsync(target.SteamId);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("player_already_muted", target.Name)}"));
                    return;
                }

                _muteManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _muteManager.AddMuteAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_failed", L("menu_mute"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_mute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_applied", L("menu_mute"), target.Name)}"));
                return;
            }
            case LastSanctionAction.Gag:
            {
                var existing = await _gagManager.GetActiveGagFreshAsync(target.SteamId);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("player_already_gagged", target.Name)}"));
                    return;
                }

                _gagManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _gagManager.AddGagAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_failed", L("menu_gag"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_gag", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" \x02{L("prefix")}\x01 {L("lastban_action_applied", L("menu_gag"), target.Name)}"));
                return;
            }
            default:
                return;
        }
    }

    private async Task<bool> ValidateCanPunishLastTargetAsync(IPlayer admin, RecentPlayerInfo target)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, admin, target.SteamId);
    }

    private enum LastSanctionAction
    {
        Ban,
        IpBan,
        Warn,
        Mute,
        Gag
    }
}
