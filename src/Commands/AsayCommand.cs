using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class AsayCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discord;

    public AsayCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        DiscordBotService discord)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
        _discord = discord;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Asay);

            if (!HasPerm(context, Permissions.Asay))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "asay_usage");
                return;
            }

            var messageText = string.Join(" ", args);
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var msg = $" \x04[AdminChat]\x01 \x10{adminName}\x01: {messageText}";

            int notified = 0;
            foreach (var p in GetOnlineAdmins(Permissions.Asay))
            {
                notified++;
                p.SendChat(msg);
            }

            _ = AdminLogManager.AddLogAsync("asay", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
            Core.Logger.LogInformationIfEnabled("ASAY from {Admin} delivered to {Count} admins: {Message}", adminName, notified, messageText);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Asay command failed");
        }
    }

    private IEnumerable<IPlayer> GetOnlineAdmins(string permission)
    {
        foreach (var p in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
        {
            if (PermissionService.HasPermission(p.SteamID, permission))
                yield return p;
        }
    }
}
