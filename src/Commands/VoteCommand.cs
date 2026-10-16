using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Core.Menus.OptionsBase;

using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public record ActiveVoteState
{
    public required string Question { get; init; }
    public required List<string> Answers { get; init; }
    public required Dictionary<ulong, int> VotesBySteamId { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public required string StartedBy { get; init; }
    public ulong StartedBySteamId { get; init; }
    public IMenuAPI? Menu { get; set; }
}

public class VoteCommand : CommandBase
{
    private readonly object _voteLock = new();
    private ActiveVoteState? _activeVote;

    public VoteCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
    }



    public override void Execute(ICommandContext context)
    {
        

        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Vote);

            if (!HasPerm(context, Permissions.Vote))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 3)
            {
                Reply(context, "vote_usage");
                return;
            }

            lock (_voteLock)
            {
                if (_activeVote != null && _activeVote.EndsAtUtc > DateTime.UtcNow)
                {
                    Reply(context, "vote_already_running");
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
                Reply(context, "vote_usage");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
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
            vote.Menu = menu;
            foreach (var player in Core.PlayerManager.GetAllPlayers())
            {
                Core.MenusAPI.OpenMenuForPlayer(player, menu);
                player.SendChat($" \x02{L("prefix")}\x01 {L("vote_started", question)}");
            }

            ScheduleVoteMenuRefresh(vote);
            Core.Scheduler.DelayBySeconds(30f, FinalizeVote);

            _ = AdminLogManager.AddLogAsync("vote", adminName, adminSteamId, null, null, $"question={question};answers={string.Join("|", answers)}");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Vote command failed");
        }
    }

    private IMenuAPI BuildVoteMenu(ActiveVoteState vote)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(L("vote_menu_title", vote.Question));

        for (var i = 0; i < vote.Answers.Count; i++)
        {
            var answerIndex = i;
            var text = $"{i + 1}. {vote.Answers[i]}";
            var option = new ButtonMenuOption(text) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                var player = args.Player;
                var playerId = player.PlayerID;
                lock (_voteLock)
                {
                    if (_activeVote == null || _activeVote.EndsAtUtc <= DateTime.UtcNow)
                    {
                        return ValueTask.CompletedTask;
                    }

                    _activeVote.VotesBySteamId[player.SteamID] = answerIndex;
                }

                // Menü click handler'ı thread pool'da çalışır; SendChat main thread ister.
                Core.Scheduler.NextTick(() =>
                {
                    var live = Core.PlayerManager.GetPlayer(playerId);
                    if (live?.IsValid == true)
                    {
                        live.SendChat($" \x02{L("prefix")}\x01 {L("vote_received", vote.Answers[answerIndex])}");
                    }
                });
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
        foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            player.SendChat($" \x02{L("prefix")}\x01 {L("vote_result_header", vote.Question, totalVotes)}");
            for (var i = 0; i < vote.Answers.Count; i++)
            {
                player.SendChat($" \x02{L("prefix")}\x01 {L("vote_result_line", i + 1, vote.Answers[i], counts[i])}");
            }

            player.SendChat($" \x02{L("prefix")}\x01 {L("vote_result_winner", vote.Answers[winnerIndex], counts[winnerIndex])}");
        }

        _ = AdminLogManager.AddLogAsync("vote_result", vote.StartedBy, vote.StartedBySteamId, null, null, $"question={vote.Question};winner={vote.Answers[winnerIndex]};votes={counts[winnerIndex]};total={totalVotes}");
    }

    private void ScheduleVoteMenuRefresh(ActiveVoteState vote)
    {
        Core.Scheduler.DelayBySeconds(1f, () =>
        {
            ActiveVoteState? current;
            lock (_voteLock)
            {
                current = _activeVote;
            }

            if (!ReferenceEquals(current, vote) || vote.EndsAtUtc <= DateTime.UtcNow || vote.Menu == null)
            {
                return;
            }

            foreach (var player in Core.PlayerManager.GetAllPlayers())
            {
                if (vote.VotesBySteamId.ContainsKey(player.SteamID))
                {
                    continue;
                }

                var currentMenu = Core.MenusAPI.GetCurrentMenu(player);
                if (currentMenu != vote.Menu)
                {
                    Core.MenusAPI.OpenMenuForPlayer(player, vote.Menu);
                }
            }

            ScheduleVoteMenuRefresh(vote);
        });
    }
}

