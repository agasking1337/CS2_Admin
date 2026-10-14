using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class TeamCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public TeamCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.ChangeTeam);

            if (!HasPerm(context, Permissions.ChangeTeam))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(context, "team_usage");
                return;
            }

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var canTarget = await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true);
            if (!canTarget)
                return;

            var team = PlayerUtils.ParseTeam(args[1]);
            if (team == null)
            {
                // await sonrası thread pool'dayız; Reply main thread ister.
                Core.Scheduler.NextTick(() => Reply(context, "invalid_team"));
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var targetName = target.Controller.PlayerName;
            var targetSteamId = target.SteamID;
            var prefix = L("prefix");
            var teamName = PlayerUtils.GetTeamName((int)team.Value, PluginLocalizer.Get(Core));

            // await sonrası ana thread'de değiliz; ChangeTeam/SendChat gibi native çağrılar
            // SADECE ana thread'den yapılabilir. Oyuncu bu arada ayrılmış olabileceğinden
            // canlı referansı yeniden alıp IsValid kontrolü yapıyoruz.
            Core.Scheduler.NextTick(() =>
            {
                var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                if (liveTarget?.IsValid != true) return;

                liveTarget.ChangeTeam(team.Value);

                PlayerUtils.SendNotification(Core, liveTarget, Messages,
                    $"<font color='#00ccff'><b>{L("team_changed_personal_html")}</b></font><br><br>{L("label_new_team")}: <font color='#00ff00'>{teamName}</font><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(liveTarget, adminName)}</font>",
                    $" \x02{prefix}\x01 {L("team_changed_personal_chat", teamName, ResolveVisibleAdminName(liveTarget, adminName))}");

                foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                {
                    var visibleAdmin = ResolveVisibleAdminName(player, adminName);
                    player.SendChat($" \x02{prefix}\x01 {L("team_changed_notification", visibleAdmin, targetName, teamName)}");
                }

                _ = AdminLogManager.AddLogAsync("team", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"team={teamName}", targetName);
                Core.Logger.LogInformationIfEnabled("{Admin} moved {Target} to {Team}", adminName, targetName, teamName);
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Team command failed");
        }
    }
}
