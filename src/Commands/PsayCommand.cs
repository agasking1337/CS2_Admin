using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class PsayCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public PsayCommand(
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

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Psay);

            if (!HasPerm(context, Permissions.Psay))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "psay_usage");
                return;
            }

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null || !target.IsValid)
            {
                Reply(context, "player_not_found");
                return;
            }

            var messageText = string.Join(" ", args.Skip(1));
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var visibleAdmin = ResolveVisibleAdminName(target, adminName);

            target.SendChat($" \x04[PM]\x01 \x10{visibleAdmin}\x01: {messageText}");
            ReplyRaw(context, $"Message sent to {target.Controller.PlayerName}.");

            _ = AdminLogManager.AddLogAsync("psay", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"message={messageText}", target.Controller.PlayerName);
            Core.Logger.LogInformationIfEnabled("PSAY from {Admin} to {Target}: {Message}", adminName, target.Controller.PlayerName, messageText);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Psay command failed");
        }
    }
}
