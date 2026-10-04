using System.Text.Json.Serialization;

namespace CS2_Admin.Config;

public class PluginConfig
{
    public const int CurrentVersion = 10;
    public int Version { get; set; } = CurrentVersion;
    public bool AutoUpdate { get; set; } = true;
    public bool Debug { get; set; } = false;
    public string Language { get; set; } = "en";
    public List<string> LanguageOptions { get; set; } = ["en", "tr", "de", "ru", "hu"];
    [JsonIgnore]
    public DiscordFileConfig Discord { get; set; } = new();
    [JsonIgnore]
    public AfkFileConfig Afk { get; set; } = new();
    public MessagesConfig Messages { get; set; } = new();
    public MultiServerConfig MultiServer { get; set; } = new();
    [JsonPropertyName("BanMode_Info_Comment")]
    public string BanModeInfo { get; set; } = "steamid, ip, both";
    public string BanMode { get; set; } = "steamid"; // steamid, ip, both
    [JsonIgnore]
    public TagsConfig Tags { get; set; } = new();
    [JsonIgnore]
    public bool ShowAdminName { get; set; } = true;
    public AdminPlaytimeConfig AdminPlaytime { get; set; } = new();
    [JsonIgnore]
    public CommandsConfig Commands { get; set; } = new();
    [JsonIgnore]
    public CommandBlockerFileConfig CommandBlocker { get; set; } = new();
    [JsonIgnore]
    public PermissionsConfig Permissions { get; set; } = new();
    [JsonIgnore]
    public GameMapsConfig GameMaps { get; set; } = new();
    [JsonIgnore]
    public WorkshopMapsConfig WorkshopMaps { get; set; } = new();
    [JsonIgnore]
    public MapsFileConfig MapsFile { get; set; } = new();
    public SanctionMenuConfig Sanctions { get; set; } = new();

    [JsonIgnore]
    public int EffectiveBanType => ParseBanMode(BanMode);

    public static int ParseBanMode(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
        {
            switch (mode.Trim().ToLowerInvariant())
            {
                case "steam":
                case "steamid":
                    return 1;
                case "ip":
                    return 2;
                case "both":
                case "steam+ip":
                case "steamid+ip":
                case "ip+steam":
                case "ip+steamid":
                    return 3;
            }
        }

        return 1;
    }

    public static string NormalizeBanMode(string? mode)
    {
        return ParseBanMode(mode) switch
        {
            2 => "ip",
            3 => "both",
            _ => "steamid"
        };
    }
}

public class TagsConfig
{
    public bool Enabled { get; set; } = false;
    public string PlayerTag { get; set; } = "PLAYER";
    public bool ShowAdminName { get; set; } = true;
}

public class ChatTagsFileConfig
{
    public const int CurrentVersion = 10;
    public int Version { get; set; } = CurrentVersion;
    public bool ScoreboardEnabled { get; set; } = true;
    public bool ChatEnabled { get; set; } = true;
    public string PlayerTag { get; set; } = "PLAYER";
    public bool ShowAdminName { get; set; } = true;
    public List<string> SupportedColors { get; set; } =
    [
        "[default]",
        "[white]",
        "[silver]",
        "[gray]",
        "[grey]",
        "[lightyellow]",
        "[yellow]",
        "[gold]",
        "[lightred]",
        "[red]",
        "[darkred]",
        "[olive]",
        "[lime]",
        "[green]",
        "[lightblue]",
        "[blue]",
        "[darkblue]",
        "[bluegrey]",
        "[lightpurple]",
        "[purple]",
        "[magenta]"
    ];
    // Legacy alias for older tags.json files.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; set; } = null;
    public Dictionary<string, ChatTagGroupStyle> Groups { get; set; } = new();
}

public class ChatTagGroupStyle
{
    public string ChatColor { get; set; } = "";
    public string TagColor { get; set; } = "";
    public string NameColor { get; set; } = "";
    public string TagText { get; set; } = "";
}

public class AdminPlaytimeConfig
{
    public int TrackIntervalMinutes { get; set; } = 1;
    public int MenuTopLimit { get; set; } = 20;
    public int DiscordTopLimit { get; set; } = 20;
    public bool AutoSendResetAfterSend { get; set; } = false;
    public int AutoSendDayOfWeek { get; set; } = 0;
}

public class MultiServerConfig
{
    public bool Enabled { get; set; } = false;
    public bool GlobalBansByDefault { get; set; } = true;
}

public class MessagesConfig
{
    public const int CurrentVersion = 10;
    public int Version { get; set; } = CurrentVersion;
    public string Prefix { get; set; } = "CS2_Admin";
    public bool EnableCenterHtmlMessages { get; set; } = true;
    public int CenterHtmlDurationMs { get; set; } = 5000;
    public float BanKickDelaySeconds { get; set; } = 5f;
}

public class DiscordFileConfig
{
    public const int CurrentVersion = 14;
    public int Version { get; set; } = CurrentVersion;
    public string ServerName { get; set; } = "";
    public string BotToken { get; set; } = "";
    public string ServerPublicIp { get; set; } = "";
    public string BannerUrl { get; set; } = "";
    public string CustomConnectUrl { get; set; } = "";
    public string AdminLogChannelId { get; set; } = "";
    public string ChatLogChannelId { get; set; } = "";
    public string ConnectionLogChannelId { get; set; } = "";
    public string CallAdminChannelId { get; set; } = "";
    public string ReportChannelId { get; set; } = "";
    public string AdminTimeChannelId { get; set; } = "";
    public string ServerStatusChannelId { get; set; } = "";
    public int ServerStatusUpdateSeconds { get; set; } = 30;
    public string StatusCategoryName { get; set; } = "";
    public string DiscordInvite { get; set; } = "";
    public bool VerifyEnabled { get; set; } = false;
    public string VerifyChannelId { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string VerifiedRoleId { get; set; } = "";
    public int VerifyCodeExpiryMinutes { get; set; } = 10;
    public string VerifyVipGroup { get; set; } = "VIP_STANDARD";
    public int VerifyVipDuration { get; set; } = 30;
}

public class AfkFileConfig
{
    public const int CurrentVersion = 10;
    public int Version { get; set; } = CurrentVersion;
    public bool Enabled { get; set; } = false;
    public float Timer { get; set; } = 30f;
    public bool SkipWarmup { get; set; } = true;
    public string WarningSound { get; set; } = "UIPanorama.ui_custom_lobby_dialog_slide";
    public bool AfkSkipAdmin { get; set; } = false;
}

public class CommandsConfig
{
    public const int CurrentVersion = 11;
    public int Version { get; set; } = CurrentVersion;
    [JsonIgnore]
    public List<string> AdminMenu { get; set; } = ["admin"];
    [JsonIgnore]
    public List<string> AdminRoot { get; set; } = ["admin"];
    [JsonIgnore]
    public List<string> Asay { get; set; } = ["asay"];
    [JsonIgnore]
    public List<string> Say { get; set; } = ["say"];
    [JsonIgnore]
    public List<string> Psay { get; set; } = ["psay"];
    [JsonIgnore]
    public List<string> Csay { get; set; } = ["csay"];
    [JsonIgnore]
    public List<string> Hsay { get; set; } = ["hsay"];
    public List<string> CallAdmin { get; set; } = ["calladmin"];
    public List<string> Report { get; set; } = ["report"];
    public List<string> Verify { get; set; } = ["verify", "link"];
    public List<string> Unverify { get; set; } = ["unverify", "unlink"];
    public List<string> AdminTime { get; set; } = ["admintime"];
    public List<string> AdminTimeSend { get; set; } = ["admintimesend"];
    public List<string> Afk { get; set; } = ["afk"];
    [JsonIgnore]
    public List<string> AdminReload { get; set; } = ["adminreload"];
    [JsonIgnore]
    public List<string> AddAdmin { get; set; } = ["addadmin"];
    [JsonIgnore]
    public List<string> EditAdmin { get; set; } = ["editadmin"];
    [JsonIgnore]
    public List<string> RemoveAdmin { get; set; } = ["removeadmin"];
    [JsonIgnore]
    public List<string> ListAdmins { get; set; } = ["listadmins", "admins"];
    [JsonIgnore]
    public List<string> AddGroup { get; set; } = ["addgroup"];
    [JsonIgnore]
    public List<string> EditGroup { get; set; } = ["editgroup"];
    [JsonIgnore]
    public List<string> RemoveGroup { get; set; } = ["removegroup"];
    [JsonIgnore]
    public List<string> ListGroups { get; set; } = ["listgroups"];
    public List<string> Ban { get; set; } = ["ban"];
    public List<string> IpBan { get; set; } = ["ipban"];
    public List<string> LastBan { get; set; } = ["lastban"];
    public List<string> Warn { get; set; } = ["warn"];
    public List<string> Unwarn { get; set; } = ["unwarn"];
    public List<string> AddBan { get; set; } = ["addban"];
    public List<string> Unban { get; set; } = ["unban"];
    public List<string> Kick { get; set; } = ["kick"];
    [JsonIgnore]
    public List<string> Slap { get; set; } = ["slap"];
    [JsonIgnore]
    public List<string> God { get; set; } = ["god"];
    [JsonIgnore]
    public List<string> Gag { get; set; } = ["gag"];
    [JsonIgnore]
    public List<string> Ungag { get; set; } = ["ungag"];
    [JsonIgnore]
    public List<string> Mute { get; set; } = ["mute"];
    [JsonIgnore]
    public List<string> Unmute { get; set; } = ["unmute"];
    [JsonIgnore]
    public List<string> Silence { get; set; } = ["silence"];
    [JsonIgnore]
    public List<string> Unsilence { get; set; } = ["unsilence"];
    [JsonIgnore]
    public List<string> Slay { get; set; } = ["slay"];
    [JsonIgnore]
    public List<string> Respawn { get; set; } = ["respawn", "revive"];
    [JsonIgnore]
    public List<string> ChangeTeam { get; set; } = ["team", "swap"];
    [JsonIgnore]
    public List<string> MixTeam { get; set; } = ["mixteam"];
    [JsonIgnore]
    public List<string> NoClip { get; set; } = ["noclip"];
    public List<string> Goto { get; set; } = ["goto"];
    public List<string> Bring { get; set; } = ["bring"];
    [JsonIgnore]
    public List<string> Freeze { get; set; } = ["freeze"];
    [JsonIgnore]
    public List<string> Unfreeze { get; set; } = ["unfreeze"];
    [JsonIgnore]
    public List<string> Resize { get; set; } = ["resize"];

    [JsonIgnore]
    public List<string> Blind { get; set; } = ["blind"];
    [JsonIgnore]
    public List<string> Glow { get; set; } = ["glow", "glove"];
    [JsonIgnore]
    public List<string> Rgb { get; set; } = ["rgb", "rainbow"];
    [JsonIgnore]
    public List<string> Beacon { get; set; } = ["beacon"];
    [JsonIgnore]
    public List<string> Bury { get; set; } = ["bury"];
    [JsonIgnore]
    public List<string> Unbury { get; set; } = ["unbury"];
    [JsonIgnore]
    public List<string> Burn { get; set; } = ["burn"];
    [JsonIgnore]
    public List<string> Disarm { get; set; } = ["disarm"];
    [JsonIgnore]
    public List<string> Speed { get; set; } = ["speed", "setspeed"];
    [JsonIgnore]
    public List<string> Gravity { get; set; } = ["gravity", "setgravity"];
    [JsonIgnore]
    public List<string> Rename { get; set; } = ["rename"];
    [JsonIgnore]
    public List<string> Unrename { get; set; } = ["unrename"];
    [JsonIgnore]
    public List<string> Hp { get; set; } = ["hp"];
    [JsonIgnore]
    public List<string> Money { get; set; } = ["money", "setmoney", "givemoney"];
    [JsonIgnore]
    public List<string> Give { get; set; } = ["give", "giveitem"];
    [JsonIgnore]
    public List<string> Vote { get; set; } = ["vote"];
    public List<string> ChangeMap { get; set; } = ["map"];
    public List<string> ChangeWSMap { get; set; } = ["wsmap"];
    [JsonIgnore]
    public List<string> RestartGame { get; set; } = ["rr", "restart"];
    [JsonIgnore]
    public List<string> HeadshotMode { get; set; } = ["hson", "hsoff"];
    [JsonIgnore]
    public List<string> HeadshotOn { get; set; } = ["hson"];
    [JsonIgnore]
    public List<string> HeadshotOff { get; set; } = ["hsoff"];
    [JsonIgnore]
    public List<string> BunnyHop { get; set; } = ["bhopon", "bunnyon", "bhopoff", "bunnyoff"];
    [JsonIgnore]
    public List<string> BunnyOn { get; set; } = ["bhopon", "bunnyon"];
    [JsonIgnore]
    public List<string> BunnyOff { get; set; } = ["bhopoff", "bunnyoff"];
    [JsonIgnore]
    public List<string> RespawnMode { get; set; } = ["respawnon", "respawnoff"];
    [JsonIgnore]
    public List<string> RespawnOn { get; set; } = ["respawnon"];
    [JsonIgnore]
    public List<string> RespawnOff { get; set; } = ["respawnoff"];
    [JsonIgnore]
    public List<string> Rcon { get; set; } = ["rcon"];
    [JsonIgnore]
    public List<string> Cvar { get; set; } = ["cvar"];
    public List<string> ListPlayers { get; set; } = ["players"];
    public List<string> Who { get; set; } = ["who"];
}

public class PermissionsConfig
{
    public const int CurrentVersion = 11;
    public int Version { get; set; } = CurrentVersion;
    public string AdminMenu { get; set; } = "admin.generic";
    public string AdminRoot { get; set; } = "admin.root";
    public string Asay { get; set; } = "admin.generic";
    public string Say { get; set; } = "admin.generic";
    public string Psay { get; set; } = "admin.generic";
    public string Csay { get; set; } = "admin.generic";
    public string Hsay { get; set; } = "admin.generic";
    public string CallAdmin { get; set; } = "admin.generic";
    public string Report { get; set; } = "";
    public string Verify { get; set; } = "";
    public string Unverify { get; set; } = "";
    public string AdminTime { get; set; } = "admin.generic";
    public string AdminTimeSend { get; set; } = "admin.root";
    public string AdminReload { get; set; } = "admin.root";
    public string AddAdmin { get; set; } = "admin.root";
    public string EditAdmin { get; set; } = "admin.root";
    public string RemoveAdmin { get; set; } = "admin.root";
    public string ListAdmins { get; set; } = "admin.root";
    public string AddGroup { get; set; } = "admin.root";
    public string EditGroup { get; set; } = "admin.root";
    public string RemoveGroup { get; set; } = "admin.root";
    public string ListGroups { get; set; } = "admin.root";
    public string Ban { get; set; } = "admin.ban";
    public string IpBan { get; set; } = "admin.ban";
    public string LastBan { get; set; } = "admin.ban";
    public string Warn { get; set; } = "admin.generic";
    public string Unwarn { get; set; } = "admin.generic";
    public string ListWarns { get; set; } = "admin.generic";
    public string AddBan { get; set; } = "admin.ban";
    public string Unban { get; set; } = "admin.ban";
    public string Kick { get; set; } = "admin.kick";
    public string Slap { get; set; } = "admin.cheats";
    public string God { get; set; } = "admin.cheats";
    public string Gag { get; set; } = "admin.mute";
    public string Ungag { get; set; } = "admin.mute";
    public string Mute { get; set; } = "admin.mute";
    public string Unmute { get; set; } = "admin.mute";
    public string Silence { get; set; } = "admin.mute";
    public string Unsilence { get; set; } = "admin.mute";
    public string Slay { get; set; } = "admin.cheats";
    public string Respawn { get; set; } = "admin.cheats";
    public string ChangeTeam { get; set; } = "admin.cheats";
    public string MixTeam { get; set; } = "admin.cheats";
    public string NoClip { get; set; } = "admin.cheats";
    public string Goto { get; set; } = "admin.cheats";
    public string Bring { get; set; } = "admin.cheats";
    public string Freeze { get; set; } = "admin.cheats";
    public string Unfreeze { get; set; } = "admin.cheats";
    public string Resize { get; set; } = "admin.cheats";

    public string Blind { get; set; } = "admin.cheats";
    public string Glow { get; set; } = "admin.cheats";
    public string Rgb { get; set; } = "admin.cheats";
    public string Beacon { get; set; } = "admin.cheats";
    public string Bury { get; set; } = "admin.cheats";
    public string Unbury { get; set; } = "admin.cheats";
    public string Burn { get; set; } = "admin.cheats";
    public string Disarm { get; set; } = "admin.cheats";
    public string Speed { get; set; } = "admin.cheats";
    public string Gravity { get; set; } = "admin.cheats";
    public string Rename { get; set; } = "admin.cheats";
    public string Unrename { get; set; } = "admin.cheats";
    public string Hp { get; set; } = "admin.cheats";
    public string Money { get; set; } = "admin.cheats";
    public string Give { get; set; } = "admin.cheats";
    public string Vote { get; set; } = "admin.generic";
    public string ChangeMap { get; set; } = "admin.generic";
    public string ChangeWSMap { get; set; } = "admin.generic";
    public string RestartGame { get; set; } = "admin.generic";
    public string HeadshotMode { get; set; } = "admin.rcon";
    public string BunnyHop { get; set; } = "admin.rcon";
    public string RespawnMode { get; set; } = "admin.rcon";
    public string Rcon { get; set; } = "admin.rcon";
    public string Cvar { get; set; } = "admin.cvar";
    public string ListPlayers { get; set; } = "admin.generic";
    public string Who { get; set; } = "admin.generic";

    [JsonIgnore]
    public List<string> RootBypassPermissions { get; set; } = ["admin.*", "*"];
}

public class GameMapsConfig
{
    public Dictionary<string, string> Maps { get; set; } = new()
    {
        { "de_dust2", "Dust 2" },
        { "de_mirage", "Mirage" },
        { "de_inferno", "Inferno" },
        { "de_ancient", "Ancient" },
        { "de_anubis", "Anubis" },
        { "de_train", "Train" },
        { "de_nuke", "Nuke" },
        { "de_overpass", "Overpass" },
        { "de_vertigo", "Vertigo" }
    };
}

public class MapsFileConfig
{
    public const int CurrentVersion = 10;
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, string> Maps { get; set; } = new()
    {
        { "de_dust2", "Dust 2" },
        { "de_mirage", "Mirage" },
        { "de_inferno", "Inferno" },
        { "de_ancient", "Ancient" },
        { "de_anubis", "Anubis" },
        { "de_train", "Train" },
        { "de_nuke", "Nuke" },
        { "de_overpass", "Overpass" },
        { "de_vertigo", "Vertigo" }
    };

    public Dictionary<string, uint> WorkshopMaps { get; set; } = new()
    {
        { "Inferno Online", 3549919360 }
    };
}

public class WorkshopMapsConfig
{
    public Dictionary<string, uint> Maps { get; set; } = new()
    {
        { "Inferno Online", 3549919360 }
    };
}

public class SanctionDurationConfigItem
{
    public string Name { get; set; } = "";
    public int Minutes { get; set; }
}

public class SanctionMenuConfig
{
    public List<string> Reasons { get; set; } =
    [
        "Hacking",
        "Obscene language",
        "Insult players",
        "Admin disrespect",
        "Other"
    ];

    public List<SanctionDurationConfigItem> Durations { get; set; } = new()
    {
        new() { Name = "5 minutes", Minutes = 5 },
        new() { Name = "30 minutes", Minutes = 30 },
        new() { Name = "1 hour", Minutes = 60 },
        new() { Name = "1 day", Minutes = 60 * 24 },
        new() { Name = "1 week", Minutes = 60 * 24 * 7 },
        new() { Name = "1 month", Minutes = 60 * 24 * 30 },
        new() { Name = "Permanent", Minutes = -1 }
    };
}



public class CommandBlockerFileConfig
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public bool Enabled { get; set; } = false;
    public List<string> BlockedCommands { get; set; } = ["shop", "ws", "skins"];
    public int RoleCacheSeconds { get; set; } = 60;
}
