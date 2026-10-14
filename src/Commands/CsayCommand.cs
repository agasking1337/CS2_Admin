using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class CsayCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public CsayCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Csay);

            if (!HasPerm(context, Permissions.Csay))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "csay_usage");
                return;
            }

            var messageText = string.Join(" ", args);
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            foreach (var p in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(p, adminName);
                var html = $"[ADMIN] <font color='#ffcc00'>{visibleAdmin}</font><br><font color='#ffffff'>{messageText}</font>";
                var chat = $" \x04[CSAY]\x01 \x10{visibleAdmin}\x01: {messageText}";
                PlayerUtils.SendNotification(Core, p, Messages, html, chat);
            }

            _ = AdminLogManager.AddLogAsync("csay", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
            Core.Logger.LogInformationIfEnabled("CSAY from {Admin}: {Message}", adminName, messageText);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Csay command failed");
        }
    }
}
