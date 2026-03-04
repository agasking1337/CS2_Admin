using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Misc;
using System.Text.Json;
using System.Globalization;
using System.Reflection;

namespace CS2_Admin.Commands;

public class PlayerCommands
{
    private readonly ISwiftlyCore _core;
    private readonly DiscordWebhook _discord;
    private readonly PermissionsConfig _permissions;
    private readonly CommandsConfig _commands;
    private readonly TagsConfig _tags;
    private readonly MessagesConfig _messagesConfig;
    private readonly BanManager _banManager;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly MultiServerConfig _multiServerConfig;
    private readonly HashSet<int> _noclipPlayers = new();
    private readonly HashSet<int> _frozenPlayers = new();
    private readonly HashSet<int> _beaconPlayers = new();
    private readonly HashSet<int> _burnPlayers = new();
    private readonly HashSet<int> _drugPlayers = new();
    private readonly Dictionary<int, QAngle> _drugOriginalRotations = new();

    private record PlayerListEntry(
        int Id,
        string Name,
        string SteamId,
        int Team,
        string TeamName,
        int Score,
        int Ping,
        bool IsAlive,
        string Ip,
        string Tag
    );

    public PlayerCommands(
        ISwiftlyCore core,
        DiscordWebhook discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messagesConfig,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        MultiServerConfig multiServerConfig)
    {
        _core = core;
        _discord = discord;
        _permissions = permissions;
        _commands = commands;
        _tags = tags;
        _messagesConfig = messagesConfig;
        _banManager = banManager;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _adminLogManager = adminLogManager;
        _multiServerConfig = multiServerConfig;
    }

    public void OnKickCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Kick);

        if (!HasPermission(context, _permissions.Kick))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["kick_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target, allowSelf: true))
        {
            return;
        }

        string reason = args.Length > 1 
            ? string.Join(" ", args.Skip(1)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        // Broadcast to all players
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["kicked_notification", adminName, targetName, reason]}");
        }
        
        // Personal message to target
        PlayerUtils.SendNotification(target, _messagesConfig,
            PluginLocalizer.Get(_core)["kicked_personal_html", reason],
            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["kicked_personal_chat", reason]}");

        // Kick after short delay to show message
        var targetSteamId = target.SteamID;
        _core.Scheduler.DelayBySeconds(2f, () =>
        {
            var playerToKick = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            playerToKick?.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
        });

        _ = _discord.SendKickNotificationAsync(adminName, targetName, reason);
        _ = _adminLogManager.AddLogAsync("kick", adminName, context.Sender?.SteamID ?? 0, targetSteamId, target.IPAddress, $"reason={reason}", target.Controller.PlayerName);

        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} kicked {Target}. Reason: {Reason}", 
            adminName, targetName, reason);
    }

    public void OnSlapCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Slap);

        if (!HasPermission(context, _permissions.Slap))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slap_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var damage = 5;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDamage))
        {
            damage = Math.Clamp(parsedDamage, 1, 100);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var target in targets)
        {
            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                if (liveTarget?.IsValid != true)
                {
                    return;
                }

                var livePawn = liveTarget.PlayerPawn;
                if (livePawn?.IsValid != true || livePawn.Health <= 0)
                {
                    return;
                }

                var currentHealth = livePawn.Health;
                var expectedHealth = Math.Max(currentHealth - damage, 0);

                if (expectedHealth <= 0)
                {
                    if (livePawn.Health > 0)
                    {
                        livePawn.CommitSuicide(false, true);
                    }

                    return;
                }

                if (livePawn.Health > expectedHealth)
                {
                    livePawn.Health = expectedHealth;
                    livePawn.HealthUpdated();
                }

                // Slap feedback: immediate knockback scales with damage.
                var currentVelocity = livePawn.AbsVelocity;
                var verticalBoost = Math.Clamp(140f + (damage * 4f), 180f, 720f);
                var horizontalBoost = Math.Clamp(20f + (damage * 1.8f), 30f, 240f);
                var randomX = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * horizontalBoost;
                var randomY = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * horizontalBoost;
                livePawn.AbsVelocity = new Vector(
                    currentVelocity.X + randomX,
                    currentVelocity.Y + randomY,
                    MathF.Max(currentVelocity.Z, verticalBoost));

                PlayerUtils.SendNotification(
                    liveTarget,
                    _messagesConfig,
                    PluginLocalizer.Get(_core)["slapped_personal_html", adminName, damage],
                    $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slapped_personal_chat", adminName, damage]}");

                var targetName = liveTarget.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
                foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                {
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slapped_notification", adminName, targetName, damage]}");
                }

                _ = _adminLogManager.AddLogAsync("slap", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"damage={damage}", liveTarget.Controller.PlayerName);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} slapped {Target} for {Damage} damage", adminName, targetName, damage);
            });
        }
    }

    public void OnSlayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Slay);

        if (!HasPermission(context, _permissions.Slay))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slay_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        foreach (var target in targets)
        {
            if (target.PlayerPawn?.IsValid == true)
            {
                target.PlayerPawn.CommitSuicide(false, true);
            }
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["slayed_personal_html", adminName],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slayed_personal_chat", adminName]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slayed_notification_single", adminName, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["slayed_notification_multiple", adminName, targets.Count]}");
            }
        }

        var targetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("slay", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targetSteamIds};count={targets.Count}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} slayed {Count} player(s)", adminName, targets.Count);
    }

    public void OnRespawnCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Respawn);

        if (!HasPermission(context, _permissions.Respawn))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawn_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        foreach (var target in targets)
        {
            if (target.Controller.TeamNum >= 2) // T or CT
            {
                target.Respawn();
            }
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["respawned_personal_html", adminName],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawned_personal_chat", adminName]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawned_notification_single", adminName, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["respawned_notification_multiple", adminName, targets.Count]}");
            }
        }

        var targetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("respawn", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targetSteamIds};count={targets.Count}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} respawned {Count} player(s)", adminName, targets.Count);
    }

    public void OnTeamCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.ChangeTeam);

        if (!HasPermission(context, _permissions.ChangeTeam))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["team_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target, allowSelf: true))
        {
            return;
        }

        var team = PlayerUtils.ParseTeam(args[1]);
        if (team == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_team"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;
        var teamName = PlayerUtils.GetTeamName((int)team.Value, PluginLocalizer.Get(_core));

        target.ChangeTeam(team.Value);
        
        // Personal message to target
        PlayerUtils.SendNotification(target, _messagesConfig,
            PluginLocalizer.Get(_core)["team_changed_personal_html", teamName, adminName],
            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["team_changed_personal_chat", teamName, adminName]}");

        // Broadcast to all players
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["team_changed_notification", adminName, targetName, teamName]}");
        }

        _ = _adminLogManager.AddLogAsync("team", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"team={teamName}", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} moved {Target} to {Team}", adminName, targetName, teamName);
    }

    public void OnGotoCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Goto);

        if (!HasPermission(context, _permissions.Goto))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (!context.IsSentByPlayer || context.Sender == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_only_command"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["goto_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target))
        {
            return;
        }

        var admin = context.Sender;

        if (admin.PlayerID == target.PlayerID)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self_goto"]}");
            return;
        }

        var adminPawn = admin.PlayerPawn;
        var targetPawn = target.PlayerPawn;

        if (adminPawn?.IsValid != true || targetPawn?.IsValid != true)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["both_must_be_alive"]}");
            return;
        }

        // Teleport admin near the target, facing them, to avoid getting stuck inside each other
        var targetPos = targetPawn.AbsOrigin ?? adminPawn.AbsOrigin ?? new Vector(0, 0, 0);
        var adminPos = adminPawn.AbsOrigin ?? targetPos;

        var dx = targetPos.X - adminPos.X;
        var dy = targetPos.Y - adminPos.Y;

        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 0.001f)
        {
            // If we're already very close, just pick an arbitrary horizontal direction
            dx = 1f;
            dy = 0f;
            distance = 1f;
        }

        dx /= distance;
        dy /= distance;

        const float offset = 50f; // units away from the target

        var destX = targetPos.X - dx * offset;
        var destY = targetPos.Y - dy * offset;
        var destZ = targetPos.Z;

        var destPos = new Vector(destX, destY, destZ);

        // Calculate yaw so the admin looks at the target
        var lookDx = targetPos.X - destX;
        var lookDy = targetPos.Y - destY;
        var yawRad = MathF.Atan2(lookDy, lookDx);
        var yawDeg = yawRad * (180f / MathF.PI);

        var destRot = new QAngle(0, yawDeg, 0);

        var velocity = adminPawn.AbsVelocity;
        adminPawn.Teleport(destPos, destRot, velocity);

        var adminName = admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["goto_success", targetName]}");

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["goto_notification", adminName, targetName]}");
        }

        _ = _adminLogManager.AddLogAsync("goto", adminName, admin.SteamID, target.SteamID, target.IPAddress, "", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} teleported to {Target}", adminName, targetName);
    }

    public void OnBringCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Bring);

        if (!HasPermission(context, _permissions.Bring))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (!context.IsSentByPlayer || context.Sender == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_only_command"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["bring_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target))
        {
            return;
        }

        var admin = context.Sender;

        if (admin.PlayerID == target.PlayerID)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self_bring"]}");
            return;
        }

        var targetPawn = target.PlayerPawn;
        var adminPawn = admin.PlayerPawn;
        if (targetPawn?.IsValid != true || adminPawn?.IsValid != true)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["both_must_be_alive"]}");
            return;
        }

        // Bring target to a stable point in front of admin.
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

        var adminName = admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        target.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["bring_success", adminName]}");

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["bring_notification", adminName, targetName]}");
        }

        _ = _adminLogManager.AddLogAsync("bring", adminName, admin.SteamID, target.SteamID, target.IPAddress, "", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} brought {Target}", adminName, targetName);
    }

    private Vector? GetAimPosition(IPlayer player)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null)
            return null;

        var eyePos = pawn.EyePosition;
        if (!eyePos.HasValue)
            return null;

        pawn.EyeAngles.ToDirectionVectors(out var forward, out _, out _);

        var startPos = new Vector(eyePos.Value.X, eyePos.Value.Y, eyePos.Value.Z);
        var endPos = startPos + forward * 8192;

        var trace = new CGameTrace();
        _core.Trace.SimpleTrace(
            startPos,
            endPos,
            RayType_t.RAY_TYPE_LINE,
            RnQueryObjectSet.Static | RnQueryObjectSet.Dynamic,
            MaskTrace.Solid | MaskTrace.Player,
            MaskTrace.Empty,
            MaskTrace.Empty,
            CollisionGroup.Player,
            ref trace,
            pawn
        );

        if (trace.Fraction < 1.0f)
        {
            // Offset the hit position back along the trace direction to avoid spawning inside walls
            var hitPos = trace.EndPos;
            var traceDir = endPos - startPos;
            var traceDirLen = MathF.Sqrt(traceDir.X * traceDir.X + traceDir.Y * traceDir.Y + traceDir.Z * traceDir.Z);
            if (traceDirLen > 0.001f)
            {
                // Normalize and offset back by 32 units (player hull radius)
                var nx = traceDir.X / traceDirLen;
                var ny = traceDir.Y / traceDirLen;
                var nz = traceDir.Z / traceDirLen;
                const float wallOffset = 32f;
                hitPos = new Vector(hitPos.X - nx * wallOffset, hitPos.Y - ny * wallOffset, hitPos.Z - nz * wallOffset);
            }
            // Add small Z offset so player doesn't clip into the ground
            return new Vector(hitPos.X, hitPos.Y, hitPos.Z + 10);
        }

        return null;
    }

    public void OnNoclipCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.NoClip);

        if (!HasPermission(context, _permissions.NoClip))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["noclip_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!CanTarget(context, target, allowSelf: true))
        {
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var targetName = target.Controller.PlayerName;

        bool isEnabled = _noclipPlayers.Contains(target.PlayerID);
        
        if (isEnabled)
        {
            PlayerUtils.SetNoclip(_core, target, false);
            _noclipPlayers.Remove(target.PlayerID);
        }
        else
        {
            PlayerUtils.SetNoclip(_core, target, true);
            _noclipPlayers.Add(target.PlayerID);
        }

        var state = !isEnabled ? $"\x04{PluginLocalizer.Get(_core)["noclip_on"]}\x01" : $"\x02{PluginLocalizer.Get(_core)["noclip_off"]}\x01";
        
        // Personal message to target
        var stateText = !isEnabled ? PluginLocalizer.Get(_core)["noclip_on"] : PluginLocalizer.Get(_core)["noclip_off"];
        var stateColor = !isEnabled ? "#00ff00" : "#ff0000";
        PlayerUtils.SendNotification(target, _messagesConfig,
            PluginLocalizer.Get(_core)["noclip_toggled_personal_html", stateColor, stateText, adminName],
            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["noclip_toggled_personal_chat", state, adminName]}");

        // Broadcast to all players
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["noclip_toggled_notification", adminName, state, targetName]}");
        }

        _ = _adminLogManager.AddLogAsync("noclip", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"state={(!isEnabled ? "on" : "off")}", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} toggled noclip {State} for {Target}", 
            adminName, !isEnabled ? "ON" : "OFF", targetName);
    }

    public void OnFreezeCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Freeze);

        if (!HasPermission(context, _permissions.Freeze))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["freeze_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        int? durationSeconds = null;
        if (args.Length >= 2 && int.TryParse(args[1], out var parsedSeconds) && parsedSeconds > 0)
        {
            durationSeconds = parsedSeconds;
        }

        foreach (var target in targets)
        {
            PlayerUtils.Freeze(target);
            _frozenPlayers.Add(target.PlayerID);

            if (durationSeconds.HasValue)
            {
                var playerId = target.PlayerID;
                _core.Scheduler.DelayBySeconds(durationSeconds.Value, () =>
                {
                    var player = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
                    if (player == null)
                        return;

                    if (_frozenPlayers.Contains(playerId))
                    {
                        PlayerUtils.Unfreeze(player);
                        _frozenPlayers.Remove(playerId);
                    }
                });
            }
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["frozen_personal_html", adminName],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["frozen_personal_chat", adminName]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["freeze_notification_single", adminName, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["freeze_notification_multiple", adminName, targets.Count]}");
            }
        }

        var freezeTargetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("freeze", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={freezeTargetSteamIds};count={targets.Count};duration={durationSeconds?.ToString() ?? "0"}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} froze {Count} player(s)", adminName, targets.Count);
    }

    public void OnUnfreezeCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Unfreeze);

        if (!HasPermission(context, _permissions.Unfreeze))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfreeze_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        foreach (var target in targets)
        {
            PlayerUtils.Unfreeze(target);
            _frozenPlayers.Remove(target.PlayerID);
        }

        // Personal message to targets
        foreach (var target in targets)
        {
            PlayerUtils.SendNotification(target, _messagesConfig,
                PluginLocalizer.Get(_core)["unfrozen_personal_html", adminName],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfrozen_personal_chat", adminName]}");
        }

        if (targets.Count == 1)
        {
            var targetName = targets[0].Controller.PlayerName;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfreeze_notification_single", adminName, targetName]}");
            }
        }
        else
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unfreeze_notification_multiple", adminName, targets.Count]}");
            }
        }

        var unfreezeTargetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
        _ = _adminLogManager.AddLogAsync("unfreeze", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={unfreezeTargetSteamIds};count={targets.Count}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} unfroze {Count} player(s)", adminName, targets.Count);
    }

    public void OnResizeCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Resize);

        if (!HasPermission(context, _permissions.Resize))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !TryParseFloat(args[1], out var scale))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["resize_usage"]}");
            return;
        }

        scale = Math.Clamp(scale, 0.2f, 3.0f);
        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var applied = 0;
        foreach (var target in targets)
        {
            if (TrySetPlayerScale(target, scale))
            {
                applied++;
            }
        }

        if (applied == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["resize_not_supported"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["resize_notification", adminName, applied, scale.ToString("0.00", CultureInfo.InvariantCulture)]}");
        }

        _ = _adminLogManager.AddLogAsync("resize", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={applied};scale={scale.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    public void OnDrugCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Drug);

        if (!HasPermission(context, _permissions.Drug))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["drug_usage"]}");
            return;
        }

        var durationSeconds = 10;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDuration))
        {
            durationSeconds = Math.Clamp(parsedDuration, 1, 60);
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            _drugPlayers.Add(target.PlayerID);
            if (target.PlayerPawn?.AbsRotation is { } rotation)
            {
                _drugOriginalRotations[target.PlayerID] = rotation;
            }
            StartDrugEffect(target.SteamID, durationSeconds);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["drug_notification", adminName, targets.Count, durationSeconds]}");
        }

        _ = _adminLogManager.AddLogAsync("drug", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={durationSeconds}");
    }

    public void OnBeaconCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Beacon);

        if (!HasPermission(context, _permissions.Beacon))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_usage"]}");
            return;
        }

        var durationSeconds = 20;
        var stopRequested = args.Length > 1 && (args[1].Equals("off", StringComparison.OrdinalIgnoreCase) || args[1] == "0");
        if (args.Length > 1 && !stopRequested && int.TryParse(args[1], out var parsedDuration))
        {
            durationSeconds = Math.Clamp(parsedDuration, 1, 120);
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var started = 0;
        var stopped = 0;
        foreach (var target in targets)
        {
            if (stopRequested)
            {
                if (_beaconPlayers.Remove(target.PlayerID))
                {
                    stopped++;
                }

                continue;
            }

            _beaconPlayers.Add(target.PlayerID);
            StartBeaconEffect(target.SteamID, durationSeconds);
            started++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        if (stopRequested)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_stopped", stopped]}");
            _ = _adminLogManager.AddLogAsync("beacon", adminName, context.Sender?.SteamID ?? 0, null, null, $"mode=off;targets={stopped}");
            return;
        }

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_started", adminName, started, durationSeconds]}");
        }

        _ = _adminLogManager.AddLogAsync("beacon", adminName, context.Sender?.SteamID ?? 0, null, null, $"mode=on;targets={started};duration={durationSeconds}");
    }

    public void OnBurnCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Burn);

        if (!HasPermission(context, _permissions.Burn))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["burn_usage"]}");
            return;
        }

        var durationSeconds = 8;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedDuration))
        {
            durationSeconds = Math.Clamp(parsedDuration, 1, 60);
        }

        var damagePerTick = 5;
        if (args.Length > 2 && int.TryParse(args[2], out var parsedDamage))
        {
            damagePerTick = Math.Clamp(parsedDamage, 1, 100);
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            _burnPlayers.Add(target.PlayerID);
            StartBurnEffect(target.SteamID, durationSeconds, damagePerTick);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["burn_notification", adminName, targets.Count, durationSeconds, damagePerTick]}");
        }

        _ = _adminLogManager.AddLogAsync("burn", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};duration={durationSeconds};dmg={damagePerTick}");
    }

    public void OnDisarmCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Disarm);

        if (!HasPermission(context, _permissions.Disarm))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["disarm_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var changed = 0;
        foreach (var target in targets)
        {
            var itemServices = target.PlayerPawn?.ItemServices;
            if (itemServices?.IsValid == true)
            {
                itemServices.RemoveItems();
                changed++;
            }
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["disarm_notification", adminName, changed]}");
        }

        _ = _adminLogManager.AddLogAsync("disarm", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed}");
    }

    public void OnSpeedCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Speed);

        if (!HasPermission(context, _permissions.Speed))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !TryParseFloat(args[1], out var speedMultiplier))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["speed_usage"]}");
            return;
        }

        speedMultiplier = Math.Clamp(speedMultiplier, 0.1f, 10.0f);

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                continue;
            }

            pawn.VelocityModifier = speedMultiplier;
            pawn.VelocityModifierUpdated();
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["speed_notification", adminName, targets.Count, speedMultiplier.ToString("0.00", CultureInfo.InvariantCulture)]}");
        }

        _ = _adminLogManager.AddLogAsync("speed", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};speed={speedMultiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    public void OnGravityCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Gravity);

        if (!HasPermission(context, _permissions.Gravity))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !TryParseFloat(args[1], out var gravityMultiplier))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gravity_usage"]}");
            return;
        }

        gravityMultiplier = Math.Clamp(gravityMultiplier, 0.1f, 5.0f);

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        foreach (var target in targets)
        {
            ApplyGravityWithRetries(target.SteamID, gravityMultiplier, retries: 6, totalRetries: 6);
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gravity_notification", adminName, targets.Count, gravityMultiplier.ToString("0.00", CultureInfo.InvariantCulture)]}");
        }

        _ = _adminLogManager.AddLogAsync("gravity", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={targets.Count};gravity={gravityMultiplier.ToString("0.00", CultureInfo.InvariantCulture)}");
    }

    public void OnRenameCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Rename);

        if (!HasPermission(context, _permissions.Rename))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rename_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var newName = string.Join(" ", args.Skip(1)).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(newName))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rename_usage"]}");
            return;
        }

        if (newName.Length > 64)
        {
            newName = newName[..64];
        }

        var changed = 0;
        foreach (var target in targets)
        {
            if (target.Controller == null || !target.Controller.IsValid)
            {
                continue;
            }

            target.Controller.PlayerName = newName;
            target.Controller.PlayerNameUpdated();
            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rename_notification", adminName, changed, newName]}");
        }

        _ = _adminLogManager.AddLogAsync("rename", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed};name={newName}");
    }

    public void OnHpCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Hp);

        if (!HasPermission(context, _permissions.Hp))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var hp))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["hp_usage"]}");
            return;
        }

        hp = Math.Clamp(hp, 0, 1000);
        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var changed = 0;
        foreach (var target in targets)
        {
            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                continue;
            }

            if (hp <= 0 && pawn.Health > 0)
            {
                pawn.CommitSuicide(false, true);
            }
            else
            {
                pawn.Health = hp;
                pawn.HealthUpdated();
            }

            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["hp_notification", adminName, changed, hp]}");
        }

        _ = _adminLogManager.AddLogAsync("hp", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed};hp={hp}");
    }

    public void OnMoneyCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Money);

        if (!HasPermission(context, _permissions.Money))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var amount))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["money_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var commandName = context.CommandName ?? string.Empty;
        var isAdditive = commandName.Contains("givemoney", StringComparison.OrdinalIgnoreCase);
        amount = Math.Clamp(amount, 0, 65000);

        var changed = 0;
        foreach (var target in targets)
        {
            var moneyServices = target.Controller.InGameMoneyServices;
            if (moneyServices?.IsValid != true)
            {
                continue;
            }

            if (isAdditive)
            {
                var next = Math.Clamp(moneyServices.Account + amount, 0, 65000);
                moneyServices.Account = next;
            }
            else
            {
                moneyServices.Account = amount;
            }

            moneyServices.AccountUpdated();
            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var key = isAdditive ? "money_add_notification" : "money_set_notification";
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)[key, adminName, changed, amount]}");
        }

        var mode = isAdditive ? "add" : "set";
        _ = _adminLogManager.AddLogAsync("money", adminName, context.Sender?.SteamID ?? 0, null, null, $"mode={mode};targets={changed};amount={amount}");
    }

    public void OnGiveCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Give);

        if (!HasPermission(context, _permissions.Give))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["give_usage"]}");
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0], includeDeadPlayers: false)
            .Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0)
            .ToList();
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        targets = FilterTargetsByCanTarget(context, targets, allowSelf: true);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_valid_targets"]}");
            return;
        }

        var item = NormalizeItemName(args[1]);
        var changed = 0;
        foreach (var target in targets)
        {
            var itemServices = target.PlayerPawn?.ItemServices;
            if (itemServices?.IsValid != true)
            {
                continue;
            }

            itemServices.GiveItem(item);
            changed++;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["give_notification", adminName, changed, item]}");
        }

        _ = _adminLogManager.AddLogAsync("give", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={changed};item={item}");
    }

    public void OnPlayersCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.ListPlayers))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.ListPlayers);
        var isJson = args.Length >= 1 && string.Equals(args[0], "-json", StringComparison.OrdinalIgnoreCase);

        var players = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .OrderBy(p => p.Controller.PlayerName)
            .ToList();

        if (!isJson)
        {
            var lines = new List<string>
            {
                PluginLocalizer.Get(_core)["players_list_header"]
            };

            foreach (var player in players)
            {
                var tag = _tags.Enabled
                    ? PlayerUtils.GetScoreTag(player, _tags.PlayerTag)
                    : "-";

                lines.Add(
                    PluginLocalizer.Get(_core)["players_list_entry", player.PlayerID, tag, player.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", player.PlayerID]]);
            }

            lines.Add(PluginLocalizer.Get(_core)["players_list_footer"]);

            var output = string.Join('\n', lines);

            if (context.IsSentByPlayer && context.Sender != null)
            {
                context.Sender.SendConsole(output);

                if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                {
                    context.Sender.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["players_list_console"]}");
                }
            }
            else
            {
                _core.Logger.LogInformationIfEnabled("{PlayerList}", output);
            }
        }
        else
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            var entries = players
                .Select(p =>
                {
                    var teamNum = p.Controller.TeamNum;
                    var teamName = PlayerUtils.GetTeamName(teamNum, PluginLocalizer.Get(_core));
                    var score = p.Controller.Score;
                    var ping = (int)p.Controller.Ping;
                    var isAlive = p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0;
                    var ip = (p.IPAddress ?? PluginLocalizer.Get(_core)["unknown"]).Split(':')[0];
                    var tag = _tags.Enabled
                        ? PlayerUtils.GetScoreTag(p, _tags.PlayerTag)
                        : "-";

                    return new PlayerListEntry(
                        p.PlayerID,
                        p.Controller.PlayerName,
                        p.SteamID.ToString(),
                        teamNum,
                        teamName,
                        score,
                        ping,
                        isAlive,
                        ip,
                        tag
                    );
                })
                .ToList();

            var json = JsonSerializer.Serialize(entries, options);

            if (context.IsSentByPlayer && context.Sender != null)
            {
                context.Sender.SendConsole(json);

                if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                {
                    context.Sender.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["players_list_json_console"]}");
                }
            }
            else
            {
                _core.Logger.LogInformationIfEnabled("{PlayerListJson}", json);
            }
        }
    }

    public void OnWhoCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Who);

        if (!HasPermission(context, _permissions.Who))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["who_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        var steamId64 = target.SteamID;
        var targetPlayerId = target.PlayerID;
        var targetIp = target.IPAddress;

        _ = Task.Run(async () =>
        {
            var admin = await _adminDbManager.GetAdminAsync(steamId64);
            var effectiveFlags = await _adminDbManager.GetEffectiveFlagsAsync(steamId64);
            var effectiveImmunity = await _adminDbManager.GetEffectiveImmunityAsync(steamId64);
            var ban = await _banManager.GetActiveBanAsync(steamId64, targetIp, _multiServerConfig.Enabled);
            var mute = await _muteManager.GetActiveMuteAsync(steamId64);
            var gag = await _gagManager.GetActiveGagAsync(steamId64);
            var warn = await _warnManager.GetActiveWarnAsync(steamId64);
            var totalBans = await _banManager.GetTotalBansAsync(steamId64);
            var totalMutes = await _muteManager.GetTotalMutesAsync(steamId64);
            var totalGags = await _gagManager.GetTotalGagsAsync(steamId64);
            var totalWarns = await _warnManager.GetTotalWarnsAsync(steamId64);

            _core.Scheduler.NextTick(() =>
            {
                var liveTarget = _core.PlayerManager.GetPlayer(targetPlayerId);
                var name = liveTarget?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["player_fallback_name", targetPlayerId];
                var userId = targetPlayerId;
                var ip = (liveTarget?.IPAddress ?? targetIp ?? PluginLocalizer.Get(_core)["who_unknown"]).Split(':')[0];
                var ping = liveTarget != null ? (int)liveTarget.Controller.Ping : 0;
                var teamNum = liveTarget?.Controller.TeamNum ?? 0;
                var teamName = PlayerUtils.GetTeamName(teamNum, PluginLocalizer.Get(_core));
                var isAlive = liveTarget?.PlayerPawn?.IsValid == true && liveTarget.PlayerPawn.Health > 0;

                var lines = new List<string>
                {
                    PluginLocalizer.Get(_core)["who_header", name],
                    PluginLocalizer.Get(_core)["who_name", name],
                    PluginLocalizer.Get(_core)["who_userid", userId],
                    PluginLocalizer.Get(_core)["who_steamid", steamId64],
                    PluginLocalizer.Get(_core)["who_team", teamName, teamNum],
                    PluginLocalizer.Get(_core)["who_ip", ip],
                    PluginLocalizer.Get(_core)["who_ping", ping],
                    PluginLocalizer.Get(_core)["who_alive", isAlive ? PluginLocalizer.Get(_core)["players_yes"] : PluginLocalizer.Get(_core)["players_no"]]
                };

                if (admin != null)
                {
                    var flags = effectiveFlags.Length == 0
                        ? PluginLocalizer.Get(_core)["who_none"]
                        : string.Join(",", effectiveFlags);
                    lines.Add(PluginLocalizer.Get(_core)["who_admin_flags", flags, effectiveImmunity]);
                }

                if (ban != null && ban.IsActive)
                {
                    var expires = ban.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : ban.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_ban_yes", ban.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_ban_no"]);
                }

                if (mute != null && mute.IsActive)
                {
                    var expires = mute.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : mute.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_mute_yes", mute.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_mute_no"]);
                }

                if (gag != null && gag.IsActive)
                {
                    var expires = gag.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : gag.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_gag_yes", gag.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_gag_no"]);
                }

                if (warn != null && warn.IsActive)
                {
                    var expires = warn.IsPermanent ? PluginLocalizer.Get(_core)["duration_permanent"] : warn.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? PluginLocalizer.Get(_core)["who_unknown"];
                    lines.Add(PluginLocalizer.Get(_core)["who_active_warn_yes", warn.Reason, expires]);
                }
                else
                {
                    lines.Add(PluginLocalizer.Get(_core)["who_active_warn_no"]);
                }

                lines.Add(PluginLocalizer.Get(_core)["who_total_bans", totalBans]);
                lines.Add(PluginLocalizer.Get(_core)["who_total_mutes", totalMutes]);
                lines.Add(PluginLocalizer.Get(_core)["who_total_gags", totalGags]);
                lines.Add(PluginLocalizer.Get(_core)["who_total_warns", totalWarns]);

                lines.Add(PluginLocalizer.Get(_core)["who_footer", name]);

                var output = string.Join('\n', lines);

                if (context.IsSentByPlayer && context.Sender != null)
                {
                    context.Sender.SendConsole(output);

                    if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                    {
                        context.Sender.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["who_console"]}");
                    }
                }
                else
                {
                    _core.Logger.LogInformationIfEnabled("{WhoInfo}", output);
                }
            });
        });
    }

    public void OnPlayerDisconnect(int playerId)
    {
        _noclipPlayers.Remove(playerId);
        _frozenPlayers.Remove(playerId);
        _beaconPlayers.Remove(playerId);
        _burnPlayers.Remove(playerId);
        _drugPlayers.Remove(playerId);
        _drugOriginalRotations.Remove(playerId);
    }

    private void StartBeaconEffect(ulong targetSteamId, int ticksRemaining)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            if (target?.IsValid != true)
            {
                return;
            }

            if (!_beaconPlayers.Contains(target.PlayerID))
            {
                return;
            }

            var targetName = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
            var playerId = target.PlayerID;
            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["beacon_ping", targetName, playerId]}");
            }

            if (ticksRemaining <= 1)
            {
                _beaconPlayers.Remove(playerId);
                return;
            }

            StartBeaconEffect(targetSteamId, ticksRemaining - 1);
        });
    }

    private void StartBurnEffect(ulong targetSteamId, int ticksRemaining, int damagePerTick)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            if (target?.IsValid != true)
            {
                return;
            }

            var playerId = target.PlayerID;
            if (!_burnPlayers.Contains(playerId))
            {
                return;
            }

            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true || pawn.Health <= 0)
            {
                _burnPlayers.Remove(playerId);
                return;
            }

            var nextHealth = Math.Max(pawn.Health - damagePerTick, 0);
            if (nextHealth <= 0)
            {
                pawn.CommitSuicide(false, true);
                _burnPlayers.Remove(playerId);
                return;
            }

            pawn.Health = nextHealth;
            pawn.HealthUpdated();
            PlayerUtils.SendNotification(
                target,
                _messagesConfig,
                PluginLocalizer.Get(_core)["burn_personal_html", damagePerTick],
                $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["burn_personal_chat", damagePerTick]}");

            if (ticksRemaining <= 1)
            {
                _burnPlayers.Remove(playerId);
                return;
            }

            StartBurnEffect(targetSteamId, ticksRemaining - 1, damagePerTick);
        });
    }

    private void StartDrugEffect(ulong targetSteamId, int ticksRemaining)
    {
        _core.Scheduler.DelayBySeconds(1f, () =>
        {
            var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            if (target?.IsValid != true)
            {
                return;
            }

            var playerId = target.PlayerID;
            if (!_drugPlayers.Contains(playerId))
            {
                return;
            }

            var pawn = target.PlayerPawn;
            if (pawn?.IsValid != true || pawn.Health <= 0)
            {
                _drugPlayers.Remove(playerId);
                _drugOriginalRotations.Remove(playerId);
                return;
            }

            pawn.VelocityModifier = 0.60f;
            pawn.VelocityModifierUpdated();

            var origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
            var baseRot = _drugOriginalRotations.TryGetValue(playerId, out var savedRot)
                ? savedRot
                : (pawn.AbsRotation ?? new QAngle(0, 0, 0));
            var nextYaw = baseRot.Y + Random.Shared.Next(-18, 19);
            var nextRoll = ticksRemaining % 2 == 0 ? 12f : -12f;
            var randomX = Random.Shared.Next(-45, 46);
            var randomY = Random.Shared.Next(-45, 46);
            var currentVelocity = pawn.AbsVelocity;
            pawn.AbsVelocity = new Vector(currentVelocity.X + randomX, currentVelocity.Y + randomY, currentVelocity.Z);
            pawn.Teleport(origin, new QAngle(baseRot.X, nextYaw, nextRoll), pawn.AbsVelocity);

            if (ticksRemaining <= 1)
            {
                pawn.VelocityModifier = 1.0f;
                pawn.VelocityModifierUpdated();
                var finalOrigin = pawn.AbsOrigin ?? origin;
                if (_drugOriginalRotations.TryGetValue(playerId, out var originalRot))
                {
                    pawn.Teleport(finalOrigin, new QAngle(originalRot.X, originalRot.Y, 0f), pawn.AbsVelocity);
                }
                _drugPlayers.Remove(playerId);
                _drugOriginalRotations.Remove(playerId);
                return;
            }

            StartDrugEffect(targetSteamId, ticksRemaining - 1);
        });
    }

    private void ApplyGravityWithRetries(ulong targetSteamId, float gravityMultiplier, int retries, int totalRetries)
    {
        _core.Scheduler.NextTick(() =>
        {
            var target = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
            var pawn = target?.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                return;
            }

            var applied = TryApplyGravity(target!, gravityMultiplier, out var appliedBy);
            if (!applied && retries <= 0)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Gravity could not be applied to {SteamId}. Runtime does not expose a writable gravity member.", targetSteamId);
            }
            else if (applied && retries == totalRetries)
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] gravity applied steamid={SteamId} value={Gravity} via={Method}", targetSteamId, gravityMultiplier, appliedBy);
            }
        });

        if (retries <= 0)
        {
            return;
        }

        _core.Scheduler.DelayBySeconds(0.5f, () => ApplyGravityWithRetries(targetSteamId, gravityMultiplier, retries - 1, totalRetries));
    }

    private bool TryApplyGravity(IPlayer target, float gravityMultiplier, out string method)
    {
        method = string.Empty;
        var pawn = target.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            return false;
        }

        var applied = false;

        try
        {
            pawn.GravityScale = gravityMultiplier;
            pawn.GravityScaleUpdated();
            method = "Pawn.GravityScale";
            applied = true;
        }
        catch
        {
            // Continue with reflection-based fallbacks.
        }

        if (TrySetFloatPropertyWithUpdated(pawn, "GravityScale", gravityMultiplier))
        {
            method = string.IsNullOrWhiteSpace(method) ? "Pawn.GravityScale(reflection)" : method;
            applied = true;
        }

        if (TrySetFloatPropertyWithUpdated(pawn, "Gravity", gravityMultiplier))
        {
            method = string.IsNullOrWhiteSpace(method) ? "Pawn.Gravity(reflection)" : method;
            applied = true;
        }

        if (TrySetFloatPropertyWithUpdated(pawn, "GravityMultiplier", gravityMultiplier))
        {
            method = string.IsNullOrWhiteSpace(method) ? "Pawn.GravityMultiplier(reflection)" : method;
            applied = true;
        }

        if (target.Controller?.IsValid == true)
        {
            if (TrySetFloatPropertyWithUpdated(target.Controller, "GravityScale", gravityMultiplier))
            {
                method = string.IsNullOrWhiteSpace(method) ? "Controller.GravityScale(reflection)" : method;
                applied = true;
            }

            if (TrySetFloatPropertyWithUpdated(target.Controller, "Gravity", gravityMultiplier))
            {
                method = string.IsNullOrWhiteSpace(method) ? "Controller.Gravity(reflection)" : method;
                applied = true;
            }
        }

        if (applied)
        {
            var origin = pawn.AbsOrigin;
            var rotation = pawn.AbsRotation;
            if (origin != null && rotation != null)
            {
                pawn.Teleport(origin, rotation, pawn.AbsVelocity);
            }
        }

        return applied;
    }

    private static bool TryParseFloat(string input, out float value)
    {
        return float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static string NormalizeItemName(string input)
    {
        var normalized = input.Trim();
        if (normalized.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return $"weapon_{normalized}";
    }

    private bool TrySetPlayerScale(IPlayer player, float scale)
    {
        var applied = false;
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid == true)
        {
            try
            {
                pawn.SetScale(scale);
                applied = true;
            }
            catch
            {
                // Runtime may not expose SetScale for this pawn type; fallback to reflection below.
            }

            applied |= TrySetFloatPropertyWithUpdated(pawn, "Scale", scale);
            applied |= TrySetFloatPropertyWithUpdated(pawn, "ModelScale", scale);
        }

        if (player.Controller?.IsValid == true)
        {
            applied |= TrySetFloatPropertyWithUpdated(player.Controller, "Scale", scale);
            applied |= TrySetFloatPropertyWithUpdated(player.Controller, "ModelScale", scale);
        }

        return applied;
    }

    private static bool TrySetFloatPropertyWithUpdated(object instance, string propertyName, float value)
    {
        try
        {
            var type = instance.GetType();
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                object converted = property.PropertyType == typeof(float)
                    ? value
                    : Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture);
                property.SetValue(instance, converted);

                var updated = type.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updated?.Invoke(instance, null);
                return true;
            }

            var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && !field.IsInitOnly)
            {
                object converted = field.FieldType == typeof(float)
                    ? value
                    : Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture);
                field.SetValue(instance, converted);

                var updated = type.GetMethod($"{propertyName}Updated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updated?.Invoke(instance, null);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
    }

    private bool CanTarget(ICommandContext context, IPlayer target, bool allowSelf = false)
    {
        if (!context.IsSentByPlayer || context.Sender == null)
        {
            return true;
        }

        if (context.Sender.SteamID == target.SteamID)
        {
            if (allowSelf)
            {
                return true;
            }

            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self"]}");
            return false;
        }

        var adminImm = _adminDbManager.GetEffectiveImmunityAsync(context.Sender.SteamID).GetAwaiter().GetResult();
        var targetImm = _adminDbManager.GetEffectiveImmunityAsync(target.SteamID).GetAwaiter().GetResult();
        if (targetImm >= adminImm && targetImm > 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_immunity"]}");
            return false;
        }

        return true;
    }

    private List<IPlayer> FilterTargetsByCanTarget(ICommandContext context, IEnumerable<IPlayer> targets, bool allowSelf = false)
    {
        var result = new List<IPlayer>();
        foreach (var target in targets)
        {
            if (CanTarget(context, target, allowSelf))
            {
                result.Add(target);
            }
        }

        return result;
    }

}


