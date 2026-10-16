using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public sealed class MuteCommand : CommandBase
{
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discord;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly string _mutePermission;

    public MuteCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Mute);

            if (!HasPerm(context, _mutePermission))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "mute_usage");
                return;
            }

            if (RejectGroupTargets(context, args))
                return;

            if (!SanctionDurationParser.TryParseToMinutes(args[1], out int duration))
            {
                Reply(context, "invalid_duration");
                return;
            }

            var reason = args.Length > 2
                ? string.Join(" ", args.Skip(2))
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

                var existingMute = await _muteManager.GetActiveMuteFreshAsync(target.SteamId);
                if (existingMute != null)
                {
                    await OnMainThreadAsync(() => Reply(context, "player_already_muted", target.Name));
                    continue;
                }

                _muteManager.InvalidateCache(target.SteamId);
                var muteOk = await _muteManager.AddMuteAsync(target.SteamId, duration, reason);
                if (!muteOk)
                {
                    await OnMainThreadAsync(() => Reply(context, "mute_db_error"));
                    continue;
                }

                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);
                Core.Logger.LogInformationIfEnabled("[Debug] mute apply steamid={SteamId} duration={Duration} reason={Reason}", target.SteamId, duration, reason);
                var durationText = duration <= 0 ? L("duration_permanently") : L("duration_for_minutes", duration);

                await OnMainThreadAsync(() =>
                {
                    BroadcastNotification(adminName, "muted_notification", target.Name, durationText, reason);
                    var targetPlayer = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        var durationDisplay = duration <= 0 ? L("permanent") : L("duration_minutes", duration);
                        PlayerUtils.SendNotification(Core, targetPlayer, Messages,
                            $"<font color='#ff6600'><b>{L("muted_personal_html")}</b></font><br><br>{L("label_duration")}: <font color='#ffcc00'>{durationDisplay}</font><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                            $" \x02{L("prefix")}\x01 {L("muted_personal_chat", durationText, reason)}");
                        targetPlayer.VoiceFlags = VoiceFlagValue.Muted;
                    }
                });

                await AdminLogManager.AddLogAsync("mute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name, target.PlayerId, reason);

                Core.Logger.LogInformationIfEnabled("{Admin} muted {Target} for {Duration} minutes. Reason: {Reason}",
                    adminName, target.Name, duration, reason);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Mute command failed");
            Reply(context, "internal_error");
        }
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }

}


