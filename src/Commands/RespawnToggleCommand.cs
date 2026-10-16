using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class RespawnToggleCommand : CommandBase
{
    public RespawnToggleCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.RespawnMode);

            if (!HasPerm(context, Permissions.RespawnMode))
            {
                Reply(context, "no_permission");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            if ((args.Length > 0 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase)) || context.CommandName.Contains("off", StringComparison.OrdinalIgnoreCase))
            {
                Core.Engine.ExecuteCommand("mp_respawn_on_death_ct 0");
                Core.Engine.ExecuteCommand("mp_respawn_on_death_t 0");
                BroadcastNotification(adminName, "respawn_mode_disabled");
                _ = AdminLogManager.AddLogAsync("respawnoff", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_respawn_on_death_ct=0;mp_respawn_on_death_t=0");
                Core.Logger.LogInformation("{Admin} disabled instant respawn mode", adminName);
            }
            else
            {
                Core.Engine.ExecuteCommand("mp_respawn_on_death_ct 1");
                Core.Engine.ExecuteCommand("mp_respawn_on_death_t 1");
                BroadcastNotification(adminName, "respawn_mode_enabled");
                _ = AdminLogManager.AddLogAsync("respawnon", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_respawn_on_death_ct=1;mp_respawn_on_death_t=1");
                Core.Logger.LogInformation("{Admin} enabled instant respawn mode", adminName);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "RespawnToggle command failed");
        }
    }
}


