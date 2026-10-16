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

public class BurnCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _burnPlayers = new();

    public BurnCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Burn, Permissions.Burn, "burn_usage", _adminDbManager, "Burn",
            includeDeadPlayers: false,
            targetFilter: p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var durationSeconds = 8;
                var isInfinite = false;
                if (args.Length > 1 && int.TryParse(args[1], out var parsedDuration))
                {
                    if (parsedDuration == -1)
                        isInfinite = true;
                    else
                        durationSeconds = Math.Clamp(parsedDuration, 1, 60);
                }

                var damagePerTick = 5;
                if (args.Length > 2 && int.TryParse(args[2], out var parsedDamage))
                    damagePerTick = Math.Clamp(parsedDamage, 1, 100);

                var validCount = 0;
                foreach (var target in targets)
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true)
                        continue;

                    var pawn = liveTarget.PlayerPawn;
                    if (pawn?.IsValid != true || pawn.Health <= 0)
                        continue;

                    _burnPlayers.Add(liveTarget.PlayerID);
                    StartBurnEffect(liveTarget, isInfinite ? null : durationSeconds, damagePerTick);
                    validCount++;
                }

                if (validCount == 0) return;

                var durationLabel = isInfinite ? L("permanent") : durationSeconds.ToString();

                foreach (var burnTarget in targets)
                {
                    var liveBurnTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == burnTarget.SteamID);
                    if (liveBurnTarget?.IsValid != true) continue;
                    PlayerUtils.SendNotification(Core, liveBurnTarget, Messages,
                        $"{L("burn_center_html")}<br>{L("label_duration")}: <font color='#ffd700'>{durationLabel}s</font> | {L("label_damage_per_tick")}: <font color='#ff5733'>{damagePerTick}</font>",
                        $" \x02{L("prefix")}\x01 {L("burn_personal_chat", damagePerTick)}");
                }

                BroadcastNotification(adminName, "burn_notification", FormatTargetName(targets), durationLabel, damagePerTick);

                _ = AdminLogManager.AddLogAsync("burn", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={validCount};duration={(isInfinite ? "infinite" : durationSeconds.ToString())};dmg={damagePerTick}");
                Core.Logger.LogInformationIfEnabled("{Admin} set {Count} player(s) on fire", adminName, validCount);
            });
    }

    private void StartBurnEffect(IPlayer player, int? durationSeconds, int damagePerTick)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        var ticks = durationSeconds ?? 30;
        var interval = 1.0f;
        var tickCount = 0;

        void BurnTick()
        {
            if (tickCount >= ticks || !player.IsValid || pawn?.Health <= 0)
            {
                _burnPlayers.Remove(player.PlayerID);
                return;
            }

            if (pawn?.IsValid == true && pawn.Health > 0)
            {
                pawn.Health = Math.Max(pawn.Health - damagePerTick, 0);
                pawn.HealthUpdated();

                if (pawn.Health <= 0)
                {
                    pawn.CommitSuicide(false, false);
                    _burnPlayers.Remove(player.PlayerID);
                    return;
                }
            }

            tickCount++;
            Core.Scheduler.DelayBySeconds(interval, BurnTick);
        }

        Core.Scheduler.DelayBySeconds(interval, BurnTick);
    }
}

