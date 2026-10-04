using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class HideCommand : CommandBase
{
    private readonly AdminVisibilityService _visibilityService;

    public HideCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminVisibilityService visibilityService)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _visibilityService = visibilityService;
    }

    public override void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.Hide))
            {
                Reply(context, "no_permission");
                return;
            }

            if (!context.IsSentByPlayer || context.Sender == null)
            {
                Reply(context, "player_only_command");
                return;
            }

            var player = context.Sender;
            if (_visibilityService.IsHidden(player.SteamID))
            {
                _visibilityService.Show(player);
                Reply(context, "hide_disabled");
                return;
            }

            if (player.IsFakeClient)
                return;

            var previousTeam = player.Controller.Team;
            if (_visibilityService.Hide(player, previousTeam))
                Reply(context, "hide_enabled");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Hide command failed");
        }
    }
}
