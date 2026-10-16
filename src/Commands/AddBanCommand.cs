using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class AddBanCommand : BanCommandBase
{
    public AddBanCommand(
        ISwiftlyCore core,
        BanManager banManager,
        MuteManager muteManager,
        GagManager gagManager,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        PlayerIpDbManager playerIpDbManager,
        PlayerSessionManager playerSessionManager,
        RecentPlayersTracker recentPlayersTracker,
        DiscordBotService discord,
        PermissionsConfig permissions,
        CommandsConfig commands,
        TagsConfig tags,
        MessagesConfig messages,
        SanctionMenuConfig sanctions,
        MultiServerConfig multiServerConfig,
        int banType,
        PlayerSanctionStateService sanctionStateService,
        PermissionService permissionService)
        : base(core, banManager, muteManager, gagManager, warnManager, adminDbManager, adminLogManager,
            playerIpDbManager, playerSessionManager, recentPlayersTracker, discord, permissions, commands,
            tags, messages, sanctions, multiServerConfig, banType, sanctionStateService, permissionService)
    {
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.AddBan);

            if (!HasPerm(context, Permissions.AddBan))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "addban_usage");
                return;
            }

            if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
            {
                Reply(context, "invalid_steamid");
                return;
            }

            if (!SanctionDurationParser.TryParseToMinutes(args[1], out var duration))
            {
                Reply(context, "invalid_duration");
                return;
            }

            var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : L("no_reason");
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var adminSteamId = context.Sender?.SteamID ?? 0;
            var isGlobal = ResolveGlobalMode();

            await AddOfflineSteamBanAsync(context, targetSteamId, duration, reason, adminName, adminSteamId, isGlobal);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "AddBan command failed");
        }
    }

}
