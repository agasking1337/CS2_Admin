using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class HsToggleCommand : CommandBase
{
    public HsToggleCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.HeadshotMode);

            if (!HasPerm(context, Permissions.HeadshotMode))
            {
                Reply(context, "no_permission");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            if ((args.Length > 0 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase)) || context.CommandName.Contains("off", StringComparison.OrdinalIgnoreCase))
            {
                Core.Engine.ExecuteCommand("mp_damage_headshot_only 0");
                BroadcastNotification(adminName, "headshot_disabled");
                _ = AdminLogManager.AddLogAsync("hsoff", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_damage_headshot_only=0");
                Core.Logger.LogInformation("{Admin} disabled headshot-only mode", adminName);
            }
            else
            {
                Core.Engine.ExecuteCommand("mp_damage_headshot_only 1");
                BroadcastNotification(adminName, "headshot_enabled");
                _ = AdminLogManager.AddLogAsync("hson", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_damage_headshot_only=1");
                Core.Logger.LogInformation("{Admin} enabled headshot-only mode", adminName);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "HsToggle command failed");
        }
    }
}



