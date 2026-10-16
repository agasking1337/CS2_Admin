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

public class GlowCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public GlowCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Glow, Permissions.Glow, "glow_usage", _adminDbManager, "Glow",
            includeDeadPlayers: false,
            minArgs: 2,
            targetFilter: p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var disableGlow = args[1].Equals("off", StringComparison.OrdinalIgnoreCase);
                var r = 255;
                var g = 255;
                var b = 255;
                var a = 180;

                if (!disableGlow)
                {
                    if (TryParseColorName(args[1], out var namedColor))
                    {
                        r = namedColor[0];
                        g = namedColor[1];
                        b = namedColor[2];
                    }
                    else if (args.Length >= 4 &&
                             int.TryParse(args[1], out var ri) &&
                             int.TryParse(args[2], out var gi) &&
                             int.TryParse(args[3], out var bi))
                    {
                        r = ri;
                        g = gi;
                        b = bi;
                    }
                    else
                    {
                        Reply(ctx, "glow_usage");
                        return;
                    }

                    if (args.Length > 4 && !int.TryParse(args[4], out a))
                    {
                        Reply(ctx, "glow_usage");
                        return;
                    }

                    r = Math.Clamp(r, 0, 255);
                    g = Math.Clamp(g, 0, 255);
                    b = Math.Clamp(b, 0, 255);
                    a = Math.Clamp(a, 0, 255);
                }

                var colorHex = disableGlow ? "#ffffff" : $"#{r:X2}{g:X2}{b:X2}";

                foreach (var target in targets)
                {
                    var targetSteamId = target.SteamID;
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                    if (liveTarget?.IsValid != true)
                        continue;

                    if (disableGlow)
                        ClearGlow(liveTarget);
                    else
                        ApplyGlow(liveTarget, r, g, b, a);
                }

                foreach (var gTarget in targets)
                {
                    var liveG = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == gTarget.SteamID);
                    if (liveG?.IsValid != true) continue;
                    if (disableGlow)
                    {
                        PlayerUtils.SendNotification(Core, liveG, Messages,
                            $"<font color='#ecf0f1'><b>{L("glow_off_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffd700'>{ResolveVisibleAdminName(liveG, adminName)}</font>",
                            $" \x02{L("prefix")}\x01 {L("glow_off_personal_chat", ResolveVisibleAdminName(liveG, adminName))}");
                    }
                    else
                    {
                        PlayerUtils.SendNotification(Core, liveG, Messages,
                            $"<font color='{colorHex}'><b>{L("glow_personal_html")}</b></font><br><br><font color='{colorHex}'>■</font> RGB: {r},{g},{b}<br>{L("label_by")}: <font color='#ffd700'>{ResolveVisibleAdminName(liveG, adminName)}</font>",
                            $" \x02{L("prefix")}\x01 {L("glow_personal_chat", ResolveVisibleAdminName(liveG, adminName), $"{r},{g},{b}")}");
                    }
                }

                if (disableGlow)
                {
                    BroadcastNotification(adminName, "glow_off_notification", FormatTargetName(targets));
                }
                else
                {
                    BroadcastNotification(adminName, "glow_notification", FormatTargetName(targets));
                }

                var details = disableGlow
                    ? $"targets={targets.Count};off=true"
                    : $"targets={targets.Count};rgba={r},{g},{b},{a}";

                _ = AdminLogManager.AddLogAsync("glow", adminName, ctx.Sender?.SteamID ?? 0, null, null, details);
                Core.Logger.LogInformation("{Admin} set glow for {Count} player(s)", adminName, targets.Count);
            });
    }

    private void ApplyGlow(IPlayer target, int r, int g, int b, int a)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        pawn.Render = new(r, g, b, 255);
        pawn.RenderUpdated();
    }

    private void ClearGlow(IPlayer target)
    {
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        pawn.Render = new(255, 255, 255, 255);
        pawn.RenderUpdated();
    }

    private static bool TryParseColorName(string input, out int[] rgb)
    {
        rgb = new int[3];
        switch (input.ToLowerInvariant())
        {
            case "red": rgb = new[] { 255, 0, 0 }; return true;
            case "green": rgb = new[] { 0, 255, 0 }; return true;
            case "blue": rgb = new[] { 0, 100, 255 }; return true;
            case "yellow": rgb = new[] { 255, 255, 0 }; return true;
            case "cyan": rgb = new[] { 0, 255, 255 }; return true;
            case "magenta": rgb = new[] { 255, 0, 255 }; return true;
            case "white": rgb = new[] { 255, 255, 255 }; return true;
            case "orange": rgb = new[] { 255, 140, 0 }; return true;
            case "purple": rgb = new[] { 160, 32, 240 }; return true;
            case "pink": rgb = new[] { 255, 105, 180 }; return true;
            case "lime": rgb = new[] { 0, 255, 128 }; return true;
            case "turquoise": rgb = new[] { 64, 224, 208 }; return true;
            default: return false;
        }
    }
}

