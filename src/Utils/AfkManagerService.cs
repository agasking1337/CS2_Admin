using CS2_Admin.Config;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Sounds;

namespace CS2_Admin.Utils;

public sealed class AfkManagerService
{
    private const float CheckIntervalSeconds = 1f;

    private readonly ISwiftlyCore _core;
    private readonly AfkFileConfig _config;
    private readonly PermissionsConfig _permissions;
    private readonly MessagesConfig _messagesConfig;
    private readonly Dictionary<ulong, AfkState> _states = new();

    private CancellationTokenSource? _timerCts;
    private bool _isWarmup;
    private bool _eventsRegistered;

    public AfkManagerService(
        ISwiftlyCore core,
        AfkFileConfig config,
        PermissionsConfig permissions,
        MessagesConfig messagesConfig)
    {
        _core = core;
        _config = config;
        _permissions = permissions;
        _messagesConfig = messagesConfig;
    }

    public void Start()
    {
        if (!_config.Enabled)
        {
            Stop();
            _core.Logger.LogInformationIfEnabled("[Debug][AFK] manager disabled by config");
            return;
        }

        RegisterEventsOnce();
        Stop();
        _states.Clear();
        _timerCts = _core.Scheduler.DelayAndRepeatBySeconds(CheckIntervalSeconds, CheckIntervalSeconds, CheckPlayers);
        _core.Logger.LogInformationIfEnabled("[Debug][AFK] manager started timer={Timer} skipWarmup={SkipWarmup} skipAdmin={SkipAdmin}", GetAfkSeconds(), _config.SkipWarmup, _config.AfkSkipAdmin);
    }

    public void Stop()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
        _states.Clear();
    }

    public void OnAfkCommand(ICommandContext context)
    {
        if (!context.IsSentByPlayer || context.Sender?.IsValid != true)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {L("afk_command_only_player", "This command can only be used by players.")}");
            return;
        }

        MoveToSpectator(context.Sender, notifyAll: false, reason: "command");
    }

    private void RegisterEventsOnce()
    {
        if (_eventsRegistered)
        {
            return;
        }

        _eventsRegistered = true;
        _core.GameEvent.HookPre<EventRoundAnnounceWarmup>(@event =>
        {
            _isWarmup = true;
            _states.Clear();
            _core.Logger.LogInformationIfEnabled("[Debug][AFK] warmup started");
            return HookResult.Continue;
        });

        _core.GameEvent.HookPre<EventRoundStart>(@event =>
        {
            _isWarmup = false;
            _states.Clear();
            _core.Logger.LogInformationIfEnabled("[Debug][AFK] round started");
            return HookResult.Continue;
        });
    }

    private void CheckPlayers()
    {
        if (_config.SkipWarmup && _isWarmup)
        {
            _states.Clear();
            return;
        }

        var now = DateTime.UtcNow;
        var seen = new HashSet<ulong>();
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player?.IsValid != true || player.IsFakeClient || player.SteamID == 0)
            {
                continue;
            }

            var team = player.Controller?.TeamNum ?? 0;
            if (team <= 1)
            {
                _states.Remove(player.SteamID);
                continue;
            }

            if (_config.AfkSkipAdmin && IsAdmin(player.SteamID))
            {
                _states.Remove(player.SteamID);
                continue;
            }

            var pawn = player.PlayerPawn;
            var isAlive = pawn?.IsValid == true && pawn.Health > 0;
            if (pawn?.IsValid != true)
            {
                continue;
            }

            if (!isAlive)
            {
                if (_states.TryGetValue(player.SteamID, out var deadState) && deadState.AllowDeadCountdown)
                {
                    seen.Add(player.SteamID);
                    var deadIdleSeconds = (now - deadState.LastActivityAt).TotalSeconds;
                    var deadAfkSeconds = GetAfkSeconds();
                    if (!deadState.Warned && deadIdleSeconds >= Math.Max(1, deadAfkSeconds - 10))
                    {
                        deadState.Warned = true;
                        WarnPlayer(player, Math.Max(1, (int)Math.Ceiling(deadAfkSeconds - deadIdleSeconds)));
                    }

                    if (deadIdleSeconds >= deadAfkSeconds)
                    {
                        MoveToSpectator(player, notifyAll: true, reason: "dead_idle");
                        _states.Remove(player.SteamID);
                    }
                }

                continue;
            }

            var pos = pawn.AbsOrigin;
            if (pos == null)
            {
                continue;
            }

            seen.Add(player.SteamID);
            var angle = pawn.AbsRotation ?? new QAngle(0, 0, 0);
            if (!_states.TryGetValue(player.SteamID, out var state))
            {
                _states[player.SteamID] = new AfkState(pos.Value, angle, now) { WasAlive = true };
                continue;
            }

            if (HasActivity(state, pos.Value, angle))
            {
                state.LastPosition = pos.Value;
                state.LastAngle = angle;
                state.LastActivityAt = now;
                state.Warned = false;
                state.AllowDeadCountdown = false;
                state.WasAlive = true;
                continue;
            }

            var idleSeconds = (now - state.LastActivityAt).TotalSeconds;
            state.WasAlive = true;
            state.AllowDeadCountdown = idleSeconds >= 10;
            var afkSeconds = GetAfkSeconds();
            if (!state.Warned && idleSeconds >= Math.Max(1, afkSeconds - 10))
            {
                state.Warned = true;
                WarnPlayer(player, Math.Max(1, (int)Math.Ceiling(afkSeconds - idleSeconds)));
            }

            if (idleSeconds < afkSeconds)
            {
                continue;
            }

            MoveToSpectator(player, notifyAll: true, reason: "idle");
            _states.Remove(player.SteamID);
        }

        foreach (var steamId in _states.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _states.Remove(steamId);
        }
    }

    private void MoveToSpectator(IPlayer player, bool notifyAll, string reason)
    {
        if (player?.IsValid != true)
        {
            return;
        }

        var name = player.Controller?.PlayerName ?? player.SteamID.ToString();
        _core.Scheduler.NextTick(() =>
        {
            var live = _core.PlayerManager.GetAllPlayers().FirstOrDefault(x => x.IsValid && x.SteamID == player.SteamID);
            if (live?.IsValid != true)
            {
                return;
            }

            live.ChangeTeam(Team.Spectator);
            _states.Remove(live.SteamID);

            if (notifyAll)
            {
                BroadcastChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {L("afk_move_broadcast", "{0} was moved to spectator for being AFK.", name)}");
            }
            else
            {
                live.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {L("afk_self_spec", "You moved yourself to spectator.")}");
            }

            _core.Logger.LogInformationIfEnabled("[Debug][AFK] moved steamid={SteamId} name={Name} reason={Reason}", live.SteamID, name, reason);
        });
    }

    private void WarnPlayer(IPlayer player, int remainingSeconds)
    {
        if (!string.IsNullOrWhiteSpace(_config.WarningSound))
        {
            try
            {
                player.ExecuteCommand($"play {_config.WarningSound}");
            }
            catch (Exception ex)
            {
                _core.Logger.LogWarningIfEnabled("[Debug][AFK] warning sound failed: {Message}", ex.Message);
            }
        }

        PlayerUtils.SendNotification(
            _core,
            player,
            _messagesConfig,
            $"<font color='#ffcc00'><b>{L("afk_warning_html", "You will be moved to spectator in {0} seconds for being AFK.", remainingSeconds)}</b></font>",
            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {L("afk_warning_chat", "You will be moved to spectator in {0} seconds for being AFK.", remainingSeconds)}");
    }

    private void BroadcastChat(string message)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
        {
            player.SendChat(message);
        }
    }

    private bool IsAdmin(ulong steamId)
    {
        return HasPermission(steamId, _permissions.AdminRoot) || HasPermission(steamId, _permissions.AdminMenu);
    }

    private bool HasPermission(ulong steamId, string permission)
    {
        return !string.IsNullOrWhiteSpace(permission) && _core.Permission.PlayerHasPermission(steamId, permission);
    }

    private string L(string key, string fallback, params object[] args)
    {
        // The HU translation uses {seconds}/{player}, EN uses {0}.
        // LocalizerHelper normalizes either form before substitution.
        return global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback, args);
    }

    private float GetAfkSeconds()
    {
        return Math.Max(5f, _config.Timer);
    }

    private static bool HasActivity(AfkState state, Vector pos, QAngle angle)
    {
        return Math.Abs(pos.X - state.LastPosition.X) > 1f
            || Math.Abs(pos.Y - state.LastPosition.Y) > 1f
            || Math.Abs(pos.Z - state.LastPosition.Z) > 3f
            || Math.Abs(angle.X - state.LastAngle.X) > 1f
            || Math.Abs(angle.Y - state.LastAngle.Y) > 1f;
    }

    private sealed class AfkState
    {
        public AfkState(Vector lastPosition, QAngle lastAngle, DateTime now)
        {
            LastPosition = lastPosition;
            LastAngle = lastAngle;
            LastActivityAt = now;
        }

        public Vector LastPosition { get; set; }
        public QAngle LastAngle { get; set; }
        public DateTime LastActivityAt { get; set; }
        public bool Warned { get; set; }
        public bool AllowDeadCountdown { get; set; }
        public bool WasAlive { get; set; }
    }
}


