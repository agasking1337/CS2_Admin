using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class MapCommand : CommandBase
{
    private readonly GameMapsConfig _gameMaps;
    private readonly WorkshopMapsConfig _workshopMaps;

    public MapCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        GameMapsConfig gameMaps,
        WorkshopMapsConfig workshopMaps)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _gameMaps = gameMaps;
        _workshopMaps = workshopMaps;
    }



    public override void Execute(ICommandContext context)
    {
        

        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.ChangeMap);

            if (!HasPerm(context, Permissions.ChangeMap))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "map_usage");
                ReplyRaw(context, L("map_available", string.Join(", ", _gameMaps.Maps.Keys)));
                return;
            }

            var mapName = args[0].ToLowerInvariant();

            var matchedMap = _gameMaps.Maps.Keys.FirstOrDefault(m =>
                m.Equals(mapName, StringComparison.OrdinalIgnoreCase));

            if (matchedMap == null)
            {
                Reply(context, "map_not_found", mapName);
                return;
            }

            if (!Core.Engine.IsMapValid(matchedMap))
            {
                Reply(context, "map_not_found", matchedMap);
                Core.Logger.LogWarningIfEnabled("Refused map change because engine rejected map {Map}", matchedMap);
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            var mapDisplayName = _gameMaps.Maps[matchedMap];
            const float changeDelaySeconds = 3f;

            BroadcastNotification(adminName, "map_changing", mapDisplayName, changeDelaySeconds);

            Core.Scheduler.DelayBySeconds(changeDelaySeconds, () =>
            {
                if (matchedMap.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = matchedMap.Split('/');
                    if (parts.Length > 1)
                    {
                        Core.Engine.ExecuteCommand($"host_workshop_map {parts[1]}");
                    }
                    else
                    {
                        Core.Engine.ExecuteCommand($"changelevel {matchedMap}");
                    }
                }
                else
                {
                    Core.Engine.ExecuteCommand($"changelevel {matchedMap}");
                }
            });

            _ = AdminLogManager.AddLogAsync("map", adminName, context.Sender?.SteamID ?? 0, null, null, $"map={matchedMap}");
            Core.Logger.LogInformationIfEnabled("{Admin} changed map to {Map}", adminName, matchedMap);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Map command failed");
        }
    }
}
