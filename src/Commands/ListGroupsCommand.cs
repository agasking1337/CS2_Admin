using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class ListGroupsCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly GroupDbManager _groupDbManager;

    public ListGroupsCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        GroupDbManager groupDbManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
        _groupDbManager = groupDbManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.ListGroups))
            {
                Reply(context, "no_permission");
                return;
            }

            var groups = await _groupDbManager.GetAllGroupsAsync();
            await OnMainThreadAsync(() =>
            {
                if (groups.Count == 0)
                {
                    Reply(context, "listgroups_none");
                    return;
                }

                Reply(context, "listgroups_header", groups.Count);
                foreach (var group in groups)
                {
                    ReplyRaw(context, L("listgroups_entry", group.Name, group.Immunity, group.Flags));
                }
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "ListGroups command failed");
        }
    }
}
