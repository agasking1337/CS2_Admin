using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace CS2_Admin.Commands;

public class BuryCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public BuryCommand(
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

    public override void Execute(ICommandContext context)
    {
        RunTargetedFunCommand(context, CommandsConfig.Bury, Permissions.Bury, "bury_usage", _adminDbManager, "Bury",
            includeDeadPlayers: false,
            targetFilter: p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                float depth = 30.0f; // Distance to push down

                foreach (var target in targets)
                {
                    var targetSteamId = target.SteamID;
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                    var pawn = liveTarget?.PlayerPawn;

                    if (pawn?.IsValid == true && pawn.Health > 0)
                    {
                        var origin = pawn.AbsOrigin;
                        if (origin != null)
                        {
                            var newPos = new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z - depth);
                            pawn.Teleport(newPos, pawn.AbsRotation, pawn.AbsVelocity);
                        }
                    }
                }

                BroadcastNotification(adminName, "bury_notification", FormatTargetName(targets));

                _ = AdminLogManager.AddLogAsync("bury", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count}");
                Core.Logger.LogInformationIfEnabled("{Admin} buried {Count} player(s)", adminName, targets.Count);
            });
    }
}
