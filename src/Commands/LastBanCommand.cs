using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;

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

        var menuBuilder = Core.Menu.CreateMenu("cs2_admin.lastban.targets")
            .WithTitle(T("menu_last_players", "Recent Disconnected Players"));

        if (recent.Count == 0)
        {
            menuBuilder.Add(new ButtonComponent(T("lastban_no_recent_players", "No recent disconnected players found."), _ => ValueTask.CompletedTask));
        }
        else
        {
            foreach (var item in recent)
            {
                var r = item;
                menuBuilder.Add(new ButtonComponent($"{r.Name} ({r.SteamId})", ctx =>
                {
                    var parent = menuBuilder.Build();
                    var actionMenu = BuildLastActionMenu(ctx.Player, r, parent);
                    actionMenu.Open(ctx.Player);
                    return ValueTask.CompletedTask;
                }));
            }
        }

        menuBuilder.Build().Open(context.Sender);
    }

    private IMenu BuildLastActionMenu(IPlayer admin, RecentPlayerInfo target, IMenu? parent = null)
    {
        var builder = Core.Menu.CreateMenu($"cs2_admin.lastban.action.{target.SteamId}")
            .WithTitle(T("menu_last_actions", $"Actions for {target.Name}", target.Name));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var hasAction = false;

        if (HasPerm(admin, Permissions.Ban))
        {
            builder.Add(new ButtonComponent(L("menu_ban"), ctx =>
            {
                var current = BuildLastActionMenu(ctx.Player, target, parent);
                var sub = BuildLastDurationMenu(ctx.Player, target, LastSanctionAction.Ban, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Ban))
        {
            builder.Add(new ButtonComponent(L("menu_ipban"), ctx =>
            {
                var current = BuildLastActionMenu(ctx.Player, target, parent);
                var sub = BuildLastDurationMenu(ctx.Player, target, LastSanctionAction.IpBan, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Warn))
        {
            builder.Add(new ButtonComponent(L("menu_warn"), ctx =>
            {
                var current = BuildLastActionMenu(ctx.Player, target, parent);
                var sub = BuildLastDurationMenu(ctx.Player, target, LastSanctionAction.Warn, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Mute))
        {
            builder.Add(new ButtonComponent(L("menu_mute"), ctx =>
            {
                var current = BuildLastActionMenu(ctx.Player, target, parent);
                var sub = BuildLastDurationMenu(ctx.Player, target, LastSanctionAction.Mute, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
            hasAction = true;
        }

        if (HasPerm(admin, Permissions.Gag))
        {
            builder.Add(new ButtonComponent(L("menu_gag"), ctx =>
            {
                var current = BuildLastActionMenu(ctx.Player, target, parent);
                var sub = BuildLastDurationMenu(ctx.Player, target, LastSanctionAction.Gag, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
            hasAction = true;
        }

        if (!hasAction)
        {
            builder.Add(new ButtonComponent(L("menu_no_actions_available"), _ => ValueTask.CompletedTask));
        }

        return builder.Build();
    }

    private IMenu BuildLastDurationMenu(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action, IMenu? parent = null)
    {
        var builder = Core.Menu.CreateMenu($"cs2_admin.lastban.duration.{action}")
            .WithTitle(T("menu_select_duration", "Select Duration"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        foreach (var item in _sanctions.Durations)
        {
            var d = item.Minutes;
            builder.Add(new ButtonComponent(item.Name, ctx =>
            {
                var current = BuildLastDurationMenu(ctx.Player, target, action, parent);
                OpenLastReasonMenu(ctx.Player, target, action, d, current);
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private void OpenLastReasonMenu(IPlayer admin, RecentPlayerInfo target, LastSanctionAction action, int duration, IMenu? parent = null)
    {
        var builder = Core.Menu.CreateMenu($"cs2_admin.lastban.reason.{action}")
            .WithTitle(T("menu_select_reason", "Select Reason"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        foreach (var reason in GetReasonsForLastAction(action))
        {
            var r = reason;
            builder.Add(new ButtonComponent(r, ctx =>
            {
                _ = ApplyLastSanctionAsync(ctx.Player, target, action, duration, r);
                return ValueTask.CompletedTask;
            }));
        }

        builder.Build().Open(admin);
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
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("steamid_already_banned", target.SteamId)}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _banManager.AddBanAsync(target.SteamId, target.Name, duration, reason, isGlobal, target.IpAddress);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_failed", L("menu_ban"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_ban", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_applied", L("menu_ban"), target.Name)}"));
                return;
            }
            case LastSanctionAction.IpBan:
            {
                if (string.IsNullOrWhiteSpace(target.IpAddress))
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_no_ip")}"));
                    return;
                }

                var existing = await _banManager.GetActiveBanFreshAsync(0, target.IpAddress, _multiServerConfig.Enabled);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_ip_already_banned", target.IpAddress)}"));
                    return;
                }

                _banManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _banManager.AddIpBanAsync(target.IpAddress, target.Name, duration, reason, isGlobal, target.SteamId);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_failed", L("menu_ipban"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_ipban", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};global={isGlobal};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_applied", L("menu_ipban"), target.Name)}"));
                return;
            }
            case LastSanctionAction.Warn:
            {
                _warnManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _warnManager.AddWarnAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_failed", L("menu_warn"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_warn", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_applied", L("menu_warn"), target.Name)}"));
                return;
            }
            case LastSanctionAction.Mute:
            {
                var existing = await _muteManager.GetActiveMuteFreshAsync(target.SteamId);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("player_already_muted", target.Name)}"));
                    return;
                }

                _muteManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _muteManager.AddMuteAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_failed", L("menu_mute"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_mute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_applied", L("menu_mute"), target.Name)}"));
                return;
            }
            case LastSanctionAction.Gag:
            {
                var existing = await _gagManager.GetActiveGagFreshAsync(target.SteamId);
                if (existing != null)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("player_already_gagged", target.Name)}"));
                    return;
                }

                _gagManager.SetAdminContext(adminName, adminSteamId);
                var ok = await _gagManager.AddGagAsync(target.SteamId, duration, reason);
                if (!ok)
                {
                    await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_failed", L("menu_gag"))}"));
                    return;
                }

                await AdminLogManager.AddLogAsync("lastban_gag", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                await OnMainThreadAsync(() => admin.SendChat($" {L("prefix")} {L("lastban_action_applied", L("menu_gag"), target.Name)}"));
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
