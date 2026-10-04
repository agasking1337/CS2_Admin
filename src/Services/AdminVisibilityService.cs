using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Services;

public class AdminVisibilityService
{
    private const string TeamSelectCvar = "sv_disable_teamselect_menu";
    private static readonly TimeSpan TeamChangeGrace = TimeSpan.FromSeconds(2);

    private readonly ISwiftlyCore _core;
    private readonly object _lock = new();
    private readonly Dictionary<ulong, HiddenAdmin> _hiddenAdmins = new();

    public AdminVisibilityService(ISwiftlyCore core)
    {
        _core = core;
    }

    public bool IsHidden(ulong steamId)
    {
        lock (_lock)
            return _hiddenAdmins.ContainsKey(steamId);
    }

    public bool Hide(IPlayer player, Team previousTeam)
    {
        if (!player.IsValid || player.IsFakeClient)
            return false;

        lock (_lock)
        {
            if (_hiddenAdmins.ContainsKey(player.SteamID))
                return true;

            _hiddenAdmins[player.SteamID] = new HiddenAdmin(player.PlayerID, (int)player.Controller.Index, DateTime.UtcNow, previousTeam);
        }

        _core.Engine.ExecuteCommand($"{TeamSelectCvar} 1");

        var pawn = player.PlayerPawn;
        if (pawn?.IsValid == true && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
            pawn.CommitSuicide(true, false);

        if (previousTeam > Team.Spectator)
            _core.Scheduler.DelayBySeconds(0.15f, () => _core.Scheduler.NextWorldUpdate(() => ChangeTeam(player, Team.Spectator)));

        _core.Scheduler.DelayBySeconds(0.26f, () => _core.Scheduler.NextWorldUpdate(() => ChangeTeam(player, Team.None)));
        _core.Scheduler.DelayBySeconds(0.5f, () => _core.Scheduler.NextWorldUpdate(() => _core.Engine.ExecuteCommand($"{TeamSelectCvar} 0")));
        return true;
    }

    public void Show(IPlayer player)
    {
        HiddenAdmin? hidden;
        lock (_lock)
        {
            if (!_hiddenAdmins.Remove(player.SteamID, out hidden))
                return;
        }

        Restore(player, hidden);
    }

    public void OnTeamChanged(CCSPlayerController? controller, Team newTeam)
    {
        if (newTeam == Team.None || controller == null || !controller.IsValid)
            return;

        var index = (int)controller.Index;
        lock (_lock)
        {
            var entry = _hiddenAdmins.FirstOrDefault(kv => kv.Value.ControllerEntityId == index);
            if (entry.Value == null || DateTime.UtcNow - entry.Value.HiddenAt < TeamChangeGrace)
                return;

            _hiddenAdmins.Remove(entry.Key);
        }
    }

    public void Remove(ulong steamId)
    {
        lock (_lock)
            _hiddenAdmins.Remove(steamId);
    }

    public void RestoreAll()
    {
        KeyValuePair<ulong, HiddenAdmin>[] all;
        lock (_lock)
        {
            all = _hiddenAdmins.ToArray();
            _hiddenAdmins.Clear();
        }

        foreach (var (_, hidden) in all)
        {
            var player = _core.PlayerManager.GetPlayer(hidden.PlayerId);
            if (player?.IsValid == true)
                Restore(player, hidden);
        }
    }

    private void Restore(IPlayer player, HiddenAdmin hidden)
    {
        if (player.Controller.Team > Team.Spectator)
            return;

        var target = hidden.PreviousTeam > Team.Spectator ? hidden.PreviousTeam : Team.Spectator;
        _core.Engine.ExecuteCommand($"{TeamSelectCvar} 1");
        ChangeTeam(player, target);
        _core.Scheduler.DelayBySeconds(0.5f, () => _core.Scheduler.NextWorldUpdate(() => _core.Engine.ExecuteCommand($"{TeamSelectCvar} 0")));
    }

    private static void ChangeTeam(IPlayer player, Team team)
    {
        if (player.IsValid)
            player.ChangeTeam(team);
    }

    private sealed record HiddenAdmin(int PlayerId, int ControllerEntityId, DateTime HiddenAt, Team PreviousTeam);
}
