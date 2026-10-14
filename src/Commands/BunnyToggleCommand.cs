using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class BunnyToggleCommand : CommandBase
{
    public BunnyToggleCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.BunnyHop);

            if (!HasPerm(context, Permissions.BunnyHop))
            {
                Reply(context, "no_permission");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            if ((args.Length > 0 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase)) ||
                context.CommandName.Contains("off", StringComparison.OrdinalIgnoreCase))
            {
                Core.Engine.ExecuteCommand("sv_enablebunnyhopping 0");
                Core.Engine.ExecuteCommand("sv_autobunnyhopping 0");
                BroadcastNotification(adminName, "bunny_disabled");
                _ = AdminLogManager.AddLogAsync("bunnyoff", adminName, context.Sender?.SteamID ?? 0, null, null, "sv_enablebunnyhopping=0;sv_autobunnyhopping=0");
                Core.Logger.LogInformation("{Admin} disabled bunny hop", adminName);
            }
            else
            {
                Core.Engine.ExecuteCommand("sv_enablebunnyhopping 1");
                Core.Engine.ExecuteCommand("sv_autobunnyhopping 1");
                BroadcastNotification(adminName, "bunny_enabled");
                _ = AdminLogManager.AddLogAsync("bunnyon", adminName, context.Sender?.SteamID ?? 0, null, null, "sv_enablebunnyhopping=1;sv_autobunnyhopping=1");
                Core.Logger.LogInformation("{Admin} enabled bunny hop", adminName);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "BunnyToggle command failed");
        }
    }
}

