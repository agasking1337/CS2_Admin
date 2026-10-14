using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using System.Net;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class UnbanCommand : BanCommandBase
{
    public UnbanCommand(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        PlayerIpDbManager playerIpDbManager,
        PlayerSessionManager playerSessionManager,
        RecentPlayersTracker recentPlayersTracker,
        DiscordBotService discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messages,
        SanctionMenuConfig sanctions,
        MultiServerConfig multiServerConfig,
        int banType,
        PlayerSanctionStateService sanctionStateService,
        PermissionService permissionService)
        : base(core, banManager, muteManager, gagManager, warnManager, adminDbManager, adminLogManager,
            playerIpDbManager, playerSessionManager, recentPlayersTracker, discord, permissions, commands,
            tags, messages, sanctions, multiServerConfig, banType, sanctionStateService, permissionService)
    {
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Unban);

            if (!HasPerm(context, Permissions.Ban))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "unban_usage");
                return;
            }

            var reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : L("no_reason");
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            _banManager.SetAdminContext(adminName, adminSteamId);
            var success = false;
            ulong? targetSteamId = null;
            string? targetIp = null;
            var affectedRows = 0;
            var targetArg = args[0].Trim();

            if (PlayerUtils.TryParseSteamId(targetArg, out var steamId))
            {
                var result = await UnbanSteamAndKnownIpsAsync(steamId, reason, adminName, adminSteamId);
                affectedRows += result.AffectedRows;
                targetSteamId = steamId;
                targetIp = result.KnownIps;

                success = affectedRows > 0;
                Core.Logger.LogInformationIfEnabled("[Debug] unban requested steamid={SteamId} affected={AffectedRows}", steamId, affectedRows);
                if (success)
                {
                    await _sanctionStateService.RefreshAsync(steamId, null);
                }
            }
            else if (TryNormalizeIpTarget(targetArg, out var normalizedIp))
            {
                affectedRows = await _banManager.UnbanIpWithCountAsync(normalizedIp, reason);
                success = affectedRows > 0;
                targetIp = normalizedIp;
                Core.Logger.LogInformationIfEnabled("[Debug] unban requested ip={Ip} affected={AffectedRows}", normalizedIp, affectedRows);
            }
            else
            {
                var matches = await _banManager.FindActiveSteamBanTargetsByNameAsync(targetArg, 5);
                if (matches.Count == 0)
                {
                        ReplyRaw(context, T("unban_name_not_found", "No active banned player matched '{0}'.", targetArg));
                    return;
                }

                if (matches.Count > 1)
                {
                    var hint = string.Join(", ", matches.Select(m => $"{m.TargetName} ({m.SteamId})"));
                        ReplyRaw(context, T("unban_name_ambiguous", "Multiple banned players matched '{0}'. Use SteamID. Matches: {1}", targetArg, hint));
                    return;
                }

                var selected = matches[0];
                var result = await UnbanSteamAndKnownIpsAsync(selected.SteamId, reason, adminName, adminSteamId);
                affectedRows += result.AffectedRows;
                targetSteamId = selected.SteamId;
                targetIp = result.KnownIps;
                success = affectedRows > 0;

                Core.Logger.LogInformationIfEnabled(
                    "[CS2_Admin][Debug] unban requested by-name target={Target} resolvedSteamId={SteamId} affected={AffectedRows}",
                    targetArg,
                    selected.SteamId,
                    affectedRows);
            }

            await OnMainThreadAsync(() =>
            {
                if (!success)
                {
                    ReplyRaw(context, T("unban_failed", "Unban failed. No active ban matched."));
                    return;
                }

                if (targetSteamId.HasValue)
                {
                    Reply(context, "unbanned_success", targetSteamId.Value, reason);
                    return;
                }

                ReplyRaw(context, T("unbanned_ip_success", "Unbanned IP {0}. Reason: {1}", targetIp ?? "-", reason));
            });

            if (success)
            {
                await AdminLogManager.AddLogAsync("unban", adminName, adminSteamId, targetSteamId, targetIp, $"reason={reason}", null, null, reason);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Unban command failed");
        }
    }

    private async Task<(int AffectedRows, string? KnownIps)> UnbanSteamAndKnownIpsAsync(ulong steamId, string reason, string adminName, ulong adminSteamId)
    {
        var affectedRows = await _banManager.UnbanSteamByIdWithCountAsync(steamId, reason);
        var knownIps = await _playerIpDbManager.GetAllKnownIpsAsync(steamId);
        foreach (var knownIp in knownIps)
        {
            _banManager.SetAdminContext(adminName, adminSteamId);
            affectedRows += await _banManager.UnbanIpWithCountAsync(knownIp, reason);
        }

        return (affectedRows, knownIps.Count > 0 ? string.Join(",", knownIps) : null);
    }

}
