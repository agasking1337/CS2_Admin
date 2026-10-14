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
using System.Drawing;

namespace CS2_Admin.Commands;

public class ResizeCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public ResizeCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Resize, Permissions.Resize, "resize_usage", _adminDbManager, "Resize",
            includeDeadPlayers: false,
            minArgs: 2,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                if (!float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                {
                    Reply(ctx, "resize_usage");
                    return;
                }

                scale = Math.Clamp(scale, 0.2f, 3.0f);

                var applied = 0;
                foreach (var target in targets)
                {
                    if (TrySetPlayerScale(target, scale))
                        applied++;
                }

                if (applied == 0)
                {
                    Reply(ctx, "resize_not_supported");
                    return;
                }

                string targetLabel = FormatTargetName(targets);
                var scaleStr = scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

                foreach (var rTarget in targets)
                {
                    var liveR = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == rTarget.SteamID);
                    if (liveR?.IsValid != true) continue;
                    PlayerUtils.SendNotification(Core, liveR, Messages,
                        $"<font color='#9b59b6'><b>{L("resize_personal_html")}</b></font><br><br>{L("label_value")}: <font color='#ffd700'>{scaleStr}x</font><br>{L("label_by")}: <font color='#ffd700'>{ResolveVisibleAdminName(liveR, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("resize_personal_chat", ResolveVisibleAdminName(liveR, adminName), scaleStr)}");
                }

                BroadcastNotification(adminName, "resize_notification", targetLabel, scaleStr);

                _ = AdminLogManager.AddLogAsync("resize", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={applied};scale={scaleStr}");
                Core.Logger.LogInformationIfEnabled("{Admin} resized {Count} player(s) to {Scale}", adminName, applied, scale);
            });
    }

    private bool TrySetPlayerScale(IPlayer player, float scale)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return false;

        try
        {
            pawn.SetScale(scale);
            return true;
        }
        catch
        {
            try
            {
                var prop = pawn.GetType().GetProperty("ModelScale");
                if (prop != null)
                {
                    prop.SetValue(pawn, scale);
                    pawn.HealthUpdated();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Core.Logger.LogErrorIfEnabled(ex, "TrySetPlayerScale reflection fallback failed for {SteamId}", player.SteamID);
            }
        }

        return false;
    }
}

