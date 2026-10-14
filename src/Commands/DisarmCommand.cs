using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using System.Drawing;

namespace CS2_Admin.Commands;

public class DisarmCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public DisarmCommand(
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
        RunTargetedFunCommand(context, CommandsConfig.Disarm, Permissions.Disarm, "disarm_usage", _adminDbManager, "Disarm",
            includeDeadPlayers: false,
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var changed = 0;
                foreach (var target in targets)
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true) continue;

                    var itemServices = liveTarget.PlayerPawn?.ItemServices;
                    if (itemServices?.IsValid == true)
                    {
                        itemServices.RemoveItems();
                        changed++;
                    }
                }

                if (changed == 0) return;

                foreach (var disTarget in targets)
                {
                    var liveDis = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == disTarget.SteamID);
                    if (liveDis?.IsValid != true) continue;
                    PlayerUtils.SendNotification(Core, liveDis, Messages,
                        $"<font color='#c0392b'><b>{L("disarm_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffd700'>{ResolveVisibleAdminName(liveDis, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("disarm_personal_chat", ResolveVisibleAdminName(liveDis, adminName))}");
                }

                if (changed > 0)
                    BroadcastNotification(adminName, "disarm_notification", FormatTargetName(targets));

                _ = AdminLogManager.AddLogAsync("disarm", adminName, ctx.Sender?.SteamID ?? 0, null, null, $"targets={changed}");
                Core.Logger.LogInformationIfEnabled("{Admin} disarmed {Count} player(s)", adminName, changed);
            });
    }
}

