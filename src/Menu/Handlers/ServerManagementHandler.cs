using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
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

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_server_management"];
        }
        catch
        {
            title = "Server Management";
        }
        builder.Design.SetMenuTitle(title);

        // Restart game
        if (HasPermission(player, _config.Permissions.RestartGame))
        {
            string restartText;
            try
            {
                restartText = PluginLocalizer.Get(_core)["menu_restart_game"];
            }
            catch
            {
                restartText = "Restart Game";
            }
            builder.AddOption(new SubmenuMenuOption(restartText, () => CreateRestartGameMenu(player)));
        }

        // Change map
        if (HasPermission(player, _config.Permissions.ChangeMap))
        {
            string mapText;
            try
            {
                mapText = PluginLocalizer.Get(_core)["menu_change_map"];
            }
            catch
            {
                mapText = "Change Map";
            }
            builder.AddOption(new SubmenuMenuOption(mapText, () => CreateChangeMapMenu(player)));
        }

        // Change workshop map
        if (HasPermission(player, _config.Permissions.ChangeWSMap))
        {
            string wsMapText;
            try
            {
                wsMapText = PluginLocalizer.Get(_core)["menu_change_ws_map"];
            }
            catch
            {
                wsMapText = "Change Workshop Map";
            }
            builder.AddOption(new SubmenuMenuOption(wsMapText, () => CreateChangeWSMapMenu(player)));
        }

        if (HasPermission(player, _config.Permissions.HeadshotMode))
        {
            var hsOnText = PluginLocalizer.Get(_core)["menu_headshot_on"];
            var hsOn = new ButtonMenuOption(hsOnText) { CloseAfterClick = true };
            hsOn.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.HeadshotOn, "hson");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(hsOn);

            var hsOffText = PluginLocalizer.Get(_core)["menu_headshot_off"];
            var hsOff = new ButtonMenuOption(hsOffText) { CloseAfterClick = true };
            hsOff.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.HeadshotOff, "hsoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(hsOff);
        }

        if (HasPermission(player, _config.Permissions.BunnyHop))
        {
            var bunnyOnText = PluginLocalizer.Get(_core)["menu_bunny_on"];
            var bunnyOn = new ButtonMenuOption(bunnyOnText) { CloseAfterClick = true };
            bunnyOn.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.BunnyOn, "bunnyon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(bunnyOn);

            var bunnyOffText = PluginLocalizer.Get(_core)["menu_bunny_off"];
            var bunnyOff = new ButtonMenuOption(bunnyOffText) { CloseAfterClick = true };
            bunnyOff.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.BunnyOff, "bunnyoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(bunnyOff);
        }

        if (HasPermission(player, _config.Permissions.RespawnMode))
        {
            var respawnOnText = PluginLocalizer.Get(_core)["menu_respawn_on"];
            var respawnOn = new ButtonMenuOption(respawnOnText) { CloseAfterClick = true };
            respawnOn.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RespawnOn, "respawnon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(respawnOn);

            var respawnOffText = PluginLocalizer.Get(_core)["menu_respawn_off"];
            var respawnOff = new ButtonMenuOption(respawnOffText) { CloseAfterClick = true };
            respawnOff.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RespawnOff, "respawnoff");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(respawnOff);
        }

        return builder.Build();
    }

    private IMenuAPI CreateRestartGameMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_restart_game"];
        }
        catch
        {
            title = "Restart Game";
        }
        builder.Design.SetMenuTitle(title);

        // Select seconds (1-10)
        string delayText;
        try
        {
            delayText = PluginLocalizer.Get(_core)["menu_restart_delay"];
        }
        catch
        {
            delayText = "Delay (Seconds)";
        }
        var slider = new SliderMenuOption(delayText, 1, 10, 2, 1);
        builder.AddOption(slider);

        string nowText;
        try
        {
            nowText = PluginLocalizer.Get(_core)["menu_restart_now"];
        }
        catch
        {
            nowText = "Restart Now";
        }
        var btn = new ButtonMenuOption(nowText) { CloseAfterClick = true };
        btn.Click += (_, args) =>
        {
            var caller = args.Player;
            var seconds = (int)slider.GetValue(caller);
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.RestartGame, "rr");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {seconds}"));
            return ValueTask.CompletedTask;
        };
        builder.AddOption(btn);

        return builder.Build();
    }

    private IMenuAPI CreateChangeMapMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_change_map"];
        }
        catch
        {
            title = "Change Map";
        }
        builder.Design.SetMenuTitle(title);

        if (_config.GameMaps.Maps != null)
        {
            foreach (var map in _config.GameMaps.Maps)
            {
                var btn = new ButtonMenuOption(map.Value) { CloseAfterClick = true };
                btn.Click += (_, args) =>
                {
                    var caller = args.Player;
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeMap, "map");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {map.Key}"));
                    return ValueTask.CompletedTask;
                };
                builder.AddOption(btn);
            }
        }

        return builder.Build();
    }

    private IMenuAPI CreateChangeWSMapMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_change_ws_map"];
        }
        catch
        {
            title = "Change Workshop Map";
        }
        builder.Design.SetMenuTitle(title);

        if (_config.WorkshopMaps.Maps != null)
        {
            foreach (var map in _config.WorkshopMaps.Maps)
            {
                var displayName = map.Key;
                var workshopId = map.Value;

                var btn = new ButtonMenuOption(displayName) { CloseAfterClick = true };
                btn.Click += (_, args) =>
                {
                    var caller = args.Player;
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeWSMap, "wsmap");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {workshopId}"));
                    return ValueTask.CompletedTask;
                };
                builder.AddOption(btn);
            }
        }

        return builder.Build();
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }
}


