using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class RemoveGroupCommand : CommandBase
{
    public RemoveGroupCommand(
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
            if (!HasPerm(context, Permissions.RemoveGroup))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.RemoveGroup);

            if (args.Length < 1)
            {
                Reply(context, "removegroup_usage");
                return;
            }

            var name = args[0];
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            var success = await GroupDbManager.RemoveGroupAsync(name);
            await OnMainThreadAsync(() => Reply(context, success ? "removegroup_success" : "removegroup_failed"));

            if (success)
            {
                await TryAutoReloadAsync();
                await AdminLogManager.AddLogAsync("removegroup", adminName, adminSteamId, null, null, $"name={name}");
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "RemoveGroup command failed");
        }
    }
}
