using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public class WarnCommand : CommandBase
{
    private readonly WarnManager _warnManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discordBotService;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly SanctionMenuConfig _sanctions;

    public WarnCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        DiscordBotService discordBotService,
        PlayerSanctionStateService sanctionStateService,
        SanctionMenuConfig sanctions)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _warnManager = warnManager;
        _adminDbManager = adminDbManager;
        _discordBotService = discordBotService;
        _sanctionStateService = sanctionStateService;
        _sanctions = sanctions;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Warn);
            if (!HasPerm(context, Permissions.Warn))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "warn_usage");
                return;
            }

            var reason = string.Join(" ", args.Skip(1)).Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                Reply(context, "warn_usage");
                return;
            }

            const int duration = -1;
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;
            
            string targetName;
            ulong targetSteamId;
            string? targetIp = null;
            int targetPlayerId = 0;

            if (PlayerUtils.TryParseSteamId(args[0], out var steamId))
            {
                targetSteamId = steamId;
                targetName = L("unknown");
            }
            else
            {
                var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
                if (target == null)
                {
                    Reply(context, "player_not_found");
                    return;
                }
                
                targetSteamId = target.SteamID;
                targetName = target.Controller.PlayerName ?? L("unknown");
                targetIp = target.IPAddress;
                targetPlayerId = target.PlayerID;
            }

            if (!await ValidateCanPunishAsync(context, targetSteamId))
                return;

            _warnManager.SetAdminContext(adminName, adminSteamId);
            _warnManager.InvalidateCache(targetSteamId);
            var ok = await _warnManager.AddWarnAsync(targetSteamId, duration, reason);
            if (!ok)
            {
                await OnMainThreadAsync(() => Reply(context, "warn_db_error"));
                return;
            }

            await _sanctionStateService.RefreshAsync(targetSteamId, targetIp);

            await OnMainThreadAsync(() =>
            {
                ReplyRaw(context, SafeLocalize("warn_sent_admin", "Warning sent successfully."));
                var onlineTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (onlineTarget != null)
                {
                    PlayerUtils.SendNotification(Core, 
                        onlineTarget,
                        Messages,
                        $"<font color='#ffd700'><b>{L("warned_personal_html")}</b></font><br><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                        $" \x02{L("prefix")}\x01 {L("warned_personal_chat", reason)}");
                }
            });

            await AdminLogManager.AddLogAsync("warn", adminName, adminSteamId, targetSteamId, targetIp, $"duration={duration};reason={reason}", targetName, targetPlayerId, reason);

            Core.Logger.LogInformationIfEnabled("{Admin} warned {Target} for {Duration} minutes. Reason: {Reason}",
                adminName, targetName, duration, reason);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Warn command failed");
        }
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }

    private string SafeLocalize(string key, string fallback)
    {
        try
        {
            return L(key);
        }
        catch
        {
            return fallback;
        }
    }

    private void OpenWarnTargetMenu(IPlayer reporter)
    {
        Core.MenusAPI.OpenMenuForPlayer(reporter, BuildWarnTargetMenu(reporter));
    }

    private SwiftlyS2.Shared.Menus.IMenuAPI BuildWarnTargetMenu(IPlayer reporter)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.Design.SetMenuTitle(SafeLocalize("menu_select_player", "Select Player"));

        var onlinePlayers = Core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid && !p.IsFakeClient)
            .OrderBy(p => p.Controller.PlayerName)
            .ToList();

        if (onlinePlayers.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(SafeLocalize("menu_no_players", "No players found")) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
            return builder.Build();
        }

        foreach (var target in onlinePlayers)
        {
            var targetName = target.Controller.PlayerName ?? SafeLocalize("unknown", "Unknown");
            var optionText = $"{targetName} (#{target.PlayerID})";
            var snapshot = new WarnTarget(target.SteamID, targetName);

            var option = new ButtonMenuOption(optionText) { CloseAfterClick = true };
            option.Click += (_, args) =>
            {
                OpenWarnReasonMenu(args.Player, snapshot);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(option);
        }

        return builder.Build();
    }

    private void OpenWarnReasonMenu(IPlayer reporter, WarnTarget target)
    {
        var builder = Core.MenusAPI.CreateBuilder();
        builder.BindToParent(BuildWarnTargetMenu(reporter));
        builder.Design.SetMenuTitle(SafeLocalize("menu_select_reason", "Select Reason"));

        var reasons = _sanctions.Reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reasons.Count == 0)
        {
            reasons =
            [
                L("warn_reason_hacking"),
                L("other")
            ];
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

        Core.MenusAPI.OpenMenuForPlayer(reporter, builder.Build());
    }

    private async Task SendMenuWarnAsync(IPlayer admin, WarnTarget target, string reason)
    {
        const int duration = -1;
        var contextLike = new WarnExecutionContext(
            admin.Controller.PlayerName ?? L("console_name"),
            admin.SteamID,
            target.Name,
            target.SteamId);

        if (!await TryApplyWarnFromMenuAsync(contextLike, duration, reason))
        {
            return;
        }

    }

    private async Task<bool> TryApplyWarnFromMenuAsync(WarnExecutionContext execution, int duration, string reason)
    {
        _warnManager.SetAdminContext(execution.AdminName, execution.AdminSteamId);
        var ok = await _warnManager.AddWarnAsync(execution.TargetSteamId, duration, reason);
        if (!ok)
            return false;

        await OnMainThreadAsync(() =>
        {
            var onlineTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == execution.TargetSteamId);
            if (onlineTarget != null)
            {
                PlayerUtils.SendNotification(Core, 
                    onlineTarget,
                    Messages,
                    $"<font color='#ffd700'><b>{L("warned_personal_html")}</b></font><br><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                    $" \x02{L("prefix")}\x01 {L("warned_personal_chat", reason)}");
            }
        });

        await AdminLogManager.AddLogAsync("warn", execution.AdminName, execution.AdminSteamId, execution.TargetSteamId, null, $"duration={duration};reason={reason};source=menu", execution.TargetName);
        return true;
    }

    private readonly record struct WarnTarget(ulong SteamId, string Name);
    private readonly record struct WarnExecutionContext(string AdminName, ulong AdminSteamId, string TargetName, ulong TargetSteamId);
}

