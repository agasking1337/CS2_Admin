using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class RespawnCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public RespawnCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override void Execute(ICommandContext context)
    {
        RunTargetedFunCommand(context, CommandsConfig.Respawn, Permissions.Respawn, "respawn_usage", _adminDbManager, "Respawn",
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var prefix = L("prefix");

                foreach (var target in targets)
                {
                    if (target.Controller.TeamNum >= 2)
                    {
                        target.Respawn();
                    }
                }

                foreach (var target in targets)
                {
                    PlayerUtils.SendNotification(Core, target, Messages,
                        $"<font color='#00ff00'><b>{L("respawned_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                        $" \x02{prefix}\x01 {L("respawned_personal_chat", ResolveVisibleAdminName(target, adminName))}");
                }

                if (targets.Count == 1)
                {
                    var targetName = targets[0].Controller.PlayerName;
                    foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                        player.SendChat($" \x02{prefix}\x01 {L("respawned_notification_single", visibleAdmin, targetName)}");
                    }
                }
                else
                {
                    foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                        player.SendChat($" \x02{prefix}\x01 {L("respawned_notification_multiple", visibleAdmin, targets.Count)}");
                    }
                }

                var targetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
                _ = AdminLogManager.AddLogAsync("respawn", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={targetSteamIds};count={targets.Count}");
                Core.Logger.LogInformationIfEnabled("{Admin} respawned {Count} player(s)", adminName, targets.Count);
            });
    }
}

