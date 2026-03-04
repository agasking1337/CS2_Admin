using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class AdminManagementHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly AdminDbManager _adminManager;
    private readonly GroupDbManager _groupManager;
    private readonly AdminLogManager _adminLogManager;

    public AdminManagementHandler(
        ISwiftlyCore core,
        PluginConfig config,
        AdminDbManager adminManager,
        GroupDbManager groupManager,
        AdminLogManager adminLogManager)
    {
        _core = core;
        _config = config;
        _adminManager = adminManager;
        _groupManager = groupManager;
        _adminLogManager = adminLogManager;
    }

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();

        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_admin_management"];
        }
        catch
        {
            title = "Admin Management";
        }
        builder.Design.SetMenuTitle(title);

        if (!IsRoot(player))
        {
            var noPermissionButton = new ButtonMenuOption(PluginLocalizer.Get(_core)["no_permission"]) { CloseAfterClick = true };
            noPermissionButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(noPermissionButton);
            return builder.Build();
        }

        string addAdminText;
        try
        {
            addAdminText = PluginLocalizer.Get(_core)["menu_add_admin"];
        }
        catch
        {
            addAdminText = "Add Admin";
        }
        builder.AddOption(new SubmenuMenuOption(addAdminText, () => BuildAddAdminMenu(player)));

        string removeAdminText;
        try
        {
            removeAdminText = PluginLocalizer.Get(_core)["menu_remove_admin"];
        }
        catch
        {
            removeAdminText = "Remove Admin";
        }
        builder.AddOption(new SubmenuMenuOption(removeAdminText, () => BuildRemoveAdminMenu(player)));

        string listAdminsText;
        try
        {
            listAdminsText = PluginLocalizer.Get(_core)["menu_list_admins"];
        }
        catch
        {
            listAdminsText = "List Admins";
        }
        builder.AddOption(new SubmenuMenuOption(listAdminsText, () => BuildListAdminsMenu(player)));

        return builder.Build();
    }

    private bool IsRoot(IPlayer player) => _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);

    private IMenuAPI BuildAddAdminMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_select_player_add"];
        }
        catch
        {
            title = "Select Player to Add";
        }
        builder.Design.SetMenuTitle(title);

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        foreach (var target in players)
        {
            var fallbackName = "Player " + target.PlayerID;
            try
            {
                fallbackName = PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];
            }
            catch
            {
                // Use default fallback
            }
            var btn = new ButtonMenuOption(target.Controller.PlayerName ?? fallbackName) { CloseAfterClick = false };
            btn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() => OpenAddAdminGroupMenu(adminPlayer, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void OpenAddAdminGroupMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_select_group"]);

        var groups = _groupManager.GetAllGroupsAsync().GetAwaiter().GetResult();
        foreach (var group in groups)
        {
            var groupBtn = new ButtonMenuOption($"{group.Name} (imm: {group.Immunity})") { CloseAfterClick = true };
            groupBtn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() => ExecuteAddAdminWithGroup(adminPlayer, target, group.Name));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(groupBtn);
        }

        if (groups.Count == 0)
        {
            var empty = new ButtonMenuOption(PluginLocalizer.Get(_core)["menu_no_groups"]) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private IMenuAPI BuildRemoveAdminMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_select_admin_remove"];
        }
        catch
        {
            title = "Select Admin to Remove";
        }
        builder.Design.SetMenuTitle(title);

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        foreach (var target in players)
        {
            var fallbackName = "Player " + target.PlayerID;
            try
            {
                fallbackName = PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];
            }
            catch
            {
                // Use default fallback
            }
            var btn = new ButtonMenuOption(target.Controller.PlayerName ?? fallbackName) { CloseAfterClick = true };
            btn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() => ExecuteRemoveAdmin(adminPlayer, target));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void ExecuteAddAdminWithGroup(IPlayer admin, IPlayer target, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        if (!IsRoot(admin))
        {
            admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var targetName = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];
        var adminName = admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
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
                _core.Scheduler.NextTick(() => admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addadmin_failed"]}"));
                return;
            }

            var effectiveFlags = await _adminManager.GetEffectiveFlagsAsync(targetSteamId);
            _core.Scheduler.NextTick(() =>
            {
                admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addadmin_success", targetName, targetSteamId, string.Join(",", effectiveFlags)]}");
                target.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addadmin_granted"]}");
                if (_config.Tags.Enabled)
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

    private void ExecuteRemoveAdmin(IPlayer admin, IPlayer target)
    {
        if (!IsRoot(admin))
        {
            admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var steamId = target.SteamID;
        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RemoveAdmin, "removeadmin");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {steamId}"));
    }

    private void ExecuteListAdmins(IPlayer admin)
    {
        if (!IsRoot(admin))
        {
            admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ListAdmins, "listadmins");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand(cmd));
    }

    private IMenuAPI BuildListAdminsMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_list_admins"]);

        var admins = _adminManager.GetAllAdminsAsync().GetAwaiter().GetResult();
        if (admins.Count == 0)
        {
            var empty = new ButtonMenuOption(PluginLocalizer.Get(_core)["menu_no_admins"]) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
            return builder.Build();
        }

        foreach (var item in admins)
        {
            var title = $"{item.Name} | #{item.Id} | {item.SteamId}";
            var detail = new ButtonMenuOption(title) { CloseAfterClick = false };
            detail.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(detail);
        }

        return builder.Build();
    }
}


