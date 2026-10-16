using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Commands;

public class MoneyCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public MoneyCommand(
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
            var args = NormalizeArgs(context.Args, CommandsConfig.Money);

            if (!HasPerm(context, Permissions.Money))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 2 || !int.TryParse(args[1], out var amount))
            {
                Reply(context, "money_usage");
                return;
            }

            amount = Math.Clamp(amount, 0, 999999);

            var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
            if (target == null)
            {
                Reply(context, "player_not_found");
                return;
            }

            var canTarget = await PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true);
            if (!canTarget)
                return;

                // await sonrası ana thread'de değiliz; InGameMoneyServices ataması gibi native
                // çağrılar SADECE ana thread'den yapılabilir.
                Core.Scheduler.NextTick(() =>
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true) return;

                    if (liveTarget.Controller?.InGameMoneyServices == null)
                    {
                        Reply(context, "player_not_found");
                        return;
                    }

                    try
                    {
                        liveTarget.Controller.InGameMoneyServices.Account = amount;
                        liveTarget.Controller.InGameMoneyServices.AccountUpdated();
                        liveTarget.Controller.InGameMoneyServicesUpdated();
                    }
                    catch
                    {
                        try
                        {
                            liveTarget.Controller.InGameMoneyServices.Account = amount;
                        }
                        catch (Exception ex)
                        {
                            Core.Logger.LogErrorIfEnabled(ex, "Money set reflection fallback failed for {SteamId}", liveTarget.SteamID);
                        }
                    }

                    var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
                    var targetName = liveTarget.Controller.PlayerName;

                    BroadcastNotification(adminName, "money_set_notification", targetName, amount);

                    PlayerUtils.SendNotification(Core, liveTarget, Messages,
                        $"<font color='#00ff00'><b>{L("money_personal_html", amount)}</b></font>",
                        $" \x02{L("prefix")}\x01 {L("money_personal_chat", amount)}");

                    _ = AdminLogManager.AddLogAsync("money", adminName, context.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"amount={amount}", targetName);
                    Core.Logger.LogInformation("{Admin} set money of {Target} to {Amount}", adminName, targetName, amount);
                });
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Money command failed");
        }
    }
}
