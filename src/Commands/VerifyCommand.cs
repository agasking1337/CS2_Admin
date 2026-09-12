using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class VerifyCommand : CommandBase
{
    private readonly DiscordBotService _discord;

    public VerifyCommand(
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

            if (!HasPerm(context, Permissions.Verify))
            {
                Reply(context, "no_permission");
                return;
            }

            if (!_discord.Verify.IsEnabled)
            {
                Reply(context, "verify_disabled");
                return;
            }

            var steamId = context.Sender.SteamID;
            var existing = await _discord.Verify.GetLinkBySteamIdAsync(steamId);
            if (existing != null)
            {
                var discordLabel = string.IsNullOrWhiteSpace(existing.DiscordName)
                    ? existing.DiscordId.ToString()
                    : existing.DiscordName;
                Reply(context, "verify_already_linked", discordLabel);
                return;
            }

            var code = _discord.Verify.CreateCode(steamId);
            Reply(context, "verify_code", code, _discord.Verify.CodeExpiryMinutes);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Verify command failed");
        }
    }
}
