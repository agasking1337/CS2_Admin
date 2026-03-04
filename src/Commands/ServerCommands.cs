using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Core.Menus.OptionsBase;

namespace CS2_Admin.Commands;

public class ServerCommands
{
    private readonly ISwiftlyCore _core;
    private readonly AdminLogManager _adminLogManager;
    private readonly PermissionsConfig _permissions;
    private readonly GameMapsConfig _gameMaps;
    private readonly WorkshopMapsConfig _workshopMaps;
    private readonly CommandsConfig _commands;
    private readonly object _voteLock = new();
    private ActiveVoteState? _activeVote;

    private sealed class ActiveVoteState
    {
        public required string Question { get; init; }
        public required List<string> Answers { get; init; }
        public required Dictionary<ulong, int> VotesBySteamId { get; init; }
        public DateTime EndsAtUtc { get; init; }
        public required string StartedBy { get; init; }
        public ulong StartedBySteamId { get; init; }
    }

    public ServerCommands(
        ISwiftlyCore core, 
        AdminLogManager adminLogManager,
        PermissionsConfig permissions,
        GameMapsConfig gameMaps,
        WorkshopMapsConfig workshopMaps,
        CommandsConfig commands)
    {
        _core = core;
        _adminLogManager = adminLogManager;
        _permissions = permissions;
        _gameMaps = gameMaps;
        _workshopMaps = workshopMaps;
        _commands = commands;
    }

    public void OnMapCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.ChangeMap);

        if (!HasPermission(context, _permissions.ChangeMap))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["map_usage"]}");
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["map_available", string.Join(", ", _gameMaps.Maps.Keys)]}");
            return;
        }

        var mapName = args[0].ToLowerInvariant();
        
        // Check if map exists in config
        var matchedMap = _gameMaps.Maps.Keys.FirstOrDefault(m => 
            m.Equals(mapName, StringComparison.OrdinalIgnoreCase));

        if (matchedMap == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["map_not_found", mapName]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var mapDisplayName = _gameMaps.Maps[matchedMap];
        const float changeDelaySeconds = 3f;

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["map_changing", adminName, mapDisplayName, changeDelaySeconds]}");
        }

        _core.Scheduler.DelayBySeconds(changeDelaySeconds, () =>
        {
            _core.Engine.ExecuteCommand($"changelevel {matchedMap}");
        });

        _ = _adminLogManager.AddLogAsync("map", adminName, context.Sender?.SteamID ?? 0, null, null, $"map={matchedMap}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} changed map to {Map}", adminName, matchedMap);
    }

    public void OnWSMapCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.ChangeWSMap);

        if (!HasPermission(context, _permissions.ChangeWSMap))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["wsmap_usage"]}");
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["wsmap_available", string.Join(", ", _workshopMaps.Maps.Keys)]}");
            return;
        }

        var input = args[0];
        uint workshopId;

        // Try to parse as workshop ID
        if (!uint.TryParse(input, out workshopId))
        {
            // Try to find by name
            var matchedMap = _workshopMaps.Maps.FirstOrDefault(m => 
                m.Key.Contains(input, StringComparison.OrdinalIgnoreCase));

            if (matchedMap.Key == null)
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["wsmap_not_found", input]}");
                return;
            }

            workshopId = matchedMap.Value;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        const float changeDelaySeconds = 3f;

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["wsmap_changing", adminName, workshopId, changeDelaySeconds]}");
        }

        _core.Scheduler.DelayBySeconds(changeDelaySeconds, () =>
        {
            _core.Engine.ExecuteCommand($"ds_workshop_changelevel {workshopId}");
            _core.Scheduler.DelayBySeconds(0.25f, () =>
            {
                _core.Engine.ExecuteCommand($"host_workshop_map {workshopId}");
            });
        });

        _ = _adminLogManager.AddLogAsync("wsmap", adminName, context.Sender?.SteamID ?? 0, null, null, $"workshop_id={workshopId}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} changed to workshop map {WorkshopId}", adminName, workshopId);
    }

    public void OnRestartCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.RestartGame);

        if (!HasPermission(context, _permissions.RestartGame))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        int seconds = 2;
        if (args.Length >= 1 && int.TryParse(args[0], out var parsed) && parsed > 0)
        {
            seconds = parsed;
        }

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["restart_notification", adminName, seconds]}");
        }

        _core.Engine.ExecuteCommand($"mp_restartgame {seconds}");

        _ = _adminLogManager.AddLogAsync("restart", adminName, context.Sender?.SteamID ?? 0, null, null, $"seconds={seconds}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} restarted the game in {Seconds} second(s)", adminName, seconds);
    }

    public void OnHeadshotOnCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.HeadshotMode))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        _core.Engine.ExecuteCommand("mp_damage_headshot_only 1");
        BroadcastToAll(PluginLocalizer.Get(_core)["headshot_enabled", adminName]);
        _ = _adminLogManager.AddLogAsync("hson", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_damage_headshot_only=1");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} enabled headshot-only mode", adminName);
    }

    public void OnHeadshotOffCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.HeadshotMode))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        _core.Engine.ExecuteCommand("mp_damage_headshot_only 0");
        BroadcastToAll(PluginLocalizer.Get(_core)["headshot_disabled", adminName]);
        _ = _adminLogManager.AddLogAsync("hsoff", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_damage_headshot_only=0");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} disabled headshot-only mode", adminName);
    }

    public void OnBunnyOnCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.BunnyHop))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        _core.Engine.ExecuteCommand("sv_enablebunnyhopping 1");
        _core.Engine.ExecuteCommand("sv_autobunnyhopping 1");
        BroadcastToAll(PluginLocalizer.Get(_core)["bunny_enabled", adminName]);
        _ = _adminLogManager.AddLogAsync("bunnyon", adminName, context.Sender?.SteamID ?? 0, null, null, "sv_enablebunnyhopping=1;sv_autobunnyhopping=1");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} enabled bunny hop", adminName);
    }

    public void OnBunnyOffCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.BunnyHop))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        _core.Engine.ExecuteCommand("sv_enablebunnyhopping 0");
        _core.Engine.ExecuteCommand("sv_autobunnyhopping 0");
        BroadcastToAll(PluginLocalizer.Get(_core)["bunny_disabled", adminName]);
        _ = _adminLogManager.AddLogAsync("bunnyoff", adminName, context.Sender?.SteamID ?? 0, null, null, "sv_enablebunnyhopping=0;sv_autobunnyhopping=0");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} disabled bunny hop", adminName);
    }

    public void OnRespawnOnCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.RespawnMode))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        _core.Engine.ExecuteCommand("mp_respawn_on_death_ct 1");
        _core.Engine.ExecuteCommand("mp_respawn_on_death_t 1");
        BroadcastToAll(PluginLocalizer.Get(_core)["respawn_mode_enabled", adminName]);
        _ = _adminLogManager.AddLogAsync("respawnon", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_respawn_on_death_ct=1;mp_respawn_on_death_t=1");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} enabled instant respawn mode", adminName);
    }

    public void OnRespawnOffCommand(ICommandContext context)
    {
        if (!HasPermission(context, _permissions.RespawnMode))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        _core.Engine.ExecuteCommand("mp_respawn_on_death_ct 0");
        _core.Engine.ExecuteCommand("mp_respawn_on_death_t 0");
        BroadcastToAll(PluginLocalizer.Get(_core)["respawn_mode_disabled", adminName]);
        _ = _adminLogManager.AddLogAsync("respawnoff", adminName, context.Sender?.SteamID ?? 0, null, null, "mp_respawn_on_death_ct=0;mp_respawn_on_death_t=0");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} disabled instant respawn mode", adminName);
    }

    public void OnRconCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Rcon);

        if (!HasPermission(context, _permissions.Rcon))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rcon_usage"]}");
            return;
        }

        var command = string.Join(" ", args);
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        _core.Engine.ExecuteCommand(command);

        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            if (_core.Permission.PlayerHasPermission(player.SteamID, _permissions.Rcon) ||
                _core.Permission.PlayerHasPermission(player.SteamID, _permissions.AdminRoot))
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["rcon_executed", adminName, command]}");
            }
        }

        _ = _adminLogManager.AddLogAsync("rcon", adminName, context.Sender?.SteamID ?? 0, null, null, $"command={command}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} executed rcon: {Command}", adminName, command);
    }

    public void OnCvarCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Cvar);

        if (!HasPermission(context, _permissions.Cvar))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cvar_usage"]}");
            return;
        }

        var cvarName = args[0];
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];

        if (args.Length == 1)
        {
            // Just query the value
            var cvar = _core.ConVar.Find<string>(cvarName);
            if (cvar == null)
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cvar_not_found", cvarName]}");
                return;
            }

            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cvar_value", cvarName, cvar.Value]}");
        }
        else
        {
            // Set the value
            var value = string.Join(" ", args.Skip(1));
            _core.Engine.ExecuteCommand($"{cvarName} {value}");

            foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                if (_core.Permission.PlayerHasPermission(player.SteamID, _permissions.Cvar) ||
                    _core.Permission.PlayerHasPermission(player.SteamID, _permissions.AdminRoot))
                {
                    player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cvar_set", adminName, cvarName, value]}");
                }
            }

            _ = _adminLogManager.AddLogAsync("cvar", adminName, context.Sender?.SteamID ?? 0, null, null, $"cvar={cvarName};value={value}");
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} set {Cvar} to {Value}", adminName, cvarName, value);
        }
    }

    public void OnVoteCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Vote);

        if (!HasPermission(context, _permissions.Vote))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 3)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_usage"]}");
            return;
        }

        lock (_voteLock)
        {
            if (_activeVote != null && _activeVote.EndsAtUtc > DateTime.UtcNow)
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_already_running"]}");
                return;
            }
        }

        var question = args[0].Trim();
        var answers = args.Skip(1)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (answers.Count < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_usage"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var vote = new ActiveVoteState
        {
            Question = question,
            Answers = answers,
            VotesBySteamId = new Dictionary<ulong, int>(),
            EndsAtUtc = DateTime.UtcNow.AddSeconds(30),
            StartedBy = adminName,
            StartedBySteamId = adminSteamId
        };

        lock (_voteLock)
        {
            _activeVote = vote;
        }

        var menu = BuildVoteMenu(vote);
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
        {
            _core.MenusAPI.OpenMenuForPlayer(player, menu);
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_started", question]}");
        }

        _core.Scheduler.DelayBySeconds(30f, FinalizeVote);

        _ = _adminLogManager.AddLogAsync("vote", adminName, adminSteamId, null, null, $"question={question};answers={string.Join("|", answers)}");
    }

    private IMenuAPI BuildVoteMenu(ActiveVoteState vote)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["vote_menu_title", vote.Question]);

        for (var i = 0; i < vote.Answers.Count; i++)
        {
            var answerIndex = i;
            var text = $"{i + 1}. {vote.Answers[i]}";
            var option = new ButtonMenuOption(text) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var player = args.Player;
                lock (_voteLock)
                {
                    if (_activeVote == null || _activeVote.EndsAtUtc <= DateTime.UtcNow)
                    {
                        return ValueTask.CompletedTask;
                    }

                    _activeVote.VotesBySteamId[player.SteamID] = answerIndex;
                }

                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_received", vote.Answers[answerIndex]]}");
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private void FinalizeVote()
    {
        ActiveVoteState? vote;
        lock (_voteLock)
        {
            vote = _activeVote;
            _activeVote = null;
        }

        if (vote == null)
        {
            return;
        }

        var counts = new int[vote.Answers.Count];
        foreach (var (_, answerIndex) in vote.VotesBySteamId)
        {
            if (answerIndex >= 0 && answerIndex < counts.Length)
            {
                counts[answerIndex]++;
            }
        }

        var winnerIndex = 0;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[winnerIndex])
            {
                winnerIndex = i;
            }
        }

        var totalVotes = vote.VotesBySteamId.Count;
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_result_header", vote.Question, totalVotes]}");
            for (var i = 0; i < vote.Answers.Count; i++)
            {
                player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_result_line", i + 1, vote.Answers[i], counts[i]]}");
            }

            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["vote_result_winner", vote.Answers[winnerIndex], counts[winnerIndex]]}");
        }

        _ = _adminLogManager.AddLogAsync("vote_result", vote.StartedBy, vote.StartedBySteamId, null, null, $"question={vote.Question};winner={vote.Answers[winnerIndex]};votes={counts[winnerIndex]};total={totalVotes}");
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
    }

    private void BroadcastToAll(string message)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {message}");
        }
    }
}


