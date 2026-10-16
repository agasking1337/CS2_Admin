using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class AddAdminCommand : CommandBase
{
    public AddAdminCommand(
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
            if (!HasPerm(context, Permissions.AddAdmin))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.AddAdmin);

            if (args.Length < 3)
            {
                ReplyRaw(context, "Usage: !addadmin <steamid> <name> <#group or group1,group2> [duration_days]");
                return;
            }

            if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
            {
                Reply(context, "invalid_steamid");
                return;
            }

            var name = args[1];
            var groups = args[2];

            int? durationDays = null;
            if (args.Length > 3 && int.TryParse(args[3], out var parsedDuration) && parsedDuration > 0)
            {
                durationDays = parsedDuration;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            var maxGroupImmunity = 0;
            foreach (var groupName in groups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var group = await GroupDbManager.GetGroupAsync(groupName.Trim().TrimStart('#', '@'));
                if (group == null)
                {
                    await OnMainThreadAsync(() => Reply(context, "addadmin_group_not_found", groupName));
                    return;
                }
                maxGroupImmunity = Math.Max(maxGroupImmunity, group.Immunity);
            }

            var resolvedImmunity = maxGroupImmunity;
            var success = await AdminDbManager.AddAdminAsync(targetSteamId, name, string.Empty, resolvedImmunity, groups, adminName, adminSteamId, durationDays);
            if (!success)
            {
                await OnMainThreadAsync(() => Reply(context, "addadmin_failed"));
                return;
            }

            var effectiveFlags = await AdminDbManager.GetEffectiveFlagsAsync(targetSteamId);
            await OnMainThreadAsync(() =>
            {
                Reply(context, "addadmin_success", name, targetSteamId, string.Join(",", effectiveFlags));
                NotifyOnlinePlayer(targetSteamId, L("addadmin_granted"));
            });
            await TryAutoReloadAsync();
            await ApplyTagToOnlinePlayerAsync(targetSteamId);
            await AdminLogManager.AddLogAsync("addadmin", adminName, adminSteamId, targetSteamId, null, $"groups={groups};immunity={resolvedImmunity};duration_days={durationDays?.ToString() ?? "0"}", name);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "AddAdmin command failed");
        }
    }

    private void NotifyOnlinePlayer(ulong steamId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player != null)
            {
                player.SendChat($" \x02{L("prefix")}\x01 {message}");
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
