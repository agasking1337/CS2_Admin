using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Commands;

public class HpCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public HpCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Hp);

            if (!HasPerm(context, Permissions.Hp))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2 || !int.TryParse(args[1], out var health))
            {
                Reply(context, "hp_usage");
                return;
            }

            health = Math.Clamp(health, 1, 999);

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var canTarget = await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true);
            if (!canTarget)
                return;

                // await sonrası ana thread'de değiliz; pawn.Health/ArmorValue gibi native
                // çağrılar SADECE ana thread'den yapılabilir.
                Core.Scheduler.NextTick(() =>
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true) return;

                    var pawn = liveTarget.PlayerPawn;
                    if (pawn?.IsValid != true)
                    {
                        Reply(context, "player_not_found");
                        return;
                    }

                    pawn.Health = health;
                    pawn.HealthUpdated();

                    if (args.Length > 2 && int.TryParse(args[2], out var armor))
                    {
                        armor = Math.Clamp(armor, 0, 100);
                        pawn.ArmorValue = armor;
                    }

                    var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
                    var targetName = liveTarget.Controller.PlayerName;

                    BroadcastNotification(adminName, "hp_notification", targetName, health);

                    PlayerUtils.SendNotification(Core, liveTarget, Messages,
                        $"<font color='#00ff00'><b>{L("hp_personal_html", health)}</b></font>",
                        $" \x02{L("prefix")}\x01 {L("hp_personal_chat", health)}");

                    _ = AdminLogManager.AddLogAsync("hp", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"health={health}", targetName);
                    Core.Logger.LogInformation("{Admin} set health of {Target} to {Health}", adminName, targetName, health);
                });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Hp command failed");
        }
    }
}
