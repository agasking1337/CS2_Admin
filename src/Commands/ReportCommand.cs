using System.Collections.Concurrent;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;

namespace CS2_Admin.Commands;

public class ReportCommand : CommandBase
{
    private static readonly ConcurrentDictionary<ulong, long> _lastMenuAction = new();
    private readonly DiscordBotService _discord;
    private readonly SanctionMenuConfig _sanctions;

    public ReportCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        DiscordBotService discord,
        SanctionMenuConfig sanctions)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _discord = discord;
        _sanctions = sanctions;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!context.IsSentByPlayer || context.Sender == null)
            {
                Reply(context, "player_only_command");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.Report);
            if (args.Length == 0)
            {
                OpenReportTargetMenu(context.Sender);
                return;
            }

            var messageText = string.Join(" ", args);
            var playerName = context.Sender.Controller.PlayerName ?? L("unknown");
            var playerSteamId = context.Sender.SteamID;
            var serverId = ServerIdentity.GetServerId(Core);

            _ = _discord.SendReportNotificationAsync(playerName, playerSteamId, messageText, serverId);
            _ = AdminLogManager.AddLogAsync("report", playerName, playerSteamId, null, context.Sender.IPAddress, $"message={messageText};server={serverId}");

            Reply(context, "report_sent");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Report command failed");
        }
    }

    private void OpenReportTargetMenu(IPlayer reporter)
    {
        Core.MenusAPI.OpenMenuForPlayer(reporter, BuildReportTargetMenu(reporter));
    }

    private SwiftlyS2.Shared.Menus.IMenuAPI BuildReportTargetMenu(IPlayer reporter)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(L("menu_select_player_report"));

        var onlinePlayers = Core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid && !p.IsFakeClient)
            .OrderBy(p => p.Controller.PlayerName)
            .ToList();

        if (onlinePlayers.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(L("menu_no_players")) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
            return builder.Build();
        }

        foreach (var target in onlinePlayers)
        {
            var targetName = target.Controller.PlayerName ?? $"Player {target.PlayerID}";
            var optionText = $"{targetName} (#{target.PlayerID})";
            var snapshot = new ReportTarget(target.SteamID, targetName);

            var option = new ButtonMenuOption(optionText) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                OpenReportReasonMenu(args.Player, snapshot);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private void OpenReportReasonMenu(IPlayer reporter, ReportTarget target)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.BindToParent(BuildReportTargetMenu(reporter));
        builder.Design.SetMenuTitle(L("menu_select_reason"));

        var reasons = _sanctions.Reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reasons.Count == 0)
        {
            reasons =
            [
                L("report_reason_cheating"),
                L("report_reason_toxic"),
                L("report_reason_griefing")
            ];
        }

        foreach (var reason in reasons)
        {
            var selectedReason = reason;
            var option = new ButtonMenuOption(selectedReason) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                _ = SendMenuReportAsync(args.Player, target, selectedReason);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        Core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
    }

    private async Task SendMenuReportAsync(IPlayer reporter, ReportTarget target, string reason)
    {
        var now = Environment.TickCount64;
        if (_lastMenuAction.TryGetValue(reporter.SteamID, out var lastTime) && (now - lastTime) < 2000)
            return;
        _lastMenuAction[reporter.SteamID] = now;

        var playerName = reporter.Controller.PlayerName ?? L("unknown");
        var playerSteamId = reporter.SteamID;
        var serverId = ServerIdentity.GetServerId(Core);
        var messageText = $"Target: {target.Name} ({target.SteamId}) | Reason: {reason}";

        await _discord.SendReportNotificationAsync(playerName, playerSteamId, messageText, serverId);
        await AdminLogManager.AddLogAsync(
            "report",
            playerName,
            playerSteamId,
            target.SteamId,
            null,
            $"target={target.Name};reason={reason};server={serverId};source=menu",
            target.Name);

        // Menü click + await sonrası thread pool'dayız; SendChat main thread ister.
        Core.Scheduler.NextTick(() => reporter.SendChat($" \x02{L("prefix")}\x01 {L("report_sent")}"));
    }

    private readonly record struct ReportTarget(ulong SteamId, string Name);
}
