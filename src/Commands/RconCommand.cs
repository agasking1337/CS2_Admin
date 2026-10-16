using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class RconCommand : CommandBase
{
    public RconCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Rcon);

            if (!HasPerm(context, Permissions.Rcon))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "rcon_usage");
                return;
            }

            var command = string.Join(" ", args);
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            Core.Engine.ExecuteCommand(command);

            foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                if (HasPerm(player, Permissions.Rcon) || HasPerm(player, Permissions.AdminRoot))
                {
                    player.SendChat($" \x02{L("prefix")}\x01 {L("rcon_executed", adminName, command)}");
                }
            }

            _ = AdminLogManager.AddLogAsync("rcon", adminName, context.Sender?.SteamID ?? 0, null, null, $"command={command}");
            Core.Logger.LogInformation("{Admin} executed rcon: {Command}", adminName, command);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Rcon command failed");
        }
    }
}

