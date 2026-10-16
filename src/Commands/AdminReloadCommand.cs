using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class AdminReloadCommand : CommandBase
{
    public AdminReloadCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        GroupDbManager groupDbManager,
        ChatTagConfigManager chatTagConfigManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService, adminDbManager, groupDbManager, chatTagConfigManager)
    {
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.AdminReload))
            {
                Reply(context, "no_permission");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0UL;

            try
            {
                await ChatTagConfigManager.SyncWithGroupsAsync(GroupDbManager);
                ReloadPermissionsConfig();
                var onlineCount = await ReloadAdminsAndTagsAsync();

                await OnMainThreadAsync(() => Reply(context, "adminreload_success"));
                await AdminLogManager.AddLogAsync("adminreload", adminName, adminSteamId, null, null, $"online={onlineCount}");
            }
            catch (Exception ex)
            {
                Core.Logger.LogErrorIfEnabled("adminreload failed: {Message}", ex.Message);
                await OnMainThreadAsync(() => Reply(context, "adminreload_failed"));
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "AdminReload command failed");
        }
    }
}
