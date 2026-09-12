using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class UnverifyCommand : CommandBase
{
    private readonly DiscordBotService _discord;

    public UnverifyCommand(
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

            if (!HasPerm(context, Permissions.Unverify))
            {
                Reply(context, "no_permission");
                return;
            }

            if (!_discord.Verify.IsEnabled)
            {
                Reply(context, "verify_disabled");
                return;
            }

            if (!await _discord.Verify.UnlinkAsync(context.Sender.SteamID))
            {
                Reply(context, "verify_not_linked");
                return;
            }

            Reply(context, "verify_unlinked");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Unverify command failed");
        }
    }
}
