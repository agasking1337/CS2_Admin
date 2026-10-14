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
using SwiftlyS2.Shared.Sounds;
using System.Drawing;

namespace CS2_Admin.Commands;

public class BeaconCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _beaconPlayers = new();

    public BeaconCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Beacon, Permissions.Beacon, "beacon_usage", _adminDbManager, "Beacon",
            includeDeadPlayers: false,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var durationSeconds = 20;
                var stopRequested = args.Length > 1 && (args[1].Equals("off", StringComparison.OrdinalIgnoreCase) || args[1] == "0");

                if (args.Length > 1 && !stopRequested && int.TryParse(args[1], out var parsedDuration))
                    durationSeconds = Math.Clamp(parsedDuration, 1, 120);

                var started = 0;
                var stopped = 0;

                foreach (var target in targets)
                {
                    if (stopRequested)
                    {
                        if (_beaconPlayers.Remove(target.PlayerID))
                            stopped++;
                        continue;
                    }

                    _beaconPlayers.Add(target.PlayerID);
                    StartBeaconEffect(target, durationSeconds);
                    started++;
                }

                if (stopRequested)
                {
                    ReplyRaw(ctx, L("beacon_stopped", stopped));
                    _ = AdminLogManager.AddLogAsync("beacon", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"mode=off;targets={stopped}");
                    return;
                }

                BroadcastNotification(adminName, "beacon_started", FormatTargetName(targets), durationSeconds);

                foreach (var bTarget in targets)
                {
                    var liveBTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == bTarget.SteamID);
                    if (liveBTarget?.IsValid != true) continue;
                    PlayerUtils.SendNotification(Core, liveBTarget, Messages,
                        $"<font color='#e74c3c'><b>{L("beacon_personal_html")}</b></font><br><br>{L("label_duration")}: <font color='#ffd700'>{durationSeconds}s</font><br>{L("label_by")}: <font color='#ffd700'>{ResolveVisibleAdminName(liveBTarget, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("beacon_personal_chat", ResolveVisibleAdminName(liveBTarget, adminName), durationSeconds)}");
                }

                _ = AdminLogManager.AddLogAsync("beacon", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"mode=on;targets={started};duration={durationSeconds}");
                Core.Logger.LogInformation("{Admin} started beacon for {Count} player(s)", adminName, started);
            });
    }

    private const int BeaconSegments = 16;
    private const int BeaconLayers = 2;
    private const float BeaconBaseRadius = 84.0f;
    private const float BeaconRadiusStep = 56.0f;
    private const float BeaconBeamLife = 0.95f;
    private const float BeaconLayerLifeStep = 0.15f;
    private const float BeaconBeamWidth = 3.5f;
    private const float BeaconHeightOffset = 6.0f;
    private const string BeaconSound = "UIPanorama.popup_accept_match_beep";
    private static readonly QAngle RotationZero = new QAngle(0, 0, 0);
    private static readonly Vector VectorZero = new Vector(0, 0, 0);

    private static (float Cos, float Sin)[] BuildBeaconUnitCircle()
    {
        var points = new (float Cos, float Sin)[BeaconSegments];
        var step = (2.0 * Math.PI) / BeaconSegments;
        for (var i = 0; i < BeaconSegments; i++)
        {
            var angle = i * step;
            points[i] = ((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
        return points;
    }

    private void StartBeaconEffect(IPlayer player, int durationSeconds)
    {
        var playerId = player.PlayerID;
        var elapsed = 0;
        var interval = 1.0f;
        var unitCircle = BuildBeaconUnitCircle();

        void BeaconTick()
        {
            if (elapsed >= durationSeconds || !_beaconPlayers.Contains(playerId))
            {
                _beaconPlayers.Remove(playerId);
                return;
            }

            if (player.IsValid)
            {
                var pawn = player.PlayerPawn;
                if (pawn?.IsValid == true && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    var originNullable = pawn.AbsOrigin;
                    if (originNullable != null)
                    {
                        var origin = originNullable.Value;
                        var color = elapsed % 2 == 0 ? System.Drawing.Color.Yellow : System.Drawing.Color.White;
                        var teamNum = player.Controller?.TeamNum;
                        if (teamNum == (byte)Team.T) color = elapsed % 2 == 0 ? System.Drawing.Color.Red : System.Drawing.Color.Orange;
                        else if (teamNum == (byte)Team.CT) color = elapsed % 2 == 0 ? System.Drawing.Color.Blue : System.Drawing.Color.Cyan;

                        var center = new Vector(origin.X, origin.Y, origin.Z + BeaconHeightOffset);

                        for (var layer = 0; layer < BeaconLayers; layer++)
                        {
                            var radius = BeaconBaseRadius + (layer * BeaconRadiusStep);
                            var life = BeaconBeamLife - (layer * BeaconLayerLifeStep);
                            
                            var previousUnit = unitCircle[BeaconSegments - 1];
                            var previous = new Vector(center.X + (radius * previousUnit.Cos), center.Y + (radius * previousUnit.Sin), center.Z);

                            for (var i = 0; i < BeaconSegments; i++)
                            {
                                var unit = unitCircle[i];
                                var current = new Vector(center.X + (radius * unit.Cos), center.Y + (radius * unit.Sin), center.Z);
                                DrawLaserBetween(previous, current, color, life, BeaconBeamWidth);
                                previous = current;
                            }
                        }

                        using var soundEvent = new SoundEvent(BeaconSound);
                        soundEvent.SourceEntityIndex = -1;
                        soundEvent.Recipients.AddAllPlayers();
                        soundEvent.Emit();
                    }
                }
            }

            elapsed++;
            Core.Scheduler.DelayBySeconds(interval, BeaconTick);
        }

        BeaconTick();
    }

    private void DrawLaserBetween(Vector startPos, Vector endPos, System.Drawing.Color color, float life, float width)
    {
        var beam = Core.EntitySystem.CreateEntityByDesignerName<CBeam>("beam");
        if (beam == null) return;

        beam.Render = new SwiftlyS2.Shared.Natives.Color((int)color.R, (int)color.G, (int)color.B, 255);
        beam.Width = width;
        beam.Teleport(startPos, RotationZero, VectorZero);
        beam.EndPos.X = endPos.X;
        beam.EndPos.Y = endPos.Y;
        beam.EndPos.Z = endPos.Z;
        beam.DispatchSpawn();

        Core.Scheduler.DelayBySeconds(life, () =>
        {
            if (beam != null && beam.IsValid)
            {
                beam.Despawn();
            }
        });
    }
}

