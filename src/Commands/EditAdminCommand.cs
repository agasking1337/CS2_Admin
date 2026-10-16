using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class EditAdminCommand : CommandBase
{
    public EditAdminCommand(
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
            if (!HasPerm(context, Permissions.EditAdmin))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.EditAdmin);

            if (args.Length < 3)
            {
                Reply(context, "editadmin_usage");
                return;
            }

            if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
            {
                Reply(context, "invalid_steamid");
                return;
            }

            var field = args[1].ToLowerInvariant();
            var value = string.Join(" ", args.Skip(2));
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            if (field == "flags")
            {
                Reply(context, "editadmin_groups_only");
                return;
            }

            if (field == "groups")
            {
                if (!TryParseGroupsArgument(value, out var normalizedGroups, out _))
                {
                    Reply(context, "editadmin_usage");
                    return;
                }

                value = normalizedGroups;
            }

            var existingAdmin = await AdminDbManager.GetAdminAsync(targetSteamId);
            var success = await AdminDbManager.EditAdminAsync(targetSteamId, field, value);
            await OnMainThreadAsync(() => Reply(context, success ? "editadmin_success" : "editadmin_failed"));

            if (success)
            {
                await TryAutoReloadAsync();
                await ApplyTagToOnlinePlayerAsync(targetSteamId);
                await AdminLogManager.AddLogAsync("editadmin", adminName, adminSteamId, targetSteamId, null, $"{field}={value}", existingAdmin?.Name);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "EditAdmin command failed");
        }
    }

    private async Task ApplyTagToOnlinePlayerAsync(ulong steamId)
    {
        if (!Tags.Enabled)
            return;

        var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
        if (player == null)
            return;

        var admin = await AdminDbManager.GetAdminAsync(steamId) ?? (await AdminDbManager.GetAllAdminsAsync()).FirstOrDefault(a => a.SteamId == steamId && a.IsActive);

        string tag;
        if (admin != null && admin.IsActive)
        {
            tag = GroupDbManager.GetPrimaryGroupNameSync(admin.GroupList) ?? admin.GroupList.FirstOrDefault() ?? "";
        }
        else if (PermissionService.HasPermission(steamId, Permissions.AdminRoot))
        {
            tag = "";
        }
        else
        {
            tag = Tags.PlayerTag;
        }

        if (ChatTagConfigManager.Config.ScoreboardEnabled &&
            ChatTagConfigManager.Config.Groups.ContainsKey(tag))
            PlayerUtils.SetScoreTagReliable(Core, player.PlayerID, tag);
    }
}
