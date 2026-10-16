using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class UnrenameCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerNameHistoryManager _playerNameHistoryManager;

    public UnrenameCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        PlayerNameHistoryManager playerNameHistoryManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
        _playerNameHistoryManager = playerNameHistoryManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Unrename);

            if (!HasPerm(context, Permissions.Unrename))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "unrename_usage");
                return;
            }

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var canTarget = await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true);
            if (!canTarget)
                return;

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;

            var originalName = await _playerNameHistoryManager.GetOriginalNameAsync(target.SteamID);
            if (string.IsNullOrWhiteSpace(originalName))
            {
                // await sonrası thread pool'dayız; ReplyRaw main thread ister.
                Core.Scheduler.NextTick(() => ReplyRaw(context, L("unrename_no_history")));
                return;
            }

            await _playerNameHistoryManager.DeleteCustomNameAsync(target.SteamID);

            // await sonrası ana thread'de değiliz; Controller.PlayerName ataması gibi native
            // çağrılar SADECE ana thread'den yapılabilir. Ayrıca oyuncu bu iki await arasında
            // ayrılmış olabileceğinden canlı referansı yeniden alıp IsValid kontrolü yapıyoruz.
            Core.Scheduler.NextTick(() =>
            {
                var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                if (liveTarget?.IsValid != true) return;

                liveTarget.Controller.PlayerName = originalName;
                liveTarget.Controller.PlayerNameUpdated();

                BroadcastNotification(adminName, "unrename_notification", targetName, originalName);

                PlayerUtils.SendNotification(Core, liveTarget, Messages,
                    $"<font color='#00ff00'><b>{L("unrename_personal_html")}</b></font><br><br>{L("label_original_name")}: <font color='#00ff00'>{originalName}</font><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(liveTarget, adminName)}</font>",
                    $" \x02{L("prefix")}\x01 {L("unrename_personal_chat", originalName, ResolveVisibleAdminName(liveTarget, adminName))}");

                _ = AdminLogManager.AddLogAsync("unrename", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"restored_name={originalName}", targetName);
                Core.Logger.LogInformationIfEnabled("{Admin} restored name of {Target} to {OriginalName}", adminName, targetName, originalName);
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Unrename command failed");
        }
    }
}
