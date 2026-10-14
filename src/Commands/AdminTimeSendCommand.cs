using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class AdminTimeSendCommand : CommandBase
{
    private readonly AdminPlaytimeDbManager _adminPlaytimeDbManager;
    private readonly DiscordBotService _discord;
    private readonly AdminPlaytimeConfig _adminPlaytimeConfig;

    public AdminTimeSendCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminPlaytimeDbManager adminPlaytimeDbManager,
        DiscordBotService discord,
        AdminPlaytimeConfig adminPlaytimeConfig)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminPlaytimeDbManager = adminPlaytimeDbManager;
        _discord = discord;
        _adminPlaytimeConfig = adminPlaytimeConfig;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.AdminTimeSend))
            {
                Reply(context, "no_permission");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            var topAdmins = await _adminPlaytimeDbManager.GetTopAdminsAsync(_adminPlaytimeConfig.DiscordTopLimit);
            if (topAdmins.Count == 0)
            {
                await OnMainThreadAsync(() => Reply(context, "admintime_no_data"));
                return;
            }

            await _discord.SendAdminTimeNotificationAsync(topAdmins);
            await _adminPlaytimeDbManager.ResetAllAsync();
            await AdminLogManager.AddLogAsync("admintimesend", adminName, adminSteamId, null, null, $"count={topAdmins.Count}");

            await OnMainThreadAsync(() => Reply(context, "admintime_sent"));
            Core.Logger.LogInformationIfEnabled("Admin playtime top list sent to Discord by {Admin} and playtime counters reset", adminName);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "AdminTimeSend command failed");
        }
    }
}
