using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class WhoCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public WhoCommand(
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
            if (!HasPerm(context, Permissions.Who))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.Who);
            var target = string.Join(' ', args).Trim();
            var matchedPlayers = string.IsNullOrWhiteSpace(target)
                ? Core.PlayerManager.GetAllPlayers().Where(player => player.IsValid && !player.IsFakeClient).ToList()
                : PlayerUtils.FindPlayersByTarget(Core, target, caller: context.Sender)
                    .Where(player => !player.IsFakeClient)
                    .ToList();

            if (matchedPlayers.Count == 0)
            {
                Reply(context, string.IsNullOrWhiteSpace(target) ? "who_none" : "player_not_found");
                return;
            }

            var players = matchedPlayers
                .OrderBy(player => player.PlayerID)
                .Select(player => new
                {
                    player.PlayerID,
                    player.SteamID,
                    Name = player.Controller.PlayerName
                })
                .ToList();

            var entries = await Task.WhenAll(players.Select(async player => new
            {
                Player = player,
                Admin = await _adminDbManager.GetAdminAsync(player.SteamID)
            }));

            Reply(context, "who_header", entries.Length);
            foreach (var entry in entries)
            {
                var groups = entry.Admin?.IsActive == true && !string.IsNullOrWhiteSpace(entry.Admin.Groups)
                    ? entry.Admin.Groups
                    : L("who_player");
                Reply(context, "who_entry", entry.Player.PlayerID, entry.Player.Name, entry.Player.SteamID, groups);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Who command failed");
        }
    }
}
