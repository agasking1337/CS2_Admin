using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
using CS2_Admin.Config;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class FunCommandsMenuHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;

    private enum FunAction
    {
        Slap, God, Slay, Respawn, Team, Noclip, Goto, Bring, Freeze, Unfreeze, Resize, Blind, Glow, Beacon, Bury, Unbury, Burn, Disarm, Speed, Gravity, Hp, Money, Give
    }

    public FunCommandsMenuHandler(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
    }

    public IMenu CreateMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun")
            .WithTitle(T("menu_fun_commands"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        AddActionOption(builder, player, parent, _config.Permissions.Slap, PluginLocalizer.Get(_core)["menu_slap"], FunAction.Slap);
        AddActionOption(builder, player, parent, _config.Permissions.God, T("menu_god", "Toggle God"), FunAction.God);
        AddActionOption(builder, player, parent, _config.Permissions.Slay, PluginLocalizer.Get(_core)["menu_slay"], FunAction.Slay);
        AddActionOption(builder, player, parent, _config.Permissions.Respawn, PluginLocalizer.Get(_core)["menu_respawn"], FunAction.Respawn);
        AddActionOption(builder, player, parent, _config.Permissions.ChangeTeam, PluginLocalizer.Get(_core)["menu_team"], FunAction.Team);
        AddActionOption(builder, player, parent, _config.Permissions.NoClip, PluginLocalizer.Get(_core)["menu_noclip"], FunAction.Noclip);
        AddActionOption(builder, player, parent, _config.Permissions.Goto, T("menu_goto"), FunAction.Goto);
        AddActionOption(builder, player, parent, _config.Permissions.Bring, T("menu_bring"), FunAction.Bring);
        AddActionOption(builder, player, parent, _config.Permissions.Freeze, PluginLocalizer.Get(_core)["menu_freeze"], FunAction.Freeze);
        AddActionOption(builder, player, parent, _config.Permissions.Unfreeze, PluginLocalizer.Get(_core)["menu_unfreeze"], FunAction.Unfreeze);
        AddActionOption(builder, player, parent, _config.Permissions.Resize, PluginLocalizer.Get(_core)["menu_resize"], FunAction.Resize);
        AddActionOption(builder, player, parent, _config.Permissions.Blind, T("menu_blind", "Blind Player"), FunAction.Blind);
        AddActionOption(builder, player, parent, _config.Permissions.Glow, T("menu_glow", "Glow Player"), FunAction.Glow);
        AddActionOption(builder, player, parent, _config.Permissions.Beacon, PluginLocalizer.Get(_core)["menu_beacon"], FunAction.Beacon);
        AddActionOption(builder, player, parent, _config.Permissions.Bury, T("menu_bury", "Bury Player"), FunAction.Bury);
        AddActionOption(builder, player, parent, _config.Permissions.Unbury, T("menu_unbury", "Unbury Player"), FunAction.Unbury);
        AddActionOption(builder, player, parent, _config.Permissions.Burn, PluginLocalizer.Get(_core)["menu_burn"], FunAction.Burn);
        AddActionOption(builder, player, parent, _config.Permissions.Disarm, PluginLocalizer.Get(_core)["menu_disarm"], FunAction.Disarm);
        AddActionOption(builder, player, parent, _config.Permissions.Speed, PluginLocalizer.Get(_core)["menu_speed"], FunAction.Speed);
        AddActionOption(builder, player, parent, _config.Permissions.Gravity, PluginLocalizer.Get(_core)["menu_gravity"], FunAction.Gravity);
        AddActionOption(builder, player, parent, _config.Permissions.Hp, PluginLocalizer.Get(_core)["menu_hp"], FunAction.Hp);
        AddActionOption(builder, player, parent, _config.Permissions.Money, PluginLocalizer.Get(_core)["menu_money"], FunAction.Money);
        AddActionOption(builder, player, parent, _config.Permissions.Give, PluginLocalizer.Get(_core)["menu_give"], FunAction.Give);

        return builder.Build();
    }

    private void AddActionOption(IMenuBuilder builder, IPlayer player, IMenu? parent, string permission, string label, FunAction action)
    {
        if (!HasPermission(player, permission)) return;

        builder.Add(new ButtonComponent(label, ctx =>
        {
            var current = CreateMenu(ctx.Player, parent);
            var subMenu = BuildPlayerSelectMenu(ctx.Player, action, current);
            subMenu.Open(ctx.Player);
            return ValueTask.CompletedTask;
        }));
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private IMenu BuildPlayerSelectMenu(IPlayer admin, FunAction action, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.fun.player.{action}")
            .WithTitle(T("menu_select_player", "Select Player"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var players = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .ToList();

        players = ApplyPlayerFilter(players, action);

        if (players.Count == 0)
        {
            builder.Add(new ButtonComponent(T("menu_no_players"), _ => ValueTask.CompletedTask));
            return builder.Build();
        }

        foreach (var target in players)
        {
            var name = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];
            var t = target;

            if (action is FunAction.Team or FunAction.Slap or FunAction.Blind or FunAction.Glow or FunAction.Resize or FunAction.Speed or FunAction.Gravity or FunAction.Hp or FunAction.Money or FunAction.Give or FunAction.Beacon or FunAction.Freeze or FunAction.Burn)
            {
                builder.Add(new ButtonComponent(name, ctx =>
                {
                    var current = BuildPlayerSelectMenu(ctx.Player, action, parent);
                    var sub = BuildActionSubMenu(ctx.Player, t, action, current);
                    sub.Open(ctx.Player);
                    return ValueTask.CompletedTask;
                }));
            }
            else
            {
                builder.Add(new ButtonComponent(name, ctx =>
                {
                    ExecuteFunAction(ctx.Player, t, action);
                    return ValueTask.CompletedTask;
                }));
            }
        }

        return builder.Build();
    }

    private IMenu BuildActionSubMenu(IPlayer admin, IPlayer target, FunAction action, IMenu? parent = null)
    {
        return action switch
        {
            FunAction.Team => BuildTeamSelectMenu(admin, target, parent),
            FunAction.Slap => BuildSlapDamageMenu(admin, target, parent),
            FunAction.Blind => BuildBlindDurationMenu(admin, target, parent),
            FunAction.Glow => BuildGlowColorMenu(admin, target, parent),
            FunAction.Resize => BuildValueMenu(admin, target, action, [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.2f, 2.4f, 2.6f, 2.8f, 3.0f], parent),
            FunAction.Speed => BuildValueMenu(admin, target, action, [0.50f, 0.80f, 1.00f, 1.20f, 1.50f, 2.00f], parent),
            FunAction.Gravity => BuildValueMenu(admin, target, action, [0.20f, 0.50f, 1.00f, 1.50f, 2.00f], parent),
            FunAction.Hp => BuildValueMenu(admin, target, action, [1f, 25f, 50f, 100f, 200f], parent),
            FunAction.Money => BuildValueMenu(admin, target, action, [0f, 800f, 16000f], parent),
            FunAction.Give => BuildGiveItemMenu(admin, target, parent),
            FunAction.Beacon => BuildBeaconDurationMenu(admin, target, parent),
            FunAction.Freeze or FunAction.Burn => BuildTimedActionDurationMenu(admin, target, action, parent),
            _ => BuildTeamSelectMenu(admin, target, parent)
        };
    }

    private IMenu BuildTeamSelectMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun.team")
            .WithTitle(T("menu_select_team", "Select Team"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        int currentTeam = target.PlayerPawn?.IsValid == true ? target.PlayerPawn.TeamNum : 0;

        if (currentTeam != 2) AddTeamButton(builder, target, T("team_t"), "t");
        if (currentTeam != 3) AddTeamButton(builder, target, T("team_ct"), "ct");
        if (currentTeam != 1) AddTeamButton(builder, target, T("team_spec"), "spec");

        return builder.Build();
    }

    private void AddTeamButton(IMenuBuilder builder, IPlayer target, string label, string teamArg)
    {
        builder.Add(new ButtonComponent(label, ctx =>
        {
            var caller = ctx.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.ChangeTeam, "team");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {teamArg}"));
            return ValueTask.CompletedTask;
        }));
    }

    private IMenu BuildSlapDamageMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun.slap")
            .WithTitle(T("menu_select_duration", "Select Duration"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var damages = new[] { 0, 5, 10, 50, 90, 100 };
        foreach (var damage in damages)
        {
            var value = damage;
            builder.Add(new ButtonComponent(value.ToString(), ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Slap, "slap");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {value}"));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildValueMenu(IPlayer admin, IPlayer target, FunAction action, IReadOnlyList<float> values, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.fun.val.{action}")
            .WithTitle(T("menu_select_value", "Select Value"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        foreach (var value in values)
        {
            var current = value;
            builder.Add(new ButtonComponent(current.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), ctx =>
            {
                var caller = ctx.Player;
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

                if (!string.IsNullOrWhiteSpace(command))
                {
                    if (action is FunAction.Hp or FunAction.Money)
                    {
                        _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{command} {targetId} {(int)current}"));
                    }
                    else
                    {
                        _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{command} {targetId} {current.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));
                    }
                }
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildBlindDurationMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun.blind")
            .WithTitle(T("menu_select_duration", "Select Duration"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var durations = new[] { 3, 5, 10, 20, 30, 45 };
        foreach (var duration in durations)
        {
            var current = duration;
            builder.Add(new ButtonComponent($"{current}s", ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Blind, "blind");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildGlowColorMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun.glow")
            .WithTitle(T("menu_select_value", "Select Value"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var presets = new (string Label, string Args)[]
        {
            (T("glow_color_red"), "255 64 64 180"),
            (T("glow_color_green"), "64 255 64 180"),
            (T("glow_color_blue"), "64 160 255 180"),
            (T("glow_color_yellow"), "255 220 64 180"),
            (T("glow_color_purple"), "180 64 255 180"),
            (T("glow_color_cyan"), "64 255 255 180"),
            (T("glow_color_white"), "255 255 255 180"),
            (T("menu_off"), "off")
        };

        foreach (var preset in presets)
        {
            var current = preset;
            builder.Add(new ButtonComponent(current.Label, ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Glow, "glow");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current.Args}"));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildBeaconDurationMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun.beacon")
            .WithTitle(T("menu_select_duration", "Select Duration"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        builder.Add(new ButtonComponent(T("menu_off", "Off"), ctx =>
        {
            var caller = ctx.Player;
            var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
            _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} off"));
            return ValueTask.CompletedTask;
        }));

        var durations = new[] { 5, 10, 20, 40, 60, 120 };
        foreach (var duration in durations)
        {
            var current = duration;
            builder.Add(new ButtonComponent($"{current}s", ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildTimedActionDurationMenu(IPlayer admin, IPlayer target, FunAction action, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.fun.timed.{action}")
            .WithTitle(T("menu_select_duration", "Select Duration"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        builder.Add(new ButtonComponent(PluginLocalizer.Get(_core)["permanent"], ctx =>
        {
            var caller = ctx.Player;
            if (action == FunAction.Freeze)
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID}"));
            }
            else
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} -1 5"));
            }

            return ValueTask.CompletedTask;
        }));

        for (var seconds = 5; seconds <= 60; seconds += 5)
        {
            var current = seconds;
            builder.Add(new ButtonComponent($"{current}s", ctx =>
            {
                var caller = ctx.Player;
                if (action == FunAction.Freeze)
                {
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Freeze, "freeze");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                }
                else
                {
                    var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Burn, "burn");
                    _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current} 5"));
                }

                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildGiveItemMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.fun.give")
            .WithTitle(T("menu_select_item", "Select Item"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var items = new (string Display, string Item)[]
        {
            ("AK-47", "weapon_ak47"),
            ("M4A1", "weapon_m4a1"),
            ("AWP", "weapon_awp"),
            ("Deagle", "weapon_deagle"),
            ("SSG 08", "weapon_ssg08"),
            ("Flash", "weapon_flashbang"),
            ("HE Grenade", "weapon_hegrenade"),
            ("Smoke", "weapon_smokegrenade"),
            ("Molotov", "weapon_molotov"),
            ("Defuse Kit", "item_defuser"),
            ("Assault Suit", "item_assaultsuit")
        };

        foreach (var (display, item) in items)
        {
            var current = item;
            builder.Add(new ButtonComponent(display, ctx =>
            {
                var caller = ctx.Player;
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Give, "give");
                _core.Scheduler.NextTick(() => caller.ExecuteCommand($"{cmd} {target.PlayerID} {current}"));
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private string T(string key, string fallback, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            var value = args.Length == 0 ? localizer[key] : localizer[key, args];
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
                ? (args.Length == 0 ? fallback : string.Format(fallback, args))
                : value;
        }
        catch
        {
            return args.Length == 0 ? fallback : string.Format(fallback, args);
        }
    }

    private string T(string key, params object[] args) => T(key, key, args);

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
            case FunAction.God:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.God, "god");
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
            case FunAction.Goto:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Goto, "goto");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Bring:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Bring, "bring");
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
            case FunAction.Beacon:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Beacon, "beacon");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} 20"));
                break;
            }
            case FunAction.Bury:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Bury, "bury");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
                break;
            }
            case FunAction.Unbury:
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Unbury, "unbury");
                _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId}"));
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

    private static bool IsAlive(IPlayer player)
    {
        return player.PlayerPawn?.IsValid == true && player.PlayerPawn.Health > 0;
    }

    private List<IPlayer> ApplyPlayerFilter(List<IPlayer> players, FunAction action)
    {
        return action switch
        {
            FunAction.Respawn => players.Where(p => !IsAlive(p)).ToList(),
            FunAction.Beacon => players,
            _ => players.Where(IsAlive).ToList()
        };
    }
}
