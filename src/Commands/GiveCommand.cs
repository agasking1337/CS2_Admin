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

public class GiveCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public GiveCommand(
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

    public override void Execute(ICommandContext context)
    {
        RunTargetedFunCommand(context, CommandsConfig.Give, Permissions.Give, "give_usage", _adminDbManager, "Give",
            minArgs: 2,
            notFoundKey: "player_not_found",
            onMainThread: (ctx, args, targets, adminName) =>
            {
                var itemName = ResolveWeaponName(string.Join(" ", args.Skip(1)));

                var givenCount = 0;
                foreach (var target in targets)
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamID);
                    if (liveTarget?.IsValid != true) continue;

                    var pawn = liveTarget.PlayerPawn;
                    if (pawn?.IsValid != true)
                        continue;

                    var slot = GetWeaponSlot(itemName);
                    if (slot != gear_slot_t.GEAR_SLOT_INVALID)
                    {
                        pawn.WeaponServices?.RemoveWeaponBySlot(slot);
                    }
                    if (pawn?.IsValid == true)
                        pawn.ItemServices?.GiveItem(itemName);

                    PlayerUtils.SendNotification(Core, liveTarget, Messages,
                        $"<font color='#00ff00'><b>{L("give_personal_html")}</b></font><br><br>{L("label_item")}: <font color='#00ff00'>{itemName}</font><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(liveTarget, adminName)}</font>",
                        $" \x02{L("prefix")}\x01 {L("give_personal_chat", itemName, ResolveVisibleAdminName(liveTarget, adminName))}");

                    _ = AdminLogManager.AddLogAsync("give", adminName, ctx.Sender?.SteamID ?? 0, liveTarget.SteamID, liveTarget.IPAddress, $"item={itemName}", liveTarget.Controller.PlayerName);
                    givenCount++;
                }

                if (givenCount == 0) return;

                BroadcastNotification(adminName, "give_notification", FormatTargetName(targets), itemName);

                Core.Logger.LogInformation("{Admin} gave {ItemName} to {Count} player(s)", adminName, itemName, givenCount);
            });
    }

    private static gear_slot_t GetWeaponSlot(string itemName)
    {
        return itemName.ToLowerInvariant() switch
        {
            "weapon_ak47" or "weapon_m4a1" or "weapon_m4a1_silencer" or
            "weapon_awp" or "weapon_ssg08" or "weapon_aug" or "weapon_sg556" or
            "weapon_famas" or "weapon_galilar" or "weapon_scar20" or "weapon_p90" or
            "weapon_ump45" or "weapon_mp9" or "weapon_mac10" or "weapon_mp7" or
            "weapon_mp5sd" or "weapon_bizon" or "weapon_mag7" or "weapon_nova" or
            "weapon_xm1014" or "weapon_sawedoff" or "weapon_negev" or "weapon_m249"
                => gear_slot_t.GEAR_SLOT_RIFLE,

            "weapon_deagle" or "weapon_usp_silencer" or "weapon_hkp2000" or
            "weapon_glock" or "weapon_p250" or "weapon_fiveseven" or "weapon_tec9" or
            "weapon_cz75" or "weapon_revolver" or "weapon_elite"
                => gear_slot_t.GEAR_SLOT_PISTOL,

            "weapon_flashbang" or "weapon_hegrenade" or "weapon_smokegrenade" or
            "weapon_molotov" or "weapon_incgrenade" or "weapon_decoy"
                => gear_slot_t.GEAR_SLOT_GRENADES,

            "weapon_c4"
                => gear_slot_t.GEAR_SLOT_C4,

            "item_defuser" or "item_assaultsuit"
                => gear_slot_t.GEAR_SLOT_BOOSTS,

            _ => gear_slot_t.GEAR_SLOT_INVALID
        };
    }

    private static string ResolveWeaponName(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        if (normalized.StartsWith("weapon_") || normalized.StartsWith("item_"))
            return normalized;

        return normalized switch
        {
            "ak" or "ak47" => "weapon_ak47",
            "m4" or "m4a1" => "weapon_m4a1",
            "m4s" or "m4a1s" or "m4a1_silencer" => "weapon_m4a1_silencer",
            "awp" => "weapon_awp",
            "ssg" or "ssg08" or "scout" => "weapon_ssg08",
            "aug" => "weapon_aug",
            "sg" or "sg556" or "sg553" => "weapon_sg556",
            "famas" => "weapon_famas",
            "galil" or "galilar" => "weapon_galilar",
            "scar" or "scar20" => "weapon_scar20",
            "p90" => "weapon_p90",
            "ump" or "ump45" => "weapon_ump45",
            "mp9" => "weapon_mp9",
            "mac" or "mac10" => "weapon_mac10",
            "mp7" => "weapon_mp7",
            "mp5" or "mp5sd" => "weapon_mp5sd",
            "bizon" => "weapon_bizon",
            "mag7" or "mag" => "weapon_mag7",
            "nova" => "weapon_nova",
            "xm" or "xm1014" or "auto" => "weapon_xm1014",
            "sawedoff" or "sawed" => "weapon_sawedoff",
            "negev" => "weapon_negev",
            "m249" => "weapon_m249",

            "deagle" or "deag" or "revolver" or "r8" => "weapon_deagle",
            "usp" or "usp-s" or "usps" or "hkp2000" or "p2000" => "weapon_usp_silencer",
            "glock" or "glock18" => "weapon_glock",
            "p250" => "weapon_p250",
            "five" or "fiveseven" or "57" or "5-7" => "weapon_fiveseven",
            "tec" or "tec9" => "weapon_tec9",
            "cz" or "cz75" or "cz75a" => "weapon_cz75",
            "elite" or "dual" or "dualies" or "dual_berettas" => "weapon_elite",

            "flash" or "flashbang" => "weapon_flashbang",
            "he" or "hegrenade" or "hegren" => "weapon_hegrenade",
            "smoke" => "weapon_smokegrenade",
            "molly" or "molotov" or "inc" or "incgrenade" => "weapon_molotov",
            "decoy" => "weapon_decoy",

            "bomb" or "c4" or "tnt" => "weapon_c4",
            "defuse" or "kit" or "defusekit" => "item_defuser",
            "armor" or "kevlar" or "vest" or "helmet" or "assaultsuit" or "armor_helmet" => "item_assaultsuit",

            _ => normalized
        };
    }
}
