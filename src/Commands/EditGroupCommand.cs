using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class EditGroupCommand : CommandBase
{
    public EditGroupCommand(
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
            if (!HasPerm(context, Permissions.EditGroup))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.EditGroup);

            if (args.Length < 2)
            {
                Reply(context, "editgroup_usage");
                return;
            }

            var name = args[0];
            var flags = args[1];
            var immunity = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : 0;
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            var success = await GroupDbManager.AddOrUpdateGroupAsync(name, flags, immunity);
            await OnMainThreadAsync(() => Reply(context, success ? "editgroup_success" : "editgroup_failed"));

            if (success)
            {
                await TryAutoReloadAsync();
                await AdminLogManager.AddLogAsync("editgroup", adminName, adminSteamId, null, null, $"name={name};flags={flags};immunity={immunity}");
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "EditGroup command failed");
        }
    }
}
