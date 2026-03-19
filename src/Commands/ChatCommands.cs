using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class ChatCommands
{
    private readonly ISwiftlyCore _core;
    private readonly AdminLogManager _adminLogManager;
    private readonly DiscordWebhook _discord;
    private readonly PermissionsConfig _permissions;
    private readonly MessagesConfig _messages;
    private readonly CommandsConfig _commands;
    private readonly SanctionMenuConfig _sanctions;

    private static readonly TimeSpan ReportSameTargetCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReportDifferentTargetCooldown = TimeSpan.FromMinutes(2);
    private readonly object _reportCooldownLock = new();
    private readonly Dictionary<ulong, ReporterLastReport> _reporterLastReports = new();
    private readonly Dictionary<ReportPairKey, DateTime> _reporterTargetLastReports = new();

    public ChatCommands(
        ISwiftlyCore core,
        AdminLogManager adminLogManager,
        DiscordWebhook discord,
        PermissionsConfig permissions,
        MessagesConfig messages,
        CommandsConfig commands,
        SanctionMenuConfig sanctions)
    {
        _core = core;
        _adminLogManager = adminLogManager;
        _discord = discord;
        _permissions = permissions;
        _messages = messages;
        _commands = commands;
        _sanctions = sanctions;
    }

    public void OnAsayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Asay);

        if (!HasPermission(context, _permissions.Asay))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["asay_usage"]}");
            return;
        }

        var messageText = string.Join(" ", args);
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var prefix = PluginLocalizer.Get(_core)["asay_prefix"];
        var msg = $" \x04{prefix}\x01 \x10{adminName}\x01: {messageText}";

        int notified = 0;
        foreach (var p in GetOnlineAdmins(_permissions.Asay))
        {
            notified++;
            p.SendChat(msg);
        }

        _ = _adminLogManager.AddLogAsync("asay", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] ASAY from {Admin} delivered to {Count} admins: {Message}", adminName, notified, messageText);
    }

    public void OnSayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Say);

        if (!HasPermission(context, _permissions.Say))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["say_usage"]}");
            return;
        }

        var messageText = string.Join(" ", args);
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var prefix = PluginLocalizer.Get(_core)["say_prefix"];
        var msg = $" \x04{prefix}\x01 \x10{adminName}\x01: {messageText}";

        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            p.SendChat(msg);
        }

        _ = _adminLogManager.AddLogAsync("say", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] SAY from {Admin}: {Message}", adminName, messageText);
    }

    public void OnPsayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Psay);

        if (!HasPermission(context, _permissions.Psay))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["psay_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null || !target.IsValid)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        var messageText = string.Join(" ", args.Skip(1));
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var prefix = PluginLocalizer.Get(_core)["psay_prefix"];

        target.SendChat($" \x04{prefix}\x01 \x10{adminName}\x01: {messageText}");
        context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["psay_sent", target.Controller.PlayerName]}");

        _ = _adminLogManager.AddLogAsync("psay", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"message={messageText}", target.Controller.PlayerName);
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] PSAY from {Admin} to {Target}: {Message}", adminName, target.Controller.PlayerName, messageText);
    }

    public void OnCsayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Csay);

        if (!HasPermission(context, _permissions.Csay))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["csay_usage"]}");
            return;
        }

        var messageText = string.Join(" ", args);
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var htmlPrefix = PluginLocalizer.Get(_core)["csay_html_prefix"];
        var chatPrefix = PluginLocalizer.Get(_core)["csay_prefix"];

        var html = $"{htmlPrefix} <font color='#ffcc00'>{adminName}</font><br><font color='#ffffff'>{messageText}</font>";
        var chat = $" \x04{chatPrefix}\x01 \x10{adminName}\x01: {messageText}";

        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            PlayerUtils.SendNotification(p, _messages, html, chat);
        }

        _ = _adminLogManager.AddLogAsync("csay", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] CSAY from {Admin}: {Message}", adminName, messageText);
    }

    public void OnHsayCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Hsay);

        if (!HasPermission(context, _permissions.Hsay))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["hsay_usage"]}");
            return;
        }

        var messageText = string.Join(" ", args);
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var htmlPrefix = PluginLocalizer.Get(_core)["hsay_html_prefix"];
        var chatPrefix = PluginLocalizer.Get(_core)["hsay_prefix"];

        var html = $"{htmlPrefix} <font color='#ffcc00'>{adminName}</font><br><font color='#ffffff'>{messageText}</font>";
        var chat = $" \x04{chatPrefix}\x01 \x10{adminName}\x01: {messageText}";

        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            PlayerUtils.SendNotification(p, _messages, html, chat);
        }

        _ = _adminLogManager.AddLogAsync("hsay", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
        _core.Logger.LogInformationIfEnabled("[CS2_Admin] HSAY from {Admin}: {Message}", adminName, messageText);
    }

    public void OnCallAdminCommand(ICommandContext context)
    {
        if (!context.IsSentByPlayer || context.Sender == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_only_command"]}");
            return;
        }

        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.CallAdmin);
        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["calladmin_usage"]}");
            return;
        }

        var messageText = string.Join(" ", args);
        var playerName = context.Sender.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
        var playerSteamId = context.Sender.SteamID;
        var serverId = ServerIdentity.GetServerId(_core);

        _ = _discord.SendCallAdminNotificationAsync(playerName, playerSteamId, messageText, serverId);
        _ = _adminLogManager.AddLogAsync("calladmin", playerName, playerSteamId, null, context.Sender.IPAddress, $"message={messageText};server={serverId}");

        context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["calladmin_sent"]}");
    }

    public void OnReportCommand(ICommandContext context)
    {
        if (!HasPermissionOrOpen(context, _permissions.Report))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (!context.IsSentByPlayer || context.Sender == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_only_command"]}");
            return;
        }

        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Report);
        if (args.Length < 1)
        {
            OpenReportTargetMenu(context.Sender);
            return;
        }

        var messageText = string.Join(" ", args);
        var playerName = context.Sender.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
        var playerSteamId = context.Sender.SteamID;
        var serverId = ServerIdentity.GetServerId(_core);

        ulong targetSteamId = 0;
        if (args.Length >= 1)
        {
            var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
            if (target != null && target.IsValid && !target.IsFakeClient)
            {
                targetSteamId = target.SteamID;
            }
        }

        if (targetSteamId != 0)
        {
            if (!TryConsumeReportCooldown(playerSteamId, targetSteamId, out var remaining, out var sameTarget))
            {
                var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
                var waitText = seconds <= 0 ? "a moment" : $"{seconds}s";
                var reasonText = sameTarget
                    ? $"You must wait {waitText} before reporting this player again."
                    : $"You must wait {waitText} before reporting another player.";
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {reasonText}");
                return;
            }
        }

        if (!_discord.HasReportWebhookConfigured)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 Discord report webhook is not configured.");
            return;
        }

        _ = _discord.SendReportNotificationAsync(playerName, playerSteamId, messageText, serverId);
        _ = _adminLogManager.AddLogAsync("report", playerName, playerSteamId, null, context.Sender.IPAddress, $"message={messageText};server={serverId}");

        context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["report_sent"]}");
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
    }

    private bool HasPermissionOrOpen(ICommandContext context, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return true;

        return HasPermission(context, permission);
    }

    private IEnumerable<IPlayer> GetOnlineAdmins(string permission)
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
        {
            if (_core.Permission.PlayerHasPermission(p.SteamID, permission) ||
                _core.Permission.PlayerHasPermission(p.SteamID, _permissions.AdminRoot))
                yield return p;
        }
    }

    private void OpenReportTargetMenu(IPlayer reporter)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_player_report", "Select Player To Report"));

        var onlinePlayers = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid && !p.IsFakeClient)
            .OrderBy(p => p.Controller.PlayerName)
            .ToList();

        if (onlinePlayers.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(T("menu_no_players", "No players found")) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
            _core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
            return;
        }

        foreach (var target in onlinePlayers)
        {
            var targetName = target.Controller.PlayerName ?? $"Player {target.PlayerID}";
            var optionText = $"{targetName} (#{target.PlayerID})";
            var snapshot = new ReportTarget(target.SteamID, targetName);

            var option = new ButtonMenuOption(optionText) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                _core.Scheduler.NextTick(() => OpenReportReasonMenu(args.Player, snapshot));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
    }

    private void OpenReportReasonMenu(IPlayer reporter, ReportTarget target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(T("menu_select_reason", "Select Reason"));

        var reasons = _sanctions.Reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reasons.Count == 0)
        {
            reasons = ["Cheating", "Toxic behavior", "Griefing"];
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

        _core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
    }

    private async Task SendMenuReportAsync(IPlayer reporter, ReportTarget target, string reason)
    {
        var playerName = reporter.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
        var playerSteamId = reporter.SteamID;
        var serverId = ServerIdentity.GetServerId(_core);
        var messageText = $"Target: {target.Name} ({target.SteamId}) | Reason: {reason}";

        if (target.SteamId != 0)
        {
            if (!TryConsumeReportCooldown(playerSteamId, target.SteamId, out var remaining, out var sameTarget))
            {
                var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
                var waitText = seconds <= 0 ? "a moment" : $"{seconds}s";
                var reasonText = sameTarget
                    ? $"You must wait {waitText} before reporting this player again."
                    : $"You must wait {waitText} before reporting another player.";
                _core.Scheduler.NextTick(() =>
                {
                    reporter.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {reasonText}");
                });
                return;
            }
        }

        await _discord.SendReportNotificationAsync(playerName, playerSteamId, messageText, serverId);
        await _adminLogManager.AddLogAsync(
            "report",
            playerName,
            playerSteamId,
            target.SteamId,
            null,
            $"target={target.Name};reason={reason};server={serverId};source=menu",
            target.Name);

        _core.Scheduler.NextTick(() =>
        {
            reporter.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["report_sent"]}");
        });
    }

    private string T(string key, string fallback)
    {
        try
        {
            return PluginLocalizer.Get(_core)[key];
        }
        catch
        {
            return fallback;
        }
    }

    private bool TryConsumeReportCooldown(ulong reporterSteamId, ulong targetSteamId, out TimeSpan remaining, out bool sameTarget)
    {
        remaining = TimeSpan.Zero;
        sameTarget = false;

        var now = DateTime.UtcNow;
        lock (_reportCooldownLock)
        {
            var pairKey = new ReportPairKey(reporterSteamId, targetSteamId);
            if (_reporterTargetLastReports.TryGetValue(pairKey, out var lastSameTargetAt))
            {
                var elapsed = now - lastSameTargetAt;
                if (elapsed < ReportSameTargetCooldown)
                {
                    sameTarget = true;
                    remaining = ReportSameTargetCooldown - elapsed;
                    return false;
                }
            }

            if (_reporterLastReports.TryGetValue(reporterSteamId, out var lastReport))
            {
                if (lastReport.TargetSteamId != 0 && lastReport.TargetSteamId != targetSteamId)
                {
                    var elapsed = now - lastReport.ReportedAtUtc;
                    if (elapsed < ReportDifferentTargetCooldown)
                    {
                        sameTarget = false;
                        remaining = ReportDifferentTargetCooldown - elapsed;
                        return false;
                    }
                }
            }

            _reporterLastReports[reporterSteamId] = new ReporterLastReport(now, targetSteamId);
            _reporterTargetLastReports[pairKey] = now;
            return true;
        }
    }

    private readonly record struct ReportTarget(ulong SteamId, string Name);
    private readonly record struct ReportPairKey(ulong ReporterSteamId, ulong TargetSteamId);
    private readonly record struct ReporterLastReport(DateTime ReportedAtUtc, ulong TargetSteamId);
}


