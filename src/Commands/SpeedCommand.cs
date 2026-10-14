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

public class SpeedCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly Dictionary<int, float> _speedOverrides = new();

    public SpeedCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Speed, Permissions.Speed, "speed_usage", _adminDbManager, "Speed",
            includeDeadPlayers: false,
            minArgs: 2,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                if (!float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var multiplier))
                {
                    Reply(ctx, "speed_usage");
                    return;
                }

                multiplier = Math.Clamp(multiplier, 0.1f, 10.0f);

                var applied = 0;
                foreach (var target in targets)
                {
                    var pawn = target.PlayerPawn;
                    if (pawn?.IsValid != true)
                        continue;

                    var playerId = target.PlayerID;

                    if (Math.Abs(multiplier - 1.0f) < 0.01f)
                    {
                        _speedOverrides.Remove(playerId);
                    }
                    else
                    {
                        _speedOverrides[playerId] = multiplier;
                        StartSpeedEnforcer(target.SteamID, playerId, multiplier);
                    }

                    try
                    {
                        pawn.VelocityModifier = multiplier;
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.LogErrorIfEnabled(ex, "VelocityModifier set failed for {SteamId}", target.SteamID);
                    }

                    applied++;

                    PlayerUtils.SendNotification(Core, target, Messages,
                        $"<font color='#00ff88'><b>{L("speed")}</b></font><br><br>{L("label_value")}: <font color='#00ff88'>{multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}x</font>",
                        $" \x02{L("prefix")}\x01 {L("speed_personal_chat", multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))}");
                }

                if (applied == 0)
                {
                    Reply(ctx, "no_valid_targets");
                    return;
                }

                string targetLabel = FormatTargetName(targets);
                BroadcastNotification(adminName, "speed_notification", targetLabel, multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

                _ = AdminLogManager.AddLogAsync("speed", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={applied};multiplier={multiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
                Core.Logger.LogInformationIfEnabled("{Admin} set speed of {Count} player(s) to {Multiplier}", adminName, applied, multiplier);
            });
    }

    private void StartSpeedEnforcer(ulong steamId, int playerId, float multiplier)
    {
        void Enforce()
        {
            // Stop if the override was removed or changed to a different value (another call superseded us)
            if (!_speedOverrides.TryGetValue(playerId, out var currentMultiplier) || Math.Abs(currentMultiplier - multiplier) > 0.001f)
                return;

            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player?.PlayerPawn?.IsValid != true)
            {
                _speedOverrides.Remove(playerId);
                return;
            }

            try
            {
                player.PlayerPawn.VelocityModifier = multiplier;
            }
            catch (Exception ex)
            {
                Core.Logger.LogErrorIfEnabled(ex, "VelocityModifier enforcer failed for {SteamId}", steamId);
            }

            Core.Scheduler.NextTick(Enforce);
        }

        Core.Scheduler.NextTick(Enforce);
    }
}
