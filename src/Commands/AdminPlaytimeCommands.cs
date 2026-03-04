using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class AdminPlaytimeCommands
{
    private readonly ISwiftlyCore _core;
    private readonly AdminPlaytimeDbManager _adminPlaytimeDbManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly DiscordWebhook _discord;
    private readonly PermissionsConfig _permissions;
    private readonly AdminPlaytimeConfig _adminPlaytimeConfig;

    public AdminPlaytimeCommands(
        ISwiftlyCore core,
        AdminPlaytimeDbManager adminPlaytimeDbManager,
        AdminLogManager adminLogManager,
        DiscordWebhook discord,
        PermissionsConfig permissions,
        AdminPlaytimeConfig adminPlaytimeConfig)
    {
        _core = core;
        _adminPlaytimeDbManager = adminPlaytimeDbManager;
        _adminLogManager = adminLogManager;
        _discord = discord;
        _permissions = permissions;
        _adminPlaytimeConfig = adminPlaytimeConfig;
    }

    public void OnAdminTimeCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.AdminTime))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        _ = Task.Run(async () =>
        {
            var topAdmins = await _adminPlaytimeDbManager.GetTopAdminsAsync(_adminPlaytimeConfig.MenuTopLimit);

            _core.Scheduler.NextTick(() =>
            {
                if (context.IsSentByPlayer && context.Sender != null)
                {
                    var viewer = context.Sender;
                    _core.Scheduler.DelayBySeconds(0.1f, () =>
                    {
                        if (viewer.IsValid)
                        {
                            OpenPlaytimeMenu(viewer, topAdmins);
                        }
                    });
                    return;
                }

                if (topAdmins.Count == 0)
                {
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["admintime_no_data"]}");
                    return;
                }

                context.Reply(PluginLocalizer.Get(_core)["admintime_console_header"]);
                for (var i = 0; i < topAdmins.Count; i++)
                {
                    var entry = topAdmins[i];
                    context.Reply(PluginLocalizer.Get(_core)["admintime_console_entry", i + 1, entry.PlayerName, entry.SteamId, entry.PlaytimeMinutes]);
                }
            });
        });
    }

    public void OnAdminTimeSendCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.AdminTimeSend))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            var topAdmins = await _adminPlaytimeDbManager.GetTopAdminsAsync(_adminPlaytimeConfig.DiscordTopLimit);
            if (topAdmins.Count == 0)
            {
                _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["admintime_no_data"]}"));
                return;
            }

            await _discord.SendAdminTimeNotificationAsync(topAdmins);
            await _adminLogManager.AddLogAsync("admintimesend", adminName, adminSteamId, null, null, $"count={topAdmins.Count}");

            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["admintime_sent"]}"));
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Admin playtime top list sent to Discord by {Admin}", adminName);
        });
    }

    private void OpenPlaytimeMenu(IPlayer player, IReadOnlyList<AdminPlaytime> entries)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_admin_playtime"]);

        if (entries.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(PluginLocalizer.Get(_core)["admintime_no_data"]) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var row = entries[i];
                var text = PluginLocalizer.Get(_core)["admintime_menu_entry", i + 1, row.PlayerName, row.PlaytimeMinutes];
                var button = new ButtonMenuOption(text) { CloseAfterClick = false };
                button.Click += (_, _) => ValueTask.CompletedTask;
                builder.AddOption(button);
            }
        }

        _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
        {
            return true;
        }

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
    }
}


