using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class CallAdminCommand : CommandBase
{
    private readonly DiscordBotService _discord;

    public CallAdminCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        DiscordBotService discord)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _discord = discord;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!context.IsSentByPlayer || context.Sender == null)
            {
                Reply(context, "player_only_command");
                return;
            }

            if (!HasPerm(context, Permissions.CallAdmin))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.CallAdmin);
            if (args.Length < 1)
            {
                Reply(context, "calladmin_usage");
                return;
            }

            var messageText = string.Join(" ", args);
            var playerName = context.Sender.Controller.PlayerName ?? L("unknown");
            var playerSteamId = context.Sender.SteamID;
            var serverId = ServerIdentity.GetServerId(Core);

            _ = _discord.SendCallAdminNotificationAsync(playerName, playerSteamId, messageText, serverId);
            _ = AdminLogManager.AddLogAsync("calladmin", playerName, playerSteamId, null, context.Sender.IPAddress, $"message={messageText};server={serverId}");

            Reply(context, "calladmin_sent");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "CallAdmin command failed");
        }
    }
}
