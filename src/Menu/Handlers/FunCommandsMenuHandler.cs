using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class FunCommandsMenuHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;

    private enum FunAction
    {
        Slap,
        Slay,
        Respawn,
        Team,
        Noclip,
        Freeze,
        Unfreeze,
        Resize,
        Drug,
        Burn,
        Disarm,
        Speed,
        Gravity,
        Hp,
        Money,
        Give
    }

    public FunCommandsMenuHandler(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
    }

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();

        string title = T("menu_fun_commands", "Fun Commands");
        builder.Design.SetMenuTitle(title);

        if (HasPermission(player, _config.Permissions.Slap))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_slap"], () => BuildPlayerSelectMenu(player, FunAction.Slap)));

        if (HasPermission(player, _config.Permissions.Slay))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_slay"], () => BuildPlayerSelectMenu(player, FunAction.Slay)));

        if (HasPermission(player, _config.Permissions.Respawn))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_respawn"], () => BuildPlayerSelectMenu(player, FunAction.Respawn)));

        if (HasPermission(player, _config.Permissions.ChangeTeam))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_team"], () => BuildPlayerSelectMenu(player, FunAction.Team)));

        if (HasPermission(player, _config.Permissions.NoClip))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_noclip"], () => BuildPlayerSelectMenu(player, FunAction.Noclip)));

        if (HasPermission(player, _config.Permissions.Freeze))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_freeze"], () => BuildPlayerSelectMenu(player, FunAction.Freeze)));

        if (HasPermission(player, _config.Permissions.Unfreeze))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_unfreeze"], () => BuildPlayerSelectMenu(player, FunAction.Unfreeze)));

        if (HasPermission(player, _config.Permissions.Resize))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_resize"], () => BuildPlayerSelectMenu(player, FunAction.Resize)));

        if (HasPermission(player, _config.Permissions.Drug))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_drug"], () => BuildPlayerSelectMenu(player, FunAction.Drug)));

        if (HasPermission(player, _config.Permissions.Burn))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_burn"], () => BuildPlayerSelectMenu(player, FunAction.Burn)));

        if (HasPermission(player, _config.Permissions.Disarm))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_disarm"], () => BuildPlayerSelectMenu(player, FunAction.Disarm)));

        if (HasPermission(player, _config.Permissions.Speed))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_speed"], () => BuildPlayerSelectMenu(player, FunAction.Speed)));

        if (HasPermission(player, _config.Permissions.Gravity))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_gravity"], () => BuildPlayerSelectMenu(player, FunAction.Gravity)));

        if (HasPermission(player, _config.Permissions.Hp))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_hp"], () => BuildPlayerSelectMenu(player, FunAction.Hp)));

        if (HasPermission(player, _config.Permissions.Money))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_money"], () => BuildPlayerSelectMenu(player, FunAction.Money)));

        if (HasPermission(player, _config.Permissions.Give))
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_give"], () => BuildPlayerSelectMenu(player, FunAction.Give)));

        return builder.Build();
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private IMenuAPI BuildPlayerSelectMenu(IPlayer admin, FunAction action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_player", "Select Player"));

        var players = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .ToList();

        if (players.Count == 0)
        {
            var empty = new ButtonMenuOption(T("menu_no_players", "No players found")) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
            return builder.Build();
        }

        foreach (var target in players)
        {
            var keepMenuOpen = action == FunAction.Noclip;
            var btn = new ButtonMenuOption(target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID])
            {
                CloseAfterClick = !keepMenuOpen
            };
            btn.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() =>
                {
                    if (action == FunAction.Team)
                    {
                        OpenTeamSelectMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Slap)
                    {
                        OpenSlapDamageMenu(adminPlayer, target);
                    }
                    else if (action == FunAction.Resize)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0.50f, 0.80f, 1.00f, 1.20f, 1.50f, 2.00f]);
                    }
                    else if (action == FunAction.Speed)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0.50f, 0.80f, 1.00f, 1.20f, 1.50f, 2.00f]);
                    }
                    else if (action == FunAction.Gravity)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0.20f, 0.50f, 1.00f, 1.50f, 2.00f]);
                    }
                    else if (action == FunAction.Hp)
                    {
                        OpenValueMenu(adminPlayer, target, action, [1f, 25f, 50f, 100f, 200f]);
                    }
                    else if (action == FunAction.Money)
                    {
                        OpenValueMenu(adminPlayer, target, action, [0f, 800f, 16000f]);
                    }
                    else if (action == FunAction.Give)
                    {
                        OpenGiveItemMenu(adminPlayer, target);
                    }
                    else
                    {
                        ExecuteFunAction(adminPlayer, target, action);
                    }
                });
                return ValueTask.CompletedTask;
            };
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private void OpenTeamSelectMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_team", "Select Team"));

        AddTeamButton(builder, admin, target, T("team_t", "Terrorist"), "t");
        AddTeamButton(builder, admin, target, T("team_ct", "Counter-Terrorist"), "ct");
        AddTeamButton(builder, admin, target, T("team_spec", "Spectator"), "spec");

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void AddTeamButton(IMenuBuilderAPI builder, IPlayer admin, IPlayer target, string label, string teamArg)
    {
        var option = new ButtonMenuOption(label) { CloseAfterClick = true };
        option.Click += (_, args) =>
        {
            var caller = args.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeTeam, "team");
            var targetId = target.PlayerID;
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {targetId} {teamArg}"));
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }

    private void OpenSlapDamageMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_duration", "Select Duration"));

        var strengths = new[] { 1, 5, 10, 50, 90, 100 };
        foreach (var strength in strengths)
        {
            var value = strength;
            var option = new ButtonMenuOption(value.ToString()) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                var targetId = target.PlayerID;
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {targetId} {value}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenValueMenu(IPlayer admin, IPlayer target, FunAction action, IReadOnlyList<float> values)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_value", "Select Value"));

        foreach (var value in values)
        {
            var current = value;
            var option = new ButtonMenuOption(current.ToString("0.##")) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var targetId = target.PlayerID;
                var command = action switch
                {
                    FunAction.Resize => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Resize, "resize"),
                    FunAction.Speed => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Speed, "speed"),
                    FunAction.Gravity => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Gravity, "gravity"),
                    FunAction.Hp => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Hp, "hp"),
                    FunAction.Money => CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Money, "money"),
                    _ => string.Empty
                };

                if (string.IsNullOrWhiteSpace(command))
                {
                    return ValueTask.CompletedTask;
                }

                if (action is FunAction.Hp or FunAction.Money)
                {
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{command} {targetId} {(int)current}"));
                }
                else
                {
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{command} {targetId} {current.ToString("0.##")}"));
                }

                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenGiveItemMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_item", "Select Item"));

        var items = new[]
        {
            "weapon_ak47",
            "weapon_m4a1",
            "weapon_awp",
            "weapon_deagle",
            "weapon_hegrenade",
            "item_assaultsuit"
        };

        foreach (var item in items)
        {
            var current = item;
            var option = new ButtonMenuOption(current) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var caller = args.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Give, "give");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private string T(string key, string fallback)
    {
        try
        {
            return PluginLocalizer.Get(_core)[key];
        }
        catch
        {
            return fallback;
        }
    }

    private void ExecuteFunAction(IPlayer admin, IPlayer target, FunAction action)
    {
        var targetId = target.PlayerID;

        switch (action)
        {
            case FunAction.Slap:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Slay:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slay, "slay");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Respawn:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Respawn, "respawn");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Noclip:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.NoClip, "noclip");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Freeze:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Unfreeze:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Unfreeze, "unfreeze");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Drug:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Drug, "drug");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} 10"));
                break;
            }
            case FunAction.Burn:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} 8 5"));
                break;
            }
            case FunAction.Disarm:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Disarm, "disarm");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
        }
    }
}


