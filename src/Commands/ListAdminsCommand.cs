using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class ListAdminsCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public ListAdminsCommand(
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
            // No permission check — everyone can see online admins.

            var onlineAdmins = Core.PlayerManager.GetAllPlayers()
                .Where(p => p.IsValid && !p.IsFakeClient)
                .Select(p => new { Player = p, Admin = _adminDbManager.GetAdminFromCache(p.SteamID) })
                .Where(x => x.Admin != null && x.Admin.IsActive)
                .ToList();

            if (onlineAdmins.Count == 0)
            {
                Reply(context, "listadmins_none");
                return;
            }

            Reply(context, "listadmins_header", onlineAdmins.Count);
            foreach (var info in onlineAdmins)
            {
                var admin = info.Admin!;
                var player = info.Player;
                var groupLabel = string.IsNullOrWhiteSpace(admin.Groups) ? "-" : admin.Groups;

                // Show: Name - Group (no SteamID or immunity for regular players)
                ReplyRaw(context, $" \x04{player.Controller.PlayerName}\x01 - \x0B{groupLabel}");
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "ListAdmins command failed");
        }
    }
}
