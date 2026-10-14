using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public sealed class UnmuteCommand : CommandBase
{
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discord;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly string _mutePermission;

    public UnmuteCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        MuteManager muteManager,
        GagManager gagManager,
        AdminDbManager adminDbManager,
        DiscordBotService discord,
        PlayerSanctionStateService sanctionStateService,
        string mutePermission)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _muteManager = muteManager;
        _gagManager = gagManager;
        _adminDbManager = adminDbManager;
        _discord = discord;
        _sanctionStateService = sanctionStateService;
        _mutePermission = mutePermission;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Unmute);

            if (!HasPerm(context, _mutePermission))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "unmute_usage");
                return;
            }

            if (RejectGroupTargets(context, args))
                return;

            var reason = args.Length > 1
                ? string.Join(" ", args.Skip(1))
                : L("no_reason");

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;
            
            var targetSnapshots = new List<PunishTargetSnapshot>();

            if (PlayerUtils.TryParseSteamId(args[0], out var steamId))
            {
                targetSnapshots.Add(new PunishTargetSnapshot(0, steamId, L("unknown"), null));
            }
            else
            {
                var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], caller: context.Sender);
                if (targets.Count == 0)
                {
                    Reply(context, "player_not_found");
                    return;
                }

                if (!EnsureSinglePunishTarget(context, targets, args[0]))
                    return;

                targetSnapshots.AddRange(targets.Select(t => new PunishTargetSnapshot(
                    t.PlayerID,
                    t.SteamID,
                    t.Controller.PlayerName ?? L("unknown"),
                    t.IPAddress)));
            }

            _muteManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                    continue;

                var existingMute = await _muteManager.GetActiveMuteAsync(target.SteamId);
                if (existingMute == null)
                {
                    await OnMainThreadAsync(() => Reply(context, "player_not_muted", target.Name));
                    continue;
                }

                await _muteManager.UnmuteAsync(target.SteamId, reason);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);

                await OnMainThreadAsync(() =>
                {
                    BroadcastNotification(adminName, "unmuted_notification", target.Name, reason);
                    var targetPlayer = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        PlayerUtils.SendNotification(Core, targetPlayer, Messages,
                            $"<font color='#00ff00'><b>{L("unmuted_personal_html")}</b></font><br><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                            $" \x02{L("prefix")}\x01 {L("unmuted_personal_chat", reason)}");
                        targetPlayer.VoiceFlags = VoiceFlagValue.Normal;
                    }
                });

                Core.Logger.LogInformationIfEnabled("{Admin} unmuted {Target}. Reason: {Reason}",
                    adminName, target.Name, reason);
                await AdminLogManager.AddLogAsync("unmute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"reason={reason}", target.Name, target.PlayerId, reason);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Unmute command failed");
        }
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }

}


