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

public class BringCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public BringCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Bring);

            if (!HasPerm(context, Permissions.Bring))
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
                Reply(context, "bring_usage");
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
                Reply(context, "cannot_target_self_bring");
                return;
            }

            var targetPawn = target.PlayerPawn;
            var adminPawn = admin.PlayerPawn;
            if (targetPawn?.IsValid != true || adminPawn?.IsValid != true)
            {
                Reply(context, "both_must_be_alive");
                return;
            }

            var adminPos = adminPawn.AbsOrigin ?? new Vector(0, 0, 0);
            var adminRot = adminPawn.AbsRotation ?? new QAngle(0, 0, 0);
            var yawRad = adminRot.Y * (MathF.PI / 180f);
            const float bringOffset = 70f;
            var destPos = new Vector(
                adminPos.X + MathF.Cos(yawRad) * bringOffset,
                adminPos.Y + MathF.Sin(yawRad) * bringOffset,
                adminPos.Z + 2f);

            var destRot = targetPawn.AbsRotation;
            targetPawn.Teleport(destPos, destRot, new Vector(0, 0, 0));

            var adminName = admin.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;

            target.SendChat($" \x02{L("prefix")}\x01 {L("bring_success", ResolveVisibleAdminName(target, adminName))}");

            BroadcastNotification(adminName, "bring_notification", targetName);

            _ = AdminLogManager.AddLogAsync("bring", adminName, admin.SteamID, target.SteamID, target.IPAddress, "", target.Controller.PlayerName);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Bring command failed");
        }
    }
}

