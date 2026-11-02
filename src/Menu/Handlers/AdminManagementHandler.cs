using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using CS2_Admin.Utils;
using CS2_Admin.Services;

namespace CS2_Admin.Menu.Handlers;

public class AdminManagementHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly AdminDbManager _adminManager;
    private readonly GroupDbManager _groupManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly ChatTagConfigManager _chatTagConfigManager;

    public AdminManagementHandler(
        ISwiftlyCore core,
        PluginConfig config,
        AdminDbManager adminManager,
        GroupDbManager groupManager,
        AdminLogManager adminLogManager,
        ChatTagConfigManager chatTagConfigManager)
    {
        _core = core;
        _config = config;
        _adminManager = adminManager;
        _groupManager = groupManager;
        _adminLogManager = adminLogManager;
        _chatTagConfigManager = chatTagConfigManager;
    }

    public IMenu CreateMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.admin_mgmt")
            .WithTitle(T("menu_admin_management"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (!IsRoot(player))
        {
            builder.Add(new ButtonComponent(T("no_permission"), _ => ValueTask.CompletedTask));
            return builder.Build();
        }

        var addAdminText = T("menu_add_admin");
        builder.Add(new ButtonComponent(addAdminText, ctx =>
        {
            var current = CreateMenu(ctx.Player, parent);
            var sub = BuildAddAdminMenu(ctx.Player, current);
            sub.Open(ctx.Player);
            return ValueTask.CompletedTask;
        }));

        var removeAdminText = T("menu_remove_admin");
        builder.Add(new ButtonComponent(removeAdminText, ctx =>
        {
            var adminPlayer = ctx.Player;
            _ = Task.Run(async () =>
            {
                var admins = await _adminManager.GetAllAdminsAsync();
                _core.Scheduler.NextTick(() =>
                {
                    if (adminPlayer.IsValid)
                    {
                        var current = CreateMenu(adminPlayer, parent);
                        var sub = BuildRemoveAdminMenu(adminPlayer, admins, current);
                        sub.Open(adminPlayer);
                    }
                });
            });
            return ValueTask.CompletedTask;
        }));

        var listAdminsText = T("menu_list_admins");
        builder.Add(new ButtonComponent(listAdminsText, ctx =>
        {
            var adminPlayer = ctx.Player;
            _ = Task.Run(async () =>
            {
                var admins = await _adminManager.GetAllAdminsAsync();
                _core.Scheduler.NextTick(() =>
                {
                    if (adminPlayer.IsValid)
                    {
                        var current = CreateMenu(adminPlayer, parent);
                        var sub = BuildListAdminsMenu(adminPlayer, admins, current);
                        sub.Open(adminPlayer);
                    }
                });
            });
            return ValueTask.CompletedTask;
        }));

        return builder.Build();
    }

    private bool IsRoot(IPlayer player) => _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);

    private IMenu BuildAddAdminMenu(IPlayer admin, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.add_admin")
            .WithTitle(T("menu_select_player_add"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        foreach (var target in players)
        {
            var t = target;
            var fallbackName = T("player_fallback_name", t.PlayerID);
            var btnText = t.Controller.PlayerName ?? fallbackName;
            builder.Add(new ButtonComponent(btnText, ctx =>
            {
                var adminPlayer = ctx.Player;
                _core.Scheduler.NextTick(() =>
                {
                    var current = BuildAddAdminMenu(adminPlayer, parent);
                    OpenAddAdminGroupMenu(adminPlayer, t, current);
                });
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private void OpenAddAdminGroupMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        _ = Task.Run(async () =>
        {
            var groups = await _groupManager.GetAllGroupsAsync();
            _core.Scheduler.NextTick(() =>
            {
                if (!admin.IsValid)
                    return;

                var builder = _core.Menu.CreateMenu($"cs2_admin.add_admin_group.{target.PlayerID}")
                    .WithTitle(T("menu_select_group"));

                if (parent != null)
                {
                    builder.WithParent(parent);
                }

                foreach (var group in groups)
                {
                    var g = group;
                    builder.Add(new ButtonComponent(T("menu_group_with_immunity", g.Name, g.Immunity), ctx =>
                    {
                        var adminPlayer = ctx.Player;
                        _core.Scheduler.NextTick(() => ExecuteAddAdminWithGroup(adminPlayer, target, g.Name));
                        return ValueTask.CompletedTask;
                    }));
                }

                if (groups.Count == 0)
                {
                    builder.Add(new ButtonComponent(T("menu_no_groups"), _ => ValueTask.CompletedTask));
                }

                builder.Build().Open(admin);
            });
        });
    }

    private IMenu BuildRemoveAdminMenu(IPlayer admin, List<Admin> admins, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.remove_admin")
            .WithTitle(T("menu_select_admin_remove"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (admins.Count == 0)
        {
            builder.Add(new ButtonComponent(T("menu_no_admins"), _ => ValueTask.CompletedTask));
            return builder.Build();
        }

        foreach (var target in admins)
        {
            var t = target;
            builder.Add(new ButtonComponent(T("menu_admin_list_entry", t.Name, t.Id, t.SteamId), ctx =>
            {
                var adminPlayer = ctx.Player;
                _core.Scheduler.NextTick(() => ExecuteRemoveAdmin(adminPlayer, t.SteamId));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private void ExecuteAddAdminWithGroup(IPlayer admin, IPlayer target, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        if (!IsRoot(admin))
        {
            admin.SendChat($" {T("prefix")} {T("no_permission")}");
            return;
        }

        var targetName = target.Controller.PlayerName ?? T("player_fallback_name", target.PlayerID);
        var adminName = admin.Controller.PlayerName ?? T("console_name");
        var targetSteamId = target.SteamID;
        var adminSteamId = admin.SteamID;

        _ = Task.Run(async () =>
        {
            var success = await _adminManager.AddAdminAsync(
                targetSteamId,
                targetName,
                string.Empty,
                0,
                groupName,
                adminName,
                adminSteamId,
                null);

            if (!success)
            {
                _core.Scheduler.NextTick(() => admin.SendChat($" {T("prefix")} {T("addadmin_failed")}"));
                return;
            }

            var effectiveFlags = await _adminManager.GetEffectiveFlagsAsync(targetSteamId);
            _core.Scheduler.NextTick(() =>
            {
                admin.SendChat($" {T("prefix")} {T("addadmin_success", targetName, targetSteamId, string.Join(",", effectiveFlags))}");
                target.SendChat($" {T("prefix")} {T("addadmin_granted")}");
                if (_config.Tags.Enabled && _chatTagConfigManager.Config.ScoreboardEnabled &&
                    _chatTagConfigManager.Config.Groups.ContainsKey(groupName))
                {
                    PlayerUtils.SetScoreTagReliable(_core, target.PlayerID, groupName);
                }

                var reloadCmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.AdminReload, "adminreload");
                admin.ExecuteCommand(reloadCmd);
            });

            await _adminLogManager.AddLogAsync(
                "addadmin",
                adminName,
                adminSteamId,
                targetSteamId,
                target.IPAddress,
                $"groups={groupName};immunity=0;source=menu",
                targetName);
        });
    }

    private void ExecuteRemoveAdmin(IPlayer admin, ulong steamId)
    {
        if (!IsRoot(admin))
        {
            admin.SendChat($" {T("prefix")} {T("no_permission")}");
            return;
        }

        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RemoveAdmin, "removeadmin");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {steamId}"));
    }

    private void ExecuteListAdmins(IPlayer admin)
    {
        if (!IsRoot(admin))
        {
            admin.SendChat($" {T("prefix")} {T("no_permission")}");
            return;
        }

        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ListAdmins, "listadmins");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand(cmd));
    }

    private IMenu BuildListAdminsMenu(IPlayer admin, List<Admin> admins, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.list_admins")
            .WithTitle(T("menu_list_admins"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (admins.Count == 0)
        {
            builder.Add(new ButtonComponent(T("menu_no_admins"), _ => ValueTask.CompletedTask));
            return builder.Build();
        }

        foreach (var item in admins)
        {
            var title = T("menu_admin_list_entry", item.Name, item.Id, item.SteamId);
            builder.Add(new ButtonComponent(title, _ => ValueTask.CompletedTask));
        }

        return builder.Build();
    }

    private string T(string key, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            return args.Length == 0 ? localizer[key] : LocalizerHelper.Get(_core, key, args);
        }
        catch
        {
            return key;
        }
    }
}
