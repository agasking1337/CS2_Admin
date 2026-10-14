using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public sealed class UngagCommand : CommandBase
{
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discord;
    private readonly PlayerSanctionStateService _sanctionStateService;
    private readonly string _gagPermission;

    public UngagCommand(
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
        string gagPermission)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _muteManager = muteManager;
        _gagManager = gagManager;
        _adminDbManager = adminDbManager;
        _discord = discord;
        _sanctionStateService = sanctionStateService;
        _gagPermission = gagPermission;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Ungag);

            if (!HasPerm(context, _gagPermission))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "ungag_usage");
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

            _gagManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                    continue;

                var existingGag = await _gagManager.GetActiveGagAsync(target.SteamId);
                if (existingGag == null)
                {
                    await OnMainThreadAsync(() => Reply(context, "player_not_gagged", target.Name));
                    continue;
                }

                await _gagManager.UngagAsync(target.SteamId, reason);
                await _sanctionStateService.RefreshAsync(target.SteamId, target.IpAddress);

                await OnMainThreadAsync(() =>
                {
                    BroadcastNotification(adminName, "ungagged_notification", target.Name, reason);
                    var targetPlayer = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        PlayerUtils.SendNotification(Core, targetPlayer, Messages,
                            $"<font color='#00ff00'><b>{L("ungagged_personal_html")}</b></font><br><br>{L("label_reason")}: <font color='#ffffff'>{reason}</font>",
                            $" \x02{L("prefix")}\x01 {L("ungagged_personal_chat", reason)}");
                    }
                });

                Core.Logger.LogInformationIfEnabled("{Admin} ungagged {Target}. Reason: {Reason}",
                    adminName, target.Name, reason);
                await AdminLogManager.AddLogAsync("ungag", adminName, adminSteamId, target.SteamId, target.IpAddress, $"reason={reason}", target.Name, target.PlayerId, reason);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Ungag command failed");
        }
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        return await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, targetSteamId);
    }

}


