using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class RestartCommand : CommandBase
{
    public RestartCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.RestartGame);

            if (!HasPerm(context, Permissions.RestartGame))
            {
                Reply(context, "no_permission");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            int seconds = 2;
            if (args.Length >= 1 && int.TryParse(args[0], out var parsed) && parsed > 0)
            {
                seconds = parsed;
            }

            BroadcastNotification(adminName, "restart_notification", seconds);

            Core.Engine.ExecuteCommand($"mp_restartgame {seconds}");

            _ = AdminLogManager.AddLogAsync("restart", adminName, context.Sender?.SteamID ?? 0, null, null, $"seconds={seconds}");
            Core.Logger.LogInformation("{Admin} restarted the game in {Seconds} second(s)", adminName, seconds);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Restart command failed");
        }
    }
}

