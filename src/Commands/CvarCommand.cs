using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class CvarCommand : CommandBase
{
    public CvarCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Cvar);

            if (!HasPerm(context, Permissions.Cvar))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "cvar_usage");
                return;
            }

            var cvarName = args[0];
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            if (args.Length == 1)
            {
                var cvar = Core.ConVar.Find<string>(cvarName);
                if (cvar == null)
                {
                    Reply(context, "cvar_not_found", cvarName);
                    return;
                }

                ReplyRaw(context, $"{cvarName} = {cvar.Value}");
            }
            else
            {
                var value = string.Join(" ", args.Skip(1));
                Core.Engine.ExecuteCommand($"{cvarName} {value}");

                foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                {
                    if (HasPerm(player, Permissions.Cvar) || HasPerm(player, Permissions.AdminRoot))
                    {
                        player.SendChat($" \x02{L("prefix")}\x01 {adminName} set {cvarName} to {value}");
                    }
                }

                _ = AdminLogManager.AddLogAsync("cvar", adminName, context.Sender?.SteamID ?? 0, null, null, $"cvar={cvarName};value={value}");
                Core.Logger.LogInformation("{Admin} set {Cvar} to {Value}", adminName, cvarName, value);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Cvar command failed");
        }
    }
}

