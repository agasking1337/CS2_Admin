using CS2_Admin.Menu;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using CS2_Admin.Config;
using CS2_Admin.Database;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class AdminMenuCommand : CommandBase
{
    private readonly AdminMenuManager _menuManager;
    private readonly Dictionary<ulong, DateTime> _lastMenuOpenByPlayer = new();
    private readonly object _menuOpenLock = new();

    public AdminMenuCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminMenuManager menuManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _menuManager = menuManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (context.Sender == null)
            {
                ReplyRaw(context, "admin <addadmin|editadmin|removeadmin|listadmins|addgroup|editgroup|removegroup|listgroups|adminreload>");
                return;
            }

            if (!HasPerm(context, Permissions.AdminMenu))
            {
                Reply(context, "no_permission");
                return;
            }

            OpenAdminMenuDebounced(context.Sender);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "AdminMenu command failed");
        }
    }

    private void OpenAdminMenuDebounced(IPlayer sender)
    {
        if (sender == null || !sender.IsValid)
            return;

        var now = DateTime.UtcNow;
        var shouldOpen = true;

        lock (_menuOpenLock)
        {
            if (_lastMenuOpenByPlayer.TryGetValue(sender.SteamID, out var lastOpen)
                && (now - lastOpen).TotalMilliseconds < 500)
            {
                shouldOpen = false;
            }
            else
            {
                _lastMenuOpenByPlayer[sender.SteamID] = now;
            }
        }

        if (shouldOpen)
        {
            _menuManager.OpenAdminMenu(sender);
        }
    }
}
