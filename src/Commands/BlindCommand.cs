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
using SwiftlyS2.Shared.ProtobufDefinitions;
using System.Drawing;

namespace CS2_Admin.Commands;

public class BlindCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public BlindCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Blind, Permissions.Blind, "blind_usage", _adminDbManager, "Blind",
            includeDeadPlayers: false,
            minArgs: 2,
            targetFilter: p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                if (!int.TryParse(args[1], out var parsedDuration))
                {
                    Reply(ctx, "blind_usage");
                    return;
                }

                var durationSeconds = Math.Clamp(parsedDuration, 1, 60);

                foreach (var target in targets)
                {
                    var targetSteamId = target.SteamID;

                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                    if (liveTarget?.IsValid != true)
                        continue;

                    ApplyBlindEffect(liveTarget, durationSeconds);

                    PlayerUtils.SendNotification(Core, liveTarget, Messages,
                        $"<font color='#2c3e50'><b>{L("blind_personal_html")}</b></font><br><br>{L("label_duration")}: <font color='#e74c3c'>{durationSeconds}s</font>",
                        $" \x02{L("prefix")}\x01 {L("blind_personal_chat", ResolveVisibleAdminName(liveTarget, ctx.Sender?.Controller.PlayerName ?? L("console_name")), durationSeconds)}");

                    Core.Scheduler.DelayBySeconds(durationSeconds, () =>
                    {
                        var sameTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                        if (sameTarget?.IsValid == true)
                            ClearBlindEffect(sameTarget);
                    });
                }

                BroadcastNotification(adminName, "blind_notification", FormatTargetName(targets), durationSeconds);

                _ = AdminLogManager.AddLogAsync("blind", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={durationSeconds}");
                Core.Logger.LogInformationIfEnabled("{Admin} blinded {Count} player(s) for {Duration}s", adminName, targets.Count, durationSeconds);
            });
    }

    private void ApplyBlindEffect(IPlayer target, float holdSeconds)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        try
        {
            using var netMessage = Core.NetMessage.Create<CUserMessageFade>();
            netMessage.Duration = Convert.ToUInt32(0.2f * 512); // fade in time
            netMessage.HoldTime = Convert.ToUInt32(holdSeconds * 512);
            netMessage.Flags = 0x0001 | 0x0010; // FADE_IN | PURGE

            var color = System.Drawing.Color.Black;
            netMessage.Color = color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);

            netMessage.Recipients.AddRecipient(target.PlayerID);
            netMessage.Send();
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "ApplyBlindEffect failed for {SteamId}", target.SteamID);
        }
    }

    private void ClearBlindEffect(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        try
        {
            using var netMessage = Core.NetMessage.Create<CUserMessageFade>();
            netMessage.Duration = Convert.ToUInt32(0.2f * 512);
            netMessage.HoldTime = 0;
            netMessage.Flags = 0x0001 | 0x0010; // FADE_IN | PURGE

            var color = System.Drawing.Color.FromArgb(0, 0, 0, 0); // Transparent
            netMessage.Color = color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);

            netMessage.Recipients.AddRecipient(target.PlayerID);
            netMessage.Send();
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "ClearBlindEffect failed for {SteamId}", target.SteamID);
        }
    }
}

