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

public class RgbCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _rgbPlayers = new();

    public RgbCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Rgb, Permissions.Rgb, "rgb_usage", _adminDbManager, "Rgb",
            includeDeadPlayers: false,
            targetFilter: p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var durationSeconds = 30;
                var stopRequested = args.Length > 1 && (args[1].Equals("off", StringComparison.OrdinalIgnoreCase) || args[1] == "0");

                if (args.Length > 1 && !stopRequested && int.TryParse(args[1], out var parsedDuration))
                    durationSeconds = Math.Clamp(parsedDuration, 1, 300);

                var started = 0;
                var stopped = 0;

                foreach (var target in targets)
                {
                    if (stopRequested)
                    {
                        if (_rgbPlayers.Remove(target.PlayerID))
                        {
                            StopRgbEffect(target);
                            stopped++;
                        }
                        continue;
                    }

                    _rgbPlayers.Add(target.PlayerID);
                    StartRgbEffect(target, durationSeconds);
                    started++;
                }

                if (stopRequested)
                {
                    ReplyRaw(ctx, L("rgb_stopped", stopped));
                    _ = AdminLogManager.AddLogAsync("rgboff", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={stopped}");
                    return;
                }

                BroadcastNotification(adminName, "rgb_started", FormatTargetName(targets), durationSeconds);
                _ = AdminLogManager.AddLogAsync("rgbon", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={started};duration={durationSeconds}");
                Core.Logger.LogInformation("{Admin} started RGB glow for {Count} player(s)", adminName, started);
            });
    }

    private void StartRgbEffect(IPlayer player, int durationSeconds)
    {
        var playerId = player.PlayerID;
        var elapsed = 0;
        var interval = 0.25f;

        void RgbTick()
        {
            if (elapsed >= durationSeconds || !_rgbPlayers.Contains(playerId))
            {
                _rgbPlayers.Remove(playerId);
                if (player.IsValid)
                    StopRgbEffect(player);
                return;
            }

            if (player.IsValid)
            {
                var pawn = player.PlayerPawn;
                if (pawn?.IsValid == true)
                {
                    var hue = (elapsed * 30) % 360;
                    var (r, g, b) = HsvToRgb(hue, 1f, 1f);
                    pawn.Render = new(r, g, b, 255);
                    pawn.RenderUpdated();
                }
            }

            elapsed++;
            Core.Scheduler.DelayBySeconds(interval, RgbTick);
        }

        RgbTick();
    }

    private void StopRgbEffect(IPlayer player)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        pawn.Render = new(255, 255, 255, 255);
        pawn.RenderUpdated();
    }

    private static (int R, int G, int B) HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        var m = v - c;
        float rf = 0, gf = 0, bf = 0;

        if (h < 60) { rf = c; gf = x; bf = 0; }
        else if (h < 120) { rf = x; gf = c; bf = 0; }
        else if (h < 180) { rf = 0; gf = c; bf = x; }
        else if (h < 240) { rf = 0; gf = x; bf = c; }
        else if (h < 300) { rf = x; gf = 0; bf = c; }
        else { rf = c; gf = 0; bf = x; }

        return (
            (int)Math.Round((rf + m) * 255f),
            (int)Math.Round((gf + m) * 255f),
            (int)Math.Round((bf + m) * 255f)
        );
    }
}
