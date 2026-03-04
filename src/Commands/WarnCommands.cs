using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class WarnCommands
{
    private readonly ISwiftlyCore _core;
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly DiscordWebhook _discord;
    private readonly string _warnPermission;
    private readonly string _unwarnPermission;
    private readonly string _adminRootPermission;
    private readonly MessagesConfig _messagesConfig;
    private readonly SanctionMenuConfig _sanctions;
    private readonly IReadOnlyList<string> _warnAliases;
    private readonly IReadOnlyList<string> _unwarnAliases;

    public WarnCommands(
        ISwiftlyCore core,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        DiscordWebhook discord,
        string warnPermission,
        string unwarnPermission,
        string adminRootPermission,
        MessagesConfig messagesConfig,
        SanctionMenuConfig sanctions,
        IReadOnlyList<string> warnAliases,
        IReadOnlyList<string> unwarnAliases)
    {
        _core = core;
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _adminLogManager = adminLogManager;
        _discord = discord;
        _warnPermission = warnPermission;
        _unwarnPermission = unwarnPermission;
        _adminRootPermission = adminRootPermission;
        _messagesConfig = messagesConfig;
        _sanctions = sanctions;
        _warnAliases = warnAliases;
        _unwarnAliases = unwarnAliases;
    }

    public void OnWarnCommand(ICommandContext context)
    {
        if (!HasPermission(context, _warnPermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _warnAliases);
        if (args.Length == 0)
        {
            if (context.IsSentByPlayer && context.Sender != null)
            {
                OpenWarnTargetMenu(context.Sender);
                return;
            }

            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warn_usage"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warn_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        var reason = string.Join(" ", args.Skip(1)).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warn_usage"]}");
            return;
        }

        const int duration = -1;

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetName = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
        var targetSteamId = target.SteamID;
        var targetIp = target.IPAddress;

        _ = Task.Run(async () =>
        {
            if (!await ValidateCanPunishAsync(context, targetSteamId))
            {
                return;
            }

            _warnManager.SetAdminContext(adminName, adminSteamId);
            var ok = await _warnManager.AddWarnAsync(targetSteamId, duration, reason);
            if (!ok)
            {
                _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warn_failed"]}"));
                return;
            }

            _core.Scheduler.NextTick(() =>
            {
                var warnedLine = GetLocalizedSafe(
                    "warned_notification",
                    "Warned {0}. Reason: {1}",
                    targetName,
                    reason);
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {warnedLine}");

                var onlineTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (onlineTarget != null)
                {
                    PlayerUtils.SendNotification(
                        onlineTarget,
                        _messagesConfig,
                        PluginLocalizer.Get(_core)["warned_personal_html", reason],
                        $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warned_personal_chat", reason]}");
                }
            });

            await _discord.SendWarnNotificationAsync(adminName, targetName, duration, reason);
            await _adminLogManager.AddLogAsync("warn", adminName, adminSteamId, targetSteamId, targetIp, $"duration={duration};reason={reason}", targetName);

            _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} warned {Target} for {Duration} minutes. Reason: {Reason}",
                adminName, targetName, duration, reason);
        });
    }

    public void OnUnwarnCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _unwarnAliases);

        if (!HasPermission(context, _unwarnPermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unwarn_usage"]}");
            return;
        }

        var target = PlayerUtils.FindPlayerByTarget(_core, args[0]);
        if (target == null)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        var reason = args.Length > 1
            ? string.Join(" ", args.Skip(1))
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetName = target.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"];
        var targetSteamId = target.SteamID;
        var targetIp = target.IPAddress;

        _ = Task.Run(async () =>
        {
            if (!await ValidateCanPunishAsync(context, targetSteamId))
            {
                return;
            }

            _warnManager.SetAdminContext(adminName, adminSteamId);
            var ok = await _warnManager.UnwarnAsync(targetSteamId, reason);
            if (!ok)
            {
                _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_warned", targetName]}"));
                return;
            }

            _core.Scheduler.NextTick(() =>
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unwarned_notification", targetName, reason]}");

                var onlineTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (onlineTarget != null)
                {
                    PlayerUtils.SendNotification(
                        onlineTarget,
                        _messagesConfig,
                        PluginLocalizer.Get(_core)["unwarned_personal_html", reason],
                        $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unwarned_personal_chat", reason]}");
                }
            });

            await _adminLogManager.AddLogAsync("unwarn", adminName, adminSteamId, targetSteamId, targetIp, $"reason={reason}", targetName);
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} removed warn from {Target}. Reason: {Reason}",
                adminName, targetName, reason);
        });
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        if (!context.IsSentByPlayer || context.Sender == null)
        {
            return true;
        }

        var adminImm = await _adminDbManager.GetEffectiveImmunityAsync(context.Sender.SteamID);
        var targetImm = await _adminDbManager.GetEffectiveImmunityAsync(targetSteamId);
        if (targetImm >= adminImm && targetImm > 0)
        {
            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_immunity"]}"));
            return false;
        }

        return true;
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
        {
            return true;
        }

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _adminRootPermission);
    }

    private string GetLocalizedSafe(string key, string fallbackTemplate, params object[] args)
    {
        try
        {
            return PluginLocalizer.Get(_core)[key, args];
        }
        catch (FormatException)
        {
            return string.Format(fallbackTemplate, args);
        }
    }

    private void OpenWarnTargetMenu(IPlayer reporter)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(SafeLocalize("menu_select_player", "Select Player"));

        var onlinePlayers = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid && !p.IsFakeClient)
            .OrderBy(p => p.Controller.PlayerName)
            .ToList();

        if (onlinePlayers.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(SafeLocalize("menu_no_players", "No players found")) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
            _core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
            return;
        }

        foreach (var target in onlinePlayers)
        {
            var targetName = target.Controller.PlayerName ?? SafeLocalize("unknown", "Unknown");
            var optionText = $"{targetName} (#{target.PlayerID})";
            var snapshot = new WarnTarget(target.SteamID, targetName);

            var option = new ButtonMenuOption(optionText) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                _core.Scheduler.NextTick(() => OpenWarnReasonMenu(args.Player, snapshot));
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
    }

    private void OpenWarnReasonMenu(IPlayer reporter, WarnTarget target)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(SafeLocalize("menu_select_reason", "Select Reason"));

        var reasons = _sanctions.Reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reasons.Count == 0)
        {
            reasons = ["Hacking", "Other"];
        }

        foreach (var reason in reasons)
        {
            var selectedReason = reason;
            var option = new ButtonMenuOption(selectedReason) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                _ = SendMenuWarnAsync(args.Player, target, selectedReason);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        _core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
    }
    private async Task SendMenuWarnAsync(IPlayer admin, WarnTarget target, string reason)
    {
        const int duration = -1;
        var contextLike = new WarnExecutionContext(
            admin.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"],
            admin.SteamID,
            target.Name,
            target.SteamId);

        if (!await TryApplyWarnFromMenuAsync(contextLike, duration, reason))
        {
            _core.Scheduler.NextTick(() =>
                admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warn_failed"]}"));
            return;
        }

        _core.Scheduler.NextTick(() =>
            admin.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {SafeLocalize("warn_sent", "Warn sent.")}"));
    }

    private async Task<bool> TryApplyWarnFromMenuAsync(WarnExecutionContext execution, int duration, string reason)
    {
        _warnManager.SetAdminContext(execution.AdminName, execution.AdminSteamId);
        var ok = await _warnManager.AddWarnAsync(execution.TargetSteamId, duration, reason);
        if (!ok)
        {
            return false;
        }

        var onlineTarget = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == execution.TargetSteamId);
        if (onlineTarget != null)
        {
            _core.Scheduler.NextTick(() =>
            {
                PlayerUtils.SendNotification(
                    onlineTarget,
                    _messagesConfig,
                    PluginLocalizer.Get(_core)["warned_personal_html", reason],
                    $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["warned_personal_chat", reason]}");
            });
        }

        await _discord.SendWarnNotificationAsync(execution.AdminName, execution.TargetName, duration, reason);
        await _adminLogManager.AddLogAsync("warn", execution.AdminName, execution.AdminSteamId, execution.TargetSteamId, null, $"duration={duration};reason={reason};source=menu", execution.TargetName);
        return true;
    }

    private string SafeLocalize(string key, string fallback)
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

    private readonly record struct WarnTarget(ulong SteamId, string Name);
    private readonly record struct WarnExecutionContext(string AdminName, ulong AdminSteamId, string TargetName, ulong TargetSteamId);
}


