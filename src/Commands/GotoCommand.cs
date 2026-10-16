using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

using CS2_Admin.Services;
using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class GotoCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public GotoCommand(
        ISwiftlyCore core,
        AdminDbManager adminDbManager,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService) : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Goto);

            if (!HasPerm(context, Permissions.Goto))
            {
                Reply(context, "no_permission");
                return;
            }

            if (!context.IsSentByPlayer || context.Sender == null)
            {
                Reply(context, "player_only_command");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "goto_usage");
                return;
            }

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var admin = context.Sender;

            if (admin.PlayerID == target.PlayerID)
            {
                Reply(context, "cannot_target_self_goto");
                return;
            }

            var adminPawn = admin.PlayerPawn;
            var targetPawn = target.PlayerPawn;

            if (adminPawn?.IsValid != true || targetPawn?.IsValid != true)
            {
                Reply(context, "both_must_be_alive");
                return;
            }

            var targetPos = targetPawn.AbsOrigin ?? adminPawn.AbsOrigin ?? new Vector(0, 0, 0);
            var adminPos = adminPawn.AbsOrigin ?? targetPos;

            var dx = targetPos.X - adminPos.X;
            var dy = targetPos.Y - adminPos.Y;

            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 0.001f)
            {
                dx = 1f;
                dy = 0f;
                distance = 1f;
            }

            dx /= distance;
            dy /= distance;

            const float offset = 50f;

            var destX = targetPos.X - dx * offset;
            var destY = targetPos.Y - dy * offset;
            var destZ = targetPos.Z;

            var destPos = new Vector(destX, destY, destZ);

            var lookDx = targetPos.X - destX;
            var lookDy = targetPos.Y - destY;
            var yawRad = MathF.Atan2(lookDy, lookDx);
            var yawDeg = yawRad * (180f / MathF.PI);

            var destRot = new QAngle(0, yawDeg, 0);

            var velocity = adminPawn.AbsVelocity;
            adminPawn.Teleport(destPos, destRot, velocity);

            var adminName = admin.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;

            admin.SendChat($" \x02{L("prefix")}\x01 {L("goto_success", targetName)}");

            BroadcastNotification(adminName, "goto_notification", targetName);

            _ = AdminLogManager.AddLogAsync("goto", adminName, admin.SteamID, target.SteamID, target.IPAddress, "", target.Controller.PlayerName);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Goto command failed");
        }
    }
}

