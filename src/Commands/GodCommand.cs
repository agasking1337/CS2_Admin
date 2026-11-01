using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class GodCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _godPlayers = new();

    public GodCommand(
        ISwiftlyCore core,
        AdminDbManager adminDbManager,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService) : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override void Execute(ICommandContext context)
    {
        RunTargetedFunCommand(context, CommandsConfig.God, Permissions.God, "god_usage", _adminDbManager, "God",
            onMainThread: (ctx, args, targets, adminName) =>
            {
                foreach (var target in targets)
                {
                    var pawn = target.PlayerPawn;
                    if (pawn?.IsValid != true) continue;

                    var isGod = _godPlayers.Contains(target.PlayerID);
                    if (isGod)
                    {
                        pawn.TakesDamage = true;
                        pawn.TakesDamageUpdated();
                        _godPlayers.Remove(target.PlayerID);
                    }
                    else
                    {
                        pawn.TakesDamage = false;
                        pawn.TakesDamageUpdated();
                        _godPlayers.Add(target.PlayerID);
                    }

                    var stateLabel = !isGod ? L("god_enabled") : L("god_disabled");
                    var targetName = target.Controller.PlayerName;

                    PlayerUtils.SendNotification(Core, target, Messages,
                        $"<font color='#9b59b6'><b>{L("god_personal_html", stateLabel)}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("god_personal_chat", stateLabel, ResolveVisibleAdminName(target, adminName))}");
                }

                foreach (var target in targets)
                {
                    var stateLabel = _godPlayers.Contains(target.PlayerID) ? L("god_enabled") : L("god_disabled");
                    var targetName = target.Controller.PlayerName;
                    foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                        player.SendChat($" \x02{L("prefix")}\x01 {L("god_notification", visibleAdmin, targetName, stateLabel)}");
                    }
                }

                foreach (var target in targets)
                {
                    var stateLabel = _godPlayers.Contains(target.PlayerID) ? L("god_enabled") : L("god_disabled");
                    _ = AdminLogManager.AddLogAsync("godmode", adminName, ctx.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"enabled={stateLabel == L("god_enabled")}", target.Controller.PlayerName);
                }
            });
    }
}

