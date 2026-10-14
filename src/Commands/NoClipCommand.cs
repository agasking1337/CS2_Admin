using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

using CS2_Admin.Services;
using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class NoClipCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _noclipPlayers = new();

    public NoClipCommand(
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

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.NoClip);

            if (!HasPerm(context, Permissions.NoClip))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "noclip_usage");
                return;
            }

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;

            bool isEnabled = _noclipPlayers.Contains(target.PlayerID);

            if (isEnabled)
            {
                PlayerUtils.SetNoclip(Core, target, false);
                _noclipPlayers.Remove(target.PlayerID);
            }
            else
            {
                PlayerUtils.SetNoclip(Core, target, true);
                _noclipPlayers.Add(target.PlayerID);
            }

            var stateLabel = !isEnabled ? L("noclip_on") : L("noclip_off");

            var senderIsTarget = context.Sender != null && context.Sender.SteamID == target.SteamID;
            if (!senderIsTarget)
            {
                PlayerUtils.SendNotification(Core, target, Messages,
                    $"<font color='#00ccff'><b>{L("noclip_toggled_personal_html", stateLabel)}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                    $" \x02{L("prefix")}\x01 {L("noclip_toggled_personal_chat", stateLabel, ResolveVisibleAdminName(target, adminName))}");
            }

            BroadcastNotification(adminName, "noclip_toggled_notification", targetName, stateLabel);

            _ = AdminLogManager.AddLogAsync("noclip", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"state={(!isEnabled ? "on" : "off")}", target.Controller.PlayerName);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "NoClip command failed");
        }
    }
}

