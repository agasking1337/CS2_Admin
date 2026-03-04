using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using CS2_Admin.Utils;

namespace CS2_Admin.Menu.Handlers;

public class PlayerManagementHandler : IAdminMenuHandler
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly WarnManager _warnManager;

    public PlayerManagementHandler(ISwiftlyCore core, PluginConfig config, WarnManager warnManager)
    {
        _core = core;
        _config = config;
        _warnManager = warnManager;
    }

    public IMenuAPI CreateMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();

        string title = PluginLocalizer.Get(_core)["menu_player_management"];
        builder.Design.SetMenuTitle(title);

        AddTargetAction(builder, player, "menu_ban", "Ban", "ban", _config.Permissions.Ban);
        AddTargetAction(builder, player, "menu_warn", "Warn", "warn", _config.Permissions.Warn);
        AddTargetAction(builder, player, "menu_mute", "Mute", "mute", _config.Permissions.Mute);
        AddTargetAction(builder, player, "menu_gag", "Gag", "gag", _config.Permissions.Gag);
        AddTargetAction(builder, player, "menu_silence", "Silence", "silence", _config.Permissions.Silence);
        AddTargetAction(builder, player, "menu_kick", "Kick", "kick", _config.Permissions.Kick);

        if (HasPermission(player, _config.Permissions.LastBan))
        {
            var lastPlayersBtn = new ButtonMenuOption(PluginLocalizer.Get(_core)["menu_last_players"]) { CloseAfterClick = true };
            lastPlayersBtn.Click += (_, args) =>
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.LastBan, "lastban");
                var caller = args.Player;
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(lastPlayersBtn);
        }

        if (HasPermission(player, _config.Permissions.ListWarns))
        {
            builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_warn_history"], () => BuildWarnHistoryPlayerMenu(player)));
        }

        return builder.Build();
    }

    private IMenuAPI BuildWarnHistoryPlayerMenu(IPlayer admin)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_warn_history"]);

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        foreach (var target in players)
        {
            var btn = new SubmenuMenuOption(
                target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID],
                () => BuildWarnHistoryFilterMenu(admin, target));
            builder.AddOption(btn);
        }

        return builder.Build();
    }

    private IMenuAPI BuildWarnHistoryFilterMenu(IPlayer admin, IPlayer target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_warn_filter"]);

        builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_warn_filter_all"], () => BuildWarnHistoryListMenu(target, WarnHistoryFilter.All)));
        builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_warn_filter_active"], () => BuildWarnHistoryListMenu(target, WarnHistoryFilter.Active)));
        builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_warn_filter_expired"], () => BuildWarnHistoryListMenu(target, WarnHistoryFilter.Expired)));
        builder.AddOption(new SubmenuMenuOption(PluginLocalizer.Get(_core)["menu_warn_filter_removed"], () => BuildWarnHistoryListMenu(target, WarnHistoryFilter.Removed)));

        return builder.Build();
    }

    private IMenuAPI BuildWarnHistoryListMenu(IPlayer target, WarnHistoryFilter filter)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_warn_history_for", target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"]]);

        var warns = _warnManager.GetWarnHistoryAsync(target.SteamID, filter, 20).GetAwaiter().GetResult();
        if (warns.Count == 0)
        {
            var empty = new ButtonMenuOption(PluginLocalizer.Get(_core)["warn_history_empty"]) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
            return builder.Build();
        }

        foreach (var warn in warns)
        {
            var status = warn.Status switch
            {
                WarnStatus.Active => PluginLocalizer.Get(_core)["warn_status_active"],
                WarnStatus.Expired => PluginLocalizer.Get(_core)["warn_status_expired"],
                WarnStatus.Removed => PluginLocalizer.Get(_core)["warn_status_removed"],
                _ => PluginLocalizer.Get(_core)["unknown"]
            };

            var created = warn.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            var text = $"{created} | {status} | {Truncate(warn.Reason, 28)}";
            var option = new ButtonMenuOption(text) { CloseAfterClick = true };
            option.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..(max - 3)] + "...";
    }

    private void AddTargetAction(IMenuBuilderAPI builder, IPlayer admin, string translationKey, string fallback, string action, string permission)
    {
        if (!HasPermission(admin, permission))
            return;

        string text = T(translationKey, fallback);

        builder.AddOption(new SubmenuMenuOption(text, () => BuildSelectPlayerMenu(admin, action)));
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private IMenuAPI BuildSelectPlayerMenu(IPlayer admin, string action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_player", "Select Player"));

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && p.PlayerID != admin.PlayerID).ToList();
        if (players.Count == 0)
        {
            var empty = new ButtonMenuOption(T("menu_no_players", "No players found")) { CloseAfterClick = true };
            empty.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(empty);
            return builder.Build();
        }

        foreach (var target in players)
        {
            var fallbackName = $"Player {target.PlayerID}";
            try
            {
                fallbackName = PluginLocalizer.Get(_core)["player_fallback_name", target.PlayerID];
            }
            catch
            {
                // fallbackName is already set
            }

            var button = new ButtonMenuOption(target.Controller.PlayerName ?? fallbackName) { CloseAfterClick = false };
            button.Click += (_, args) =>
            {
                var adminPlayer = args.Player;
                _core.Scheduler.NextTick(() =>
                {
                    OpenReasonMenu(adminPlayer, target, action);
                });
                return ValueTask.CompletedTask;
            };
            builder.AddOption(button);
        }

        return builder.Build();
    }

    private void OpenReasonMenu(IPlayer admin, IPlayer target, string action)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_select_reason"]);

        var reasons = GetReasonsForAction(action);
        foreach (var reason in reasons)
        {
            var option = new ButtonMenuOption(reason) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var adminPlayer = admin;
                _core.Scheduler.NextTick(() =>
                {
                    if (action == "kick")
                    {
                        ExecuteKick(adminPlayer, target, reason);
                    }
                    else if (action == "warn")
                    {
                        ExecuteWarn(adminPlayer, target, reason);
                    }
                    else
                    {
                        OpenDurationMenu(adminPlayer, target, action, reason);
                    }
                });
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private void OpenDurationMenu(IPlayer admin, IPlayer target, string action, string reason)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_select_duration"]);

        foreach (var item in _config.Sanctions.Durations)
        {
            var label = item.Name;
            var minutes = item.Minutes;
            var option = new ButtonMenuOption(label) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var adminPlayer = admin;
                _core.Scheduler.NextTick(() => ExecuteTimedAction(adminPlayer, target, action, minutes, reason));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(admin, builder.Build());
    }

    private IReadOnlyList<string> GetReasonsForAction(string action)
    {
        return _config.Sanctions.Reasons;
    }

    private void ExecuteKick(IPlayer admin, IPlayer target, string reason)
    {
        var targetId = target.PlayerID;
        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Kick, "kick");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} {reason}"));
    }

    private void ExecuteTimedAction(IPlayer admin, IPlayer target, string action, int minutes, string reason)
    {
        var duration = minutes <= 0 ? -1 : minutes;
        var targetId = target.PlayerID;

        string? cmdName = action switch
        {
            "ban" => _config.Commands.Ban.FirstOrDefault(),
            "warn" => _config.Commands.Warn.FirstOrDefault(),
            "mute" => _config.Commands.Mute.FirstOrDefault(),
            "gag" => _config.Commands.Gag.FirstOrDefault(),
            "silence" => _config.Commands.Silence.FirstOrDefault(),
            _ => null
        };

        if (string.IsNullOrEmpty(cmdName))
            return;

        cmdName = CommandAliasUtils.ToSwAlias(cmdName);
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmdName} {targetId} {duration} {reason}"));
    }

    private void ExecuteWarn(IPlayer admin, IPlayer target, string reason)
    {
        var targetId = target.PlayerID;
        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Warn, "warn");
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} {reason}"));
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
}


