using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
using CS2_Admin.Config;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class ServerManagementHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;

    public ServerManagementHandler(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
    }

    public IMenu CreateMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.server_mgmt")
            .WithTitle(T("menu_server_management"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (HasPermission(player, _config.Permissions.RestartGame))
        {
            var restartText = T("menu_restart_game");
            builder.Add(new ButtonComponent(restartText, ctx =>
            {
                var current = CreateMenu(ctx.Player, parent);
                var subMenu = CreateRestartGameMenu(ctx.Player, current);
                subMenu.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
        }

        if (HasPermission(player, _config.Permissions.ChangeMap))
        {
            var mapText = T("menu_change_map");
            builder.Add(new ButtonComponent(mapText, ctx =>
            {
                var current = CreateMenu(ctx.Player, parent);
                var subMenu = CreateChangeMapMenu(ctx.Player, current);
                subMenu.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
        }

        if (HasPermission(player, _config.Permissions.ChangeWSMap))
        {
            var wsMapText = T("menu_change_ws_map");
            builder.Add(new ButtonComponent(wsMapText, ctx =>
            {
                var current = CreateMenu(ctx.Player, parent);
                var subMenu = CreateChangeWSMapMenu(ctx.Player, current);
                subMenu.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
        }

        if (HasPermission(player, _config.Permissions.HeadshotMode))
        {
            builder.Add(new ButtonComponent(T("menu_headshot_on"), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.HeadshotOn, "hson");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));

            builder.Add(new ButtonComponent(T("menu_headshot_off"), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.HeadshotOff, "hsoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));
        }

        if (HasPermission(player, _config.Permissions.BunnyHop))
        {
            builder.Add(new ButtonComponent(T("menu_bunny_on"), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.BunnyOn, "bunnyon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));

            builder.Add(new ButtonComponent(T("menu_bunny_off"), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.BunnyOff, "bunnyoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));
        }

        if (HasPermission(player, _config.Permissions.RespawnMode))
        {
            builder.Add(new ButtonComponent(T("menu_respawn_on"), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RespawnOn, "respawnon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));

            builder.Add(new ButtonComponent(T("menu_respawn_off"), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RespawnOff, "respawnoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu CreateRestartGameMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.server_mgmt.restart")
            .WithTitle(T("menu_restart_game"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var delay = 2;
        builder.Add(new SliderComponent(T("menu_restart_delay"), 1, 10, 2, 1, change =>
        {
            delay = (int)change.NewValue;
        }));

        builder.Add(new ButtonComponent(T("menu_restart_now"), ctx =>
        {
            var caller = ctx.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RestartGame, "rr");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {delay}"));
            return ValueTask.CompletedTask;
        }));

        return builder.Build();
    }

    private IMenu CreateChangeMapMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.server_mgmt.maps")
            .WithTitle(T("menu_change_map"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (_config.GameMaps.Maps != null)
        {
            foreach (var map in _config.GameMaps.Maps)
            {
                var mapKey = map.Key;
                builder.Add(new ButtonComponent(map.Value, ctx =>
                {
                    var caller = ctx.Player;
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeMap, "map");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {mapKey}"));
                    return ValueTask.CompletedTask;
                }));
            }
        }

        return builder.Build();
    }

    private IMenu CreateChangeWSMapMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.server_mgmt.wsmaps")
            .WithTitle(T("menu_change_ws_map"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (_config.WorkshopMaps.Maps != null)
        {
            foreach (var map in _config.WorkshopMaps.Maps)
            {
                var workshopId = map.Value;
                builder.Add(new ButtonComponent(map.Key, ctx =>
                {
                    var caller = ctx.Player;
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeWSMap, "wsmap");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {workshopId}"));
                    return ValueTask.CompletedTask;
                }));
            }
        }

        return builder.Build();
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private string T(string key, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            return args.Length == 0 ? localizer[key] : localizer[key, args];
        }
        catch
        {
            return key;
        }
    }
}
