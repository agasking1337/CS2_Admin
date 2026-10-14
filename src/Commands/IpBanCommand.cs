using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using System.Net;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace CS2_Admin.Commands;

public class IpBanCommand : BanCommandBase
{
    public IpBanCommand(
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
            await HandleOnlineBan(context, true);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "IpBan command failed");
        }
    }

    private async Task HandleOnlineBan(ICommandContext context, bool ipMode)
    {
        var args = NormalizeArgs(context.Args, ipMode ? CommandsConfig.IpBan : CommandsConfig.Ban);

        if (!HasPerm(context, Permissions.Ban))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2)
        {
            Reply(context, ipMode ? "ipban_usage" : "ban_usage");
            return;
        }

        var targetArg = args[0];
        if (!SanctionDurationParser.TryParseToMinutes(args[1], out var duration))
        {
            Reply(context, "invalid_duration");
            return;
        }

        var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : L("no_reason");
        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var isGlobal = ResolveGlobalMode();
        var applyMode = ResolveBanApplyMode(ipMode);
        var shouldBanSteam = (applyMode & BanApplyMode.Steam) != 0;
        var shouldBanIp = (applyMode & BanApplyMode.Ip) != 0;
        var resolvedTarget = PlayerUtils.FindPlayerByTarget(Core, targetArg);
        var targetSnapshot = resolvedTarget == null
            ? null
            : new OnlineTargetSnapshot(
                resolvedTarget.PlayerID,
                resolvedTarget.SteamID,
                resolvedTarget.Controller.PlayerName ?? L("unknown"),
                resolvedTarget.IPAddress);

        if (targetSnapshot != null)
        {
            if (shouldBanIp && string.IsNullOrWhiteSpace(targetSnapshot.IpAddress))
            {
                Reply(context, "lastban_no_ip");
                return;
            }

            if (!await ValidateCanPunish(context, targetSnapshot.SteamId))
            {
                return;
            }

            _banManager.SetAdminContext(adminName, adminSteamId);

            var existingSteam = shouldBanSteam ? await _banManager.GetActiveBanFreshAsync(targetSnapshot.SteamId, null, _multiServerConfig.Enabled) : null;
            var existingIp = shouldBanIp ? await _banManager.GetActiveBanFreshAsync(0, targetSnapshot.IpAddress, _multiServerConfig.Enabled) : null;
            if (existingSteam != null || existingIp != null)
            {
                await OnMainThreadAsync(() => Reply(context, "player_already_banned", targetSnapshot.Name));
                return;
            }
            var added = shouldBanSteam && shouldBanIp
                ? await _banManager.AddCombinedBanAsync(targetSnapshot.SteamId, targetSnapshot.Name, targetSnapshot.IpAddress!, duration, reason, isGlobal)
                : shouldBanSteam
                    ? await _banManager.AddBanAsync(targetSnapshot.SteamId, targetSnapshot.Name, duration, reason, isGlobal, targetSnapshot.IpAddress)
                    : await _banManager.AddIpBanAsync(targetSnapshot.IpAddress!, targetSnapshot.Name, duration, reason, isGlobal, targetSnapshot.SteamId);
            if (!added) return;

            _ = _sanctionStateService.RefreshAsync(targetSnapshot.SteamId, targetSnapshot.IpAddress);

            var durationText = duration <= 0 ? L("duration_permanently") : L("duration_for_minutes", duration);
            await OnMainThreadAsync(() =>
            {
                BroadcastNotification(adminName, "banned_notification", targetSnapshot.Name, durationText, reason);

                var onlineTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSnapshot.SteamId);
                if (onlineTarget != null)
                {
                    var durationDisplay = duration <= 0 ? L("permanent") : L("duration_minutes", duration);
                    PlayerUtils.SendNotification(Core, 
                        onlineTarget,
                        Messages,
                        $"<font color='#ff0000'><b>{L("banned_personal_html")}</b></font><br><br>{L("label_duration")}: <font color='#ffcc00'>{durationDisplay}</font><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                        $" \x02{L("prefix")}\x01 {L("banned_personal_chat", durationText, reason)}");

                    var kickDelaySeconds = Messages.BanKickDelaySeconds > 0
                        ? Messages.BanKickDelaySeconds
                        : Math.Max(1f, Messages.CenterHtmlDurationMs / 1000f);

                    Core.Scheduler.DelayBySeconds(kickDelaySeconds, () =>
                    {
                        var playerToKick = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSnapshot.SteamId);
                        playerToKick?.Kick($"Banned: {reason}", ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
                    });
                }
            });

            var actionName = shouldBanSteam && shouldBanIp
                ? "ban_both"
                : shouldBanIp ? "ipban" : "ban";
            _ = AdminLogManager.AddLogAsync(
                actionName,
                adminName,
                adminSteamId,
                targetSnapshot.SteamId,
                targetSnapshot.IpAddress,
                $"duration={duration};global={isGlobal};reason={reason};ban_type={_banType}",
                targetSnapshot.Name,
                targetSnapshot.PlayerId,
                reason);
            return;
        }

        if (!ipMode && PlayerUtils.TryParseSteamId(targetArg, out var offlineSteamId))
        {
            if (!shouldBanSteam && shouldBanIp)
            {
                ReplyRaw(context, T("ban_type_requires_ip_target", "BanMode is IP-only. Use target IP with !ban/!ipban."));
                return;
            }

            await AddOfflineSteamBanAsync(context, offlineSteamId, duration, reason, adminName, adminSteamId, isGlobal);

            if (shouldBanIp)
            {
                var knownIps = await _playerIpDbManager.GetAllKnownIpsAsync(offlineSteamId);
                var appliedCount = 0;
                foreach (var knownIp in knownIps.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var added = await AddOfflineIpBanAsync(context, knownIp, duration, reason, adminName, adminSteamId, isGlobal, notifyResult: false);
                    if (added)
                    {
                        appliedCount++;
                    }
                }

                if (appliedCount > 0)
                {
                    await OnMainThreadAsync(() => ReplyRaw(context, T("ban_type_known_ips_applied", "Applied IP bans for {0} known IP(s).", appliedCount)));
                }
            }

            return;
        }

        if (shouldBanIp && TryNormalizeIpTarget(targetArg, out var normalizedIp))
        {
            await AddOfflineIpBanAsync(context, normalizedIp, duration, reason, adminName, adminSteamId, isGlobal);
            return;
        }

        if (ipMode)
        {
            await AddOfflineIpBanAsync(context, targetArg, duration, reason, adminName, adminSteamId, isGlobal);
            return;
        }

        Reply(context, "player_not_found");
    }

    private async Task<bool> ValidateCanPunish(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }
}
