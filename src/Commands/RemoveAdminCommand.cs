using CS2_Admin.Database;
using CS2_Admin.Config;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class RemoveAdminCommand : CommandBase
{
    public RemoveAdminCommand(
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
            if (!HasPerm(context, Permissions.RemoveAdmin))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.RemoveAdmin);

            if (args.Length < 1)
            {
                Reply(context, "removeadmin_usage");
                return;
            }

            if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
            {
                Reply(context, "invalid_steamid");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;

            var existingAdmin = await AdminDbManager.GetAdminAsync(targetSteamId);
            var success = await AdminDbManager.RemoveAdminAsync(targetSteamId);
            await OnMainThreadAsync(() =>
            {
                Reply(context, success ? "removeadmin_success" : "removeadmin_failed", targetSteamId, targetSteamId);
                if (success)
                    NotifyOnlinePlayer(targetSteamId, L("removeadmin_revoked"));
            });

            if (success)
            {
                await TryAutoReloadAsync();
                await ApplyTagToOnlinePlayerAsync(targetSteamId);
                await AdminLogManager.AddLogAsync("removeadmin", adminName, adminSteamId, targetSteamId, null, "", existingAdmin?.Name);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "RemoveAdmin command failed");
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
