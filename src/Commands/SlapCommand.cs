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

public class SlapCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public SlapCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Slap, Permissions.Slap, "slap_usage", _adminDbManager, "Slap",
            includeDeadPlayers: false,
            targetFilter: p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var damage = 0;
                if (args.Length > 1 && int.TryParse(args[1], out var parsedDamage))
                {
                    damage = Math.Clamp(parsedDamage, 0, 100);
                }

                var prefix = L("prefix");

                foreach (var target in targets)
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true)
                        continue;

                    var livePawn = liveTarget.PlayerPawn;
                    if (livePawn?.IsValid != true || livePawn.Health <= 0)
                        continue;

                    ApplySlap(livePawn, damage);

                    if (livePawn.Health <= 0)
                        continue;

                    PlayerUtils.SendNotification(Core, liveTarget, Messages,
                        $"<font color='#ffcc00'><b>{L("slapped_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(liveTarget, adminName)}</font><br>{L("label_damage")}: <font color='#ffffff'>{damage}</font>",
                        $" \x02{prefix}\x01 {L("slapped_personal_chat", ResolveVisibleAdminName(liveTarget, adminName), damage)}");

                    var targetName = liveTarget.Controller.PlayerName ?? L("unknown");
                    foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                        player.SendChat($" \x02{prefix}\x01 {L("slapped_notification", visibleAdmin, targetName, damage)}");
                    }

                    _ = AdminLogManager.AddLogAsync("slap", adminName, ctx.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"damage={damage}", liveTarget.Controller.PlayerName);
                    Core.Logger.LogInformation("[CS2_Admin] {Admin} slapped {Target} for {Damage} damage", adminName, targetName, damage);
                }
            });
    }

    private static void ApplySlap(CCSPlayerPawn pawn, int damage)
    {
        if (damage > 0)
        {
            pawn.Health = Math.Max(pawn.Health - damage, 0);
            pawn.HealthUpdated();
        }

        if (pawn.Health == 0)
        {
            pawn.CommitSuicide(false, false);
            return;
        }

        var velocity = new Vector(
            (float)Random.Shared.NextInt64(50, 230) * (Random.Shared.NextDouble() < 0.5 ? -1f : 1f),
            (float)Random.Shared.NextInt64(50, 230) * (Random.Shared.NextDouble() < 0.5 ? -1f : 1f),
            Random.Shared.NextInt64(100, 300));

        pawn.Teleport(null, null, velocity);
    }
}

