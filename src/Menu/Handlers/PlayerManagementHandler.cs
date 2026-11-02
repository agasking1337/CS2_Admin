using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
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

    public IMenu CreateMenu(IPlayer player, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.player_mgmt")
            .WithTitle(T("menu_player_management"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        AddTargetAction(builder, player, parent, "menu_ban", "ban", _config.Permissions.Ban);
        AddTargetAction(builder, player, parent, "menu_warn", "warn", _config.Permissions.Warn);
        AddTargetAction(builder, player, parent, "menu_mute", "mute", _config.Permissions.Mute);
        AddTargetAction(builder, player, parent, "menu_gag", "gag", _config.Permissions.Gag);
        AddTargetAction(builder, player, parent, "menu_silence", "silence", _config.Permissions.Silence);
        AddTargetAction(builder, player, parent, "menu_kick", "kick", _config.Permissions.Kick);

        if (HasPermission(player, _config.Permissions.LastBan))
        {
            builder.Add(new ButtonComponent(T("menu_last_players"), ctx =>
            {
                var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.LastBan, "lastban");
                var caller = ctx.Player;
                _core.Scheduler.NextTick(() => caller.ExecuteCommand(cmd));
                return ValueTask.CompletedTask;
            }));
        }

        if (HasPermission(player, _config.Permissions.ListWarns))
        {
            builder.Add(new ButtonComponent(T("menu_warn_history"), ctx =>
            {
                var current = CreateMenu(ctx.Player, parent);
                var sub = BuildWarnHistoryPlayerMenu(ctx.Player, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildWarnHistoryPlayerMenu(IPlayer admin, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.warn_history.players")
            .WithTitle(T("menu_warn_history"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient).ToList();
        foreach (var target in players)
        {
            var t = target;
            var name = t.Controller.PlayerName ?? T("player_fallback_name", t.PlayerID);
            builder.Add(new ButtonComponent(name, ctx =>
            {
                var current = BuildWarnHistoryPlayerMenu(ctx.Player, parent);
                var sub = BuildWarnHistoryFilterMenu(ctx.Player, t, current);
                sub.Open(ctx.Player);
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private IMenu BuildWarnHistoryFilterMenu(IPlayer admin, IPlayer target, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.warn_history.filter.{target.SteamID}")
            .WithTitle(T("menu_warn_filter"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        AddWarnHistoryFilterOption(builder, target, parent, "menu_warn_filter_all", WarnHistoryFilter.All);
        AddWarnHistoryFilterOption(builder, target, parent, "menu_warn_filter_active", WarnHistoryFilter.Active);
        AddWarnHistoryFilterOption(builder, target, parent, "menu_warn_filter_expired", WarnHistoryFilter.Expired);
        AddWarnHistoryFilterOption(builder, target, parent, "menu_warn_filter_removed", WarnHistoryFilter.Removed);

        return builder.Build();
    }

    private void AddWarnHistoryFilterOption(IMenuBuilder builder, IPlayer target, IMenu? parent, string labelKey, WarnHistoryFilter filter)
    {
        builder.Add(new ButtonComponent(T(labelKey), ctx =>
        {
            var viewer = ctx.Player;
            _ = Task.Run(async () =>
            {
                var warns = await _warnManager.GetWarnHistoryAsync(target.SteamID, filter, 20);
                _core.Scheduler.NextTick(() =>
                {
                    if (viewer.IsValid)
                    {
                        var current = BuildWarnHistoryFilterMenu(viewer, target, parent);
                        var sub = BuildWarnHistoryListMenu(target, warns, current);
                        sub.Open(viewer);
                    }
                });
            });
            return ValueTask.CompletedTask;
        }));
    }

    private IMenu BuildWarnHistoryListMenu(IPlayer target, IReadOnlyList<Warn> warns, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.warn_history.list.{target.SteamID}")
            .WithTitle(T("menu_warn_history_for", target.Controller.PlayerName ?? T("unknown")));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        if (warns.Count == 0)
        {
            builder.Add(new ButtonComponent(T("warn_history_empty"), _ => ValueTask.CompletedTask));
            return builder.Build();
        }

        foreach (var warn in warns)
        {
            var status = warn.Status switch
            {
                WarnStatus.Active => T("active"),
                WarnStatus.Expired => T("expired"),
                WarnStatus.Removed => T("removed"),
                _ => T("unknown")
            };

            var created = warn.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            var text = $"{created} | {status} | {Truncate(warn.Reason, 28)}";
            builder.Add(new ButtonComponent(text, _ => ValueTask.CompletedTask));
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

    private void AddTargetAction(IMenuBuilder builder, IPlayer admin, IMenu? parent, string translationKey, string action, string permission)
    {
        if (!HasPermission(admin, permission))
            return;

        string text = T(translationKey);
        builder.Add(new ButtonComponent(text, ctx =>
        {
            var current = CreateMenu(ctx.Player, parent);
            var sub = BuildSelectPlayerMenu(ctx.Player, action, current);
            sub.Open(ctx.Player);
            return ValueTask.CompletedTask;
        }));
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private IMenu BuildSelectPlayerMenu(IPlayer admin, string action, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.select_player.{action}")
            .WithTitle(T("menu_select_player"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var players = _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && p.PlayerID != admin.PlayerID && !p.IsFakeClient).ToList();
        if (players.Count == 0)
        {
            builder.Add(new ButtonComponent(T("menu_no_players"), _ => ValueTask.CompletedTask));
            return builder.Build();
        }

        foreach (var target in players)
        {
            var t = target;
            var fallbackName = T("player_fallback_name", t.PlayerID);
            var name = t.Controller.PlayerName ?? fallbackName;

            builder.Add(new ButtonComponent(name, ctx =>
            {
                var adminPlayer = ctx.Player;
                _core.Scheduler.NextTick(() =>
                {
                    var current = BuildSelectPlayerMenu(adminPlayer, action, parent);
                    OpenReasonMenu(adminPlayer, t, action, current);
                });
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private void OpenReasonMenu(IPlayer admin, IPlayer target, string action, IMenu? parent = null)
    {
        var menu = BuildReasonMenu(admin, target, action, parent);
        menu.Open(admin);
    }

    private IMenu BuildReasonMenu(IPlayer admin, IPlayer target, string action, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.reason.{action}.{target.PlayerID}")
            .WithTitle(T("menu_select_reason"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        var reasons = GetReasonsForAction(action);
        foreach (var reason in reasons)
        {
            var r = reason;
            builder.Add(new ButtonComponent(r, ctx =>
            {
                var adminPlayer = ctx.Player;
                _core.Scheduler.NextTick(() =>
                {
                    if (action == "kick")
                    {
                        ExecuteKick(adminPlayer, target, r);
                    }
                    else if (action == "warn")
                    {
                        ExecuteWarn(adminPlayer, target, r);
                    }
                    else
                    {
                        var current = BuildReasonMenu(adminPlayer, target, action, parent);
                        OpenDurationMenu(adminPlayer, target, action, r, current);
                    }
                });
                return ValueTask.CompletedTask;
            }));
        }

        return builder.Build();
    }

    private void OpenDurationMenu(IPlayer admin, IPlayer target, string action, string reason, IMenu? parent = null)
    {
        var builder = _core.Menu.CreateMenu($"cs2_admin.duration.{action}.{target.PlayerID}")
            .WithTitle(T("menu_select_duration"));

        if (parent != null)
        {
            builder.WithParent(parent);
        }

        foreach (var item in _config.Sanctions.Durations)
        {
            var label = item.Name;
            var minutes = item.Minutes;
            builder.Add(new ButtonComponent(label, ctx =>
            {
                var adminPlayer = ctx.Player;
                _core.Scheduler.NextTick(() => ExecuteTimedAction(adminPlayer, target, action, minutes, reason));
                return ValueTask.CompletedTask;
            }));
        }

        builder.Build().Open(admin);
    }

    private IReadOnlyList<string> GetReasonsForAction(string action)
    {
        return _config.Sanctions.Reasons;
    }

    private void ExecuteKick(IPlayer admin, IPlayer target, string reason)
    {
        var targetId = target.PlayerID;
        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Kick, "kick");
        ExecuteAndReopenPlayerSelection(admin, "kick", $"{cmd} {targetId} {reason}");
    }

    private void ExecuteTimedAction(IPlayer admin, IPlayer target, string action, int minutes, string reason)
    {
        var duration = minutes <= 0 ? -1 : minutes;
        var targetId = target.PlayerID;

        string? cmdName = action switch
        {
            "ban" => "ban",
            "warn" => "warn",
            "mute" => "mute",
            "gag" => "gag",
            "silence" => "silence",
            _ => null
        };

        if (string.IsNullOrEmpty(cmdName))
            return;

        var cmd = CommandAliasUtils.ToSwAlias(cmdName);
        _core.Scheduler.NextTick(() => admin.ExecuteCommand($"{cmd} {targetId} {duration} {reason}"));
        ReopenDurationMenuWithRetry(admin, target, action, reason);
    }

    private void ReopenDurationMenuWithRetry(IPlayer admin, IPlayer target, string action, string reason)
    {
        void OpenRelevantMenu()
        {
            if (!admin.IsValid)
            {
                return;
            }

            if (target.IsValid)
            {
                OpenDurationMenu(admin, target, action, reason);
                return;
            }

            BuildSelectPlayerMenu(admin, action).Open(admin);
        }

        _core.Scheduler.DelayBySeconds(0.06f, OpenRelevantMenu);
        _core.Scheduler.DelayBySeconds(0.20f, OpenRelevantMenu);
    }

    private void ExecuteWarn(IPlayer admin, IPlayer target, string reason)
    {
        var targetId = target.PlayerID;
        var cmd = CommandAliasUtils.GetPreferredExecutionAlias(_config.Commands.Warn, "warn");
        ExecuteAndReopenPlayerSelection(admin, "warn", $"{cmd} {targetId} {reason}");
    }

    private void ExecuteAndReopenPlayerSelection(IPlayer admin, string action, string command)
    {
        _core.Scheduler.NextTick(() => admin.ExecuteCommand(command));
        _core.Scheduler.DelayBySeconds(0.08f, () =>
        {
            if (!admin.IsValid)
            {
                return;
            }

            BuildSelectPlayerMenu(admin, action).Open(admin);
        });
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
