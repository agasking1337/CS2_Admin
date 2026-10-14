using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class MixTeamCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public MixTeamCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.MixTeam);

            if (!HasPerm(context, Permissions.MixTeam))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length > 0)
            {
                Reply(context, "mixteam_usage");
                return;
            }

            var players = Core.PlayerManager
                .GetAllPlayers()
                .Where(p => p.IsValid && !p.IsFakeClient && p.Controller?.TeamNum > 1)
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            if (players.Count < 2)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            var halfCount = players.Count / 2;
            var startT = Random.Shared.Next(2) == 0;
            var moved = 0;

            for (var i = 0; i < players.Count; i++)
            {
                var target = players[i];
                var team = i < halfCount
                    ? (startT ? Team.T : Team.CT)
                    : (startT ? Team.CT : Team.T);

                    if (target.IsValid)
                    {
                        target.ChangeTeam(team);
                    }
                moved++;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var prefix = L("prefix");

            foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                player.SendChat($" \x02{prefix}\x01 {L("mixteam_notification", visibleAdmin, moved)}");
            }

            _ = AdminLogManager.AddLogAsync("mixteam", adminName, context.Sender?.SteamID ?? 0, null, null, $"players={moved}");
            Core.Logger.LogInformationIfEnabled("{Admin} mixed teams for {Count} player(s)", adminName, moved);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "MixTeam command failed");
        }
    }
}
