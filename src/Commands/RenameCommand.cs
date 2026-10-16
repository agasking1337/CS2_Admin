using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class RenameCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly PlayerNameHistoryManager _playerNameHistoryManager;

    public RenameCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Rename);

            if (!HasPerm(context, Permissions.Rename))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "rename_usage");
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

            var newName = string.Join(" ", args.Skip(1));
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;

            await _playerNameHistoryManager.SetCustomNameAsync(target.SteamID, newName);

            // await sonrası ana thread'de değiliz; Controller.PlayerName ataması gibi native
            // çağrılar SADECE ana thread'den yapılabilir.
            Core.Scheduler.NextTick(() =>
            {
                var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                if (liveTarget?.IsValid != true) return;

                liveTarget.Controller.PlayerName = newName;
                liveTarget.Controller.PlayerNameUpdated();

                BroadcastNotification(adminName, "rename_notification", targetName, newName);

                PlayerUtils.SendNotification(Core, liveTarget, Messages,
                    $"<font color='#ffcc00'><b>{L("rename_personal_html")}</b></font><br><br>{L("label_new_name")}: <font color='#00ff00'>{newName}</font><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(liveTarget, adminName)}</font>",
                    $" \x02{L("prefix")}\x01 {L("rename_personal_chat", newName, ResolveVisibleAdminName(liveTarget, adminName))}");

                _ = AdminLogManager.AddLogAsync("rename", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"new_name={newName}", targetName);
                Core.Logger.LogInformationIfEnabled("{Admin} renamed {Target} to {NewName}", adminName, targetName, newName);
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Rename command failed");
        }
    }
}
