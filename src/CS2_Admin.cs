using CS2_Admin.Commands;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Events;
using CS2_Admin.Menu;
using CS2_Admin.Services;
using CS2_Admin.Utils;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CS2_Admin;

[PluginMetadata(Id = "CS2_Admin", Version = "1.0.20", Name = "CS2_Admin", Author = "CanDaysa+aga", Description = "Comprehensive admin plugin for CS2.")]
public partial class CS2_Admin : BasePlugin
{
    private PluginConfig _config = null!;
    private AdminMenuManager _adminMenuManager = null!;
    private DiscordBotService _discord = null!;
    private EventRegistrar _eventRegistrar = null!;
    private AfkManagerService _afkManager = null!;
    private PlayerSanctionStateService _sanctionStateService = null!;
    private RecentPlayersTracker _recentPlayersTracker = null!;
    private ChatTagConfigManager _chatTagConfigManager = null!;
    private CommandBlockerService _commandBlocker = null!;
    private volatile bool _commandBlockerReady;
    private TagDbManager _tagDbManager = null!;

    private BanManager _banManager = null!;
    private MuteManager _muteManager = null!;
    private GagManager _gagManager = null!;
    private WarnManager _warnManager = null!;
    private GroupDbManager _groupDbManager = null!;
    private AdminDbManager _adminDbManager = null!;
    private AdminLogManager _adminLogManager = null!;
    private ServerInfoDbManager _serverInfoDbManager = null!;
    private DiscordServerStatusDbManager _discordServerStatusDbManager = null!;
    private DiscordMessageStateDbManager _discordMessageStateDbManager = null!;
    private DiscordLinkDbManager _discordLinkDbManager = null!;
    private readonly ConcurrentDictionary<int, (ulong SteamId, string Name, string Ip)> _connectedPlayersCache = new();
    private AdminPlaytimeDbManager _adminPlaytimeDbManager = null!;
    private PlayerIpDbManager _playerIpDbManager = null!;
    private PlayerSessionManager _playerSessionManager = null!;
    private PlayerNameHistoryManager _playerNameHistoryManager = null!;
    private AltAccountDetectionService _altAccountDetection = null!;

    private AdminMenuCommand _adminMenuCmd = null!;
    private AsayCommand _asayCmd = null!;
    private SayCommand _sayCmd = null!;
    private PsayCommand _psayCmd = null!;
    private CsayCommand _csayCmd = null!;
    private HsayCommand _hsayCmd = null!;
    private CallAdminCommand _callAdminCmd = null!;
    private ReportCommand _reportCmd = null!;
    private VerifyCommand _verifyCmd = null!;
    private UnverifyCommand _unverifyCmd = null!;
    private AdminTimeCommand _adminTimeCmd = null!;
    private AdminTimeSendCommand _adminTimeSendCmd = null!;
    private BanCommand _banCmd = null!;
    private IpBanCommand _ipBanCmd = null!;
    private LastBanCommand _lastBanCmd = null!;
    private AddBanCommand _addBanCmd = null!;
    private UnbanCommand _unbanCmd = null!;
    private WarnCommand _warnCmd = null!;
    private UnwarnCommand _unwarnCmd = null!;
    private MuteCommand _muteCmd = null!;
    private UnmuteCommand _unmuteCmd = null!;
    private GagCommand _gagCmd = null!;
    private UngagCommand _ungagCmd = null!;
    private SilenceCommand _silenceCmd = null!;
    private UnsilenceCommand _unsilenceCmd = null!;
    private KickCommand _kickCmd = null!;
    private SlapCommand _slapCmd = null!;
    private SlayCommand _slayCmd = null!;
    private RespawnCommand _respawnCmd = null!;
    private TeamCommand _teamCmd = null!;
    private MixTeamCommand _mixTeamCmd = null!;
    private NoClipCommand _noClipCmd = null!;
    private GotoCommand _gotoCmd = null!;
    private BringCommand _bringCmd = null!;
    private FreezeCommand _freezeCmd = null!;
    private UnfreezeCommand _unfreezeCmd = null!;
    private ResizeCommand _resizeCmd = null!;

    private BlindCommand _blindCmd = null!;
    private GlowCommand _glowCmd = null!;
    private RgbCommand _rgbCmd = null!;
    private BeaconCommand _beaconCmd = null!;
    private BuryCommand _buryCmd = null!;
    private UnburyCommand _unburyCmd = null!;
    private BurnCommand _burnCmd = null!;
    private DisarmCommand _disarmCmd = null!;
    private SpeedCommand _speedCmd = null!;
    private GravityCommand _gravityCmd = null!;
    private RenameCommand _renameCmd = null!;
    private UnrenameCommand _unrenameCmd = null!;
    private HpCommand _hpCmd = null!;
    private MoneyCommand _moneyCmd = null!;
    private GiveCommand _giveCmd = null!;
    private VoteCommand _voteCmd = null!;
    private MapCommand _mapCmd = null!;
    private WsMapCommand _wsMapCmd = null!;
    private RestartCommand _restartCmd = null!;
    private HsToggleCommand _hsToggleCmd = null!;
    private BunnyToggleCommand _bunnyToggleCmd = null!;
    private RespawnToggleCommand _respawnToggleCmd = null!;
    private RconCommand _rconCmd = null!;
    private CvarCommand _cvarCmd = null!;
    private ListPlayersCommand _listPlayersCmd = null!;

    private AddAdminCommand _addAdminCmd = null!;
    private EditAdminCommand _editAdminCmd = null!;
    private RemoveAdminCommand _removeAdminCmd = null!;
    private ListAdminsCommand _listAdminsCmd = null!;
    private AddGroupCommand _addGroupCmd = null!;
    private EditGroupCommand _editGroupCmd = null!;
    private RemoveGroupCommand _removeGroupCmd = null!;
    private ListGroupsCommand _listGroupsCmd = null!;
    private GodCommand _godCmd = null!;
    private AdminReloadCommand _adminReloadCmd = null!;

    // --- Reload Duplikasyon Önleme ---
    // SwiftlyS2'de CommandService.commandsByPlugin sözlüğü STATIC'tir (host'ta tek kopya,
    // eklenti adına göre anahtarlı) ve DispatchCommand eşleşen TÜM callback'leri çağırır.
    // Eklenti unload olduğunda framework bu static sözlükten eski callback'leri SİLMEZ;
    // bu yüzden her reload'da eski + yeni callback'ler birikir ve komutlar 2,3,4,5 kez çalışır.
    // Çözüm: her wrapper bu instance bayrağını kontrol eder. Unload() bayrağı false yapınca
    // eski (stale) handler'lar sözlükte kalsalar bile hiçbir şey yapmadan döner. Böylece
    // sözlüğü mutasyona uğratmadan (yani SwiftlyS2'yi crash etmeden) duplikasyon engellenir.
    private volatile bool _commandsActive = true;

    private Timer? _adminPlaytimeTimer;
    private Timer? _adminTimeAutoSendTimer;
    private Timer? _periodicUpdateTimer;
    private int _isTrackingAdminPlaytime;


    public CS2_Admin(ISwiftlyCore core) : base(core) { }

    public override void Load(bool hotReload)
    {
        _commandsActive = true;
        _commandBlockerReady = false;
        LoadConfiguration();
        _discord = new DiscordBotService(Core, _config.Discord, _config.Commands);
        InitializeDatabaseManagers();
        _commandBlocker = new CommandBlockerService(Core, _config.CommandBlocker, _config.Discord, _config.Commands, _discordLinkDbManager);
        _commandBlocker.Start();
        _discord.Verify.LinkChanged += _commandBlocker.OnLinkChanged;
        _chatTagConfigManager.SetTagDbManager(_tagDbManager);
        _discord.EnsureGatewayConnection();
        _adminMenuManager = new AdminMenuManager(Core, _config, _warnManager, _adminDbManager, _groupDbManager, _adminLogManager, _adminPlaytimeDbManager, _tagDbManager);
        _adminLogManager.SetDiscordBotService(_discord);
        InitializeCommands();
        InitializeEventHandlers();
        RegisterCommands();
        _afkManager.Start();
        _ = InitializeDatabasesAsync();
        
        var versionAttr = (PluginMetadata)Attribute.GetCustomAttribute(typeof(CS2_Admin), typeof(PluginMetadata));
        if (versionAttr != null)
        {
            if (_config.AutoUpdate)
            {
                _ = global::CS2_Admin.Utils.AutoUpdater.CheckForUpdatesAsync(Core, versionAttr.Version);
                StartPeriodicUpdateCheck(versionAttr.Version);
            }
            else
            {
                Core.Logger.LogInformationIfEnabled("[CS2Admin] Auto-update is disabled in config.json (AutoUpdate=false).");
            }
        }

        Core.Logger.LogInformationIfEnabled("[CS2Admin] Plugin loaded successfully!");
    }

    public override void Unload()
    {
        // commandsByPlugin SwiftlyS2 tarafında static; UnregisterCommand burada çağrılırsa
        // (reload bir komut dispatch'i içinden tetiklenmişse) sözlük iterasyon sırasında
        // mutasyona uğrar ve SwiftlyS2 crash olur. Bunun yerine bu instance'ın komutlarını
        // _commandsActive=false ile pasifleştiriyoruz: stale handler'lar artık hiçbir şey
        // yapmaz, böylece reload'da duplikasyon oluşmaz (crash riski de yoktur).
        _commandsActive = false;
        _eventRegistrar?.UnregisterAll();
        if (_commandBlocker != null && _discord != null)
            _discord.Verify.LinkChanged -= _commandBlocker.OnLinkChanged;
        _commandBlocker?.Stop();
        _afkManager?.Stop();
        _adminPlaytimeTimer?.Dispose();
        _adminTimeAutoSendTimer?.Dispose();
        _periodicUpdateTimer?.Dispose();
        _discord?.StopBackgroundUpdates();
    }

    private void LoadConfiguration()
    {
        _config = new PluginConfig();
        _chatTagConfigManager ??= new ChatTagConfigManager(Core);
        EnsureConfig<PluginConfig>("config.json", "CS2Admin", PluginConfig.CurrentVersion, cfg => _config = cfg);
        EnsureConfig<PermissionsConfig>("permissions.json", "CS2AdminPermissions", PermissionsConfig.CurrentVersion, cfg => _config.Permissions = cfg);
        EnsureConfig<MapsFileConfig>("maps.json", "CS2AdminMaps", MapsFileConfig.CurrentVersion, cfg => { _config.MapsFile = cfg; if (cfg.Maps.Count > 0) _config.GameMaps.Maps = cfg.Maps; if (cfg.WorkshopMaps.Count > 0) _config.WorkshopMaps.Maps = cfg.WorkshopMaps; });
        EnsureConfig<DiscordFileConfig>("discord.json", "CS2_Discord", DiscordFileConfig.CurrentVersion, cfg => { _config.Discord = cfg; ServerIdentity.ConfigurePublicIp(cfg.ServerPublicIp); });
        EnsureConfig<AfkFileConfig>("afk.json", "CS2AdminAfk", AfkFileConfig.CurrentVersion, cfg => _config.Afk = cfg);
        EnsureConfig<CommandsConfig>("commands.json", "CS2AdminCommands", CommandsConfig.CurrentVersion, cfg => _config.Commands = cfg);
        EnsureConfig<CommandBlockerFileConfig>("commandblocker.json", "CS2AdminCommandBlocker", CommandBlockerFileConfig.CurrentVersion, cfg => _config.CommandBlocker = cfg);
        EnsureConfig<ChatTagsFileConfig>("tags.json", "CS2AdminTags", ChatTagsFileConfig.CurrentVersion, cfg => _chatTagConfigManager.Load(cfg));
        SanitizeCommandAliases();
        EnsureBanModeConfig();
        _config.BanMode = PluginConfig.NormalizeBanMode(_config.BanMode);
        DebugSettings.LoggingEnabled = _config.Debug;
        ApplyLanguageCulture(_config.Language);
        PluginLocalizer.SetConfiguredPrefix(_config.Messages.Prefix);
        LoadCustomLocalizer();
    }

    private void LoadCustomLocalizer()
    {
        try
        {
            var languageDir = Path.Combine(Core.PluginPath, "resources", "language");
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Attempting to load custom localizer from: {Path} | Config Language: {Lang}", languageDir, _config.Language);
            
            // Extract embedded translations to disk if they are missing.
            ExtractEmbeddedTranslationsToConfigDirectory(languageDir);

            var localizer = JsonFileLocalizer.TryCreate(languageDir, _config.Language);
            if (localizer != null)
            {
                PluginLocalizer.SetOverride(localizer);
                Core.Logger.LogInformationIfEnabled("[CS2Admin] Custom localizer loaded successfully from: {Path}", languageDir);
            }
            else
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to create custom localizer from {Path}, fallback to native.", languageDir);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Error loading custom localizer: {Message}", ex.Message);
        }
    }

    private void ExtractEmbeddedTranslationsToConfigDirectory(string outputDir)
    {
        try
        {
            Directory.CreateDirectory(outputDir);

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var resources = asm.GetManifestResourceNames()
                .Where(x => x.StartsWith("CS2_Admin.Translations.", StringComparison.OrdinalIgnoreCase)
                            && x.EndsWith(".lang", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (resources.Count == 0)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] No embedded translation resources were found in assembly.");
                return;
            }

            foreach (var resourceName in resources)
            {
                // Remove the prefix and the .lang suffix, then append .jsonc
                var nameWithoutPrefix = resourceName["CS2_Admin.Translations.".Length..];
                var fileName = nameWithoutPrefix[..^5] + ".jsonc";
                var destinationPath = Path.Combine(outputDir, fileName);
                
                // Do not overwrite existing files, allowing users to modify them
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var file = File.Create(destinationPath);
                stream.CopyTo(file);
                Core.Logger.LogInformationIfEnabled("[CS2Admin] Extracted default translation file: {File}", fileName);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to extract translations: {Message}", ex.Message);
        }
    }

    private void EnsureConfig<T>(string file, string section, int version, Action<T> apply) where T : class, new()
    {
        try
        {
            global::CS2_Admin.Utils.ConfigMigrator.EnsureVersionedConfigFile<T>(Core, Core.Configuration.GetConfigPath(file), file, section, version);
            Core.Configuration.InitializeJsonWithModel<T>(file, section).Configure(b => b.AddJsonFile(Core.Configuration.GetConfigPath(file), false, true));
            var cfg = new T();
            Core.Configuration.Manager.GetSection(section).Bind(cfg);
            apply(cfg);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load {File}: {Msg}", file, ex.Message);
        }
    }

    private void ApplyLanguageCulture(string lang)
    {
        var culture = lang.ToLowerInvariant() switch { "tr" => "tr-TR", "de" => "de-DE", "fr" => "fr-FR", "it" => "it-IT", "el" => "el-GR", "ru" => "ru-RU", "bg" => "bg-BG", "hu" => "hu-HU", _ => "en-US" };
        try
        {
            var ci = CultureInfo.GetCultureInfo(culture);
            CultureInfo.DefaultThreadCurrentCulture = ci;
            CultureInfo.DefaultThreadCurrentUICulture = ci;
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Failed to apply culture {Culture}", culture);
        }
    }

    private void LoadChatTags()
    {
        try
        {
            _chatTagConfigManager.Load();
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load chat tags, using defaults: {Msg}", ex.Message);
        }
    }

    private void EnsureBanModeConfig()
    {
        try
        {
            var configPath = Core.Configuration.GetConfigPath("config.json");
            if (!File.Exists(configPath))
                return;

            var json = File.ReadAllText(configPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
                return;

            JsonObject targetSection;
            if (root["CS2Admin"] is JsonObject pluginSection)
                targetSection = pluginSection;
            else
                targetSection = root;

            var desiredBanMode = PluginConfig.NormalizeBanMode(targetSection["BanMode"]?.GetValue<string>());
            targetSection.Remove("BanType");
            targetSection.Remove("BanType_Info_Comment");

            var currentBanMode = targetSection["BanMode"]?.GetValue<string>();
            if (string.Equals(currentBanMode, desiredBanMode, StringComparison.OrdinalIgnoreCase))
                return;

            targetSection["BanMode"] = desiredBanMode;
            var rewritten = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, rewritten);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Normalized BanMode in config.json: {BanMode}", desiredBanMode);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to normalize BanMode in config.json: {Msg}", ex.Message);
        }
    }

    private void InitializeDatabaseManagers()
    {
        _groupDbManager = new GroupDbManager(Core);
        _banManager = new BanManager(Core);
        _muteManager = new MuteManager(Core);
        _gagManager = new GagManager(Core);
        _warnManager = new WarnManager(Core);
        _tagDbManager = new TagDbManager(Core);
        _adminDbManager = new AdminDbManager(Core, _groupDbManager);
        _adminLogManager = new AdminLogManager(Core);
        _discordLinkDbManager = new DiscordLinkDbManager(Core);
        _discord.SetDatabaseManagers(_warnManager, _adminLogManager, _discordLinkDbManager);
        _serverInfoDbManager = new ServerInfoDbManager(Core);
        _discordServerStatusDbManager = new DiscordServerStatusDbManager(Core);
        _discordMessageStateDbManager = new DiscordMessageStateDbManager(Core);
        _adminPlaytimeDbManager = new AdminPlaytimeDbManager(Core, _adminDbManager);
        _playerIpDbManager = new PlayerIpDbManager(Core);
        _playerSessionManager = new PlayerSessionManager(Core, _adminDbManager);
        _playerNameHistoryManager = new PlayerNameHistoryManager(Core);
        _recentPlayersTracker = new RecentPlayersTracker();
        _sanctionStateService = new PlayerSanctionStateService(_banManager, _muteManager, _gagManager, _warnManager, _config.MultiServer);
        _altAccountDetection = new AltAccountDetectionService(Core, _playerIpDbManager);
        _afkManager = new AfkManagerService(Core, _config.Afk, _config.Permissions, _config.Messages);
    }

    private void InitializeCommands()
    {
        var ps = new PermissionService(Core, _config.Permissions);

        // Base-only commands
        _hsToggleCmd = new HsToggleCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _respawnToggleCmd = new RespawnToggleCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _voteCmd = new VoteCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _rconCmd = new RconCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _cvarCmd = new CvarCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _restartCmd = new RestartCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _bunnyToggleCmd = new BunnyToggleCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _listPlayersCmd = new ListPlayersCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);

        // AdminDbManager-only commands (pattern: extra at end)
        _kickCmd = new KickCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _psayCmd = new PsayCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _csayCmd = new CsayCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _hsayCmd = new HsayCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _slapCmd = new SlapCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _slayCmd = new SlayCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _godCmd = new GodCommand(Core, _adminDbManager, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _respawnCmd = new RespawnCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _teamCmd = new TeamCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _mixTeamCmd = new MixTeamCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _resizeCmd = new ResizeCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);

        _blindCmd = new BlindCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _glowCmd = new GlowCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _rgbCmd = new RgbCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _beaconCmd = new BeaconCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _buryCmd = new BuryCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _unburyCmd = new UnburyCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _burnCmd = new BurnCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _disarmCmd = new DisarmCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _speedCmd = new SpeedCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _gravityCmd = new GravityCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _hpCmd = new HpCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _moneyCmd = new MoneyCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _giveCmd = new GiveCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);
        _listAdminsCmd = new ListAdminsCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager);

        // AdminDbManager-only commands (pattern: extra after core)
        _bringCmd = new BringCommand(Core, _adminDbManager, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _gotoCmd = new GotoCommand(Core, _adminDbManager, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _freezeCmd = new FreezeCommand(Core, _adminDbManager, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _unfreezeCmd = new UnfreezeCommand(Core, _adminDbManager, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);
        _noClipCmd = new NoClipCommand(Core, _adminDbManager, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps);

        // AdminDbManager + DiscordBotService
        _sayCmd = new SayCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _discord);
        _asayCmd = new AsayCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _discord);

        // DiscordBotService only
        _callAdminCmd = new CallAdminCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _discord);
        _reportCmd = new ReportCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _discord, _config.Sanctions);
        _verifyCmd = new VerifyCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _discord, _config.Discord);
        _unverifyCmd = new UnverifyCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _discord, _config.Discord);

        // AdminDbManager + GroupDbManager
        _listGroupsCmd = new ListGroupsCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager);

        // AdminDbManager + GroupDbManager + ChatTagConfigManager
        _addAdminCmd = new AddAdminCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);
        _editAdminCmd = new EditAdminCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);
        _removeAdminCmd = new RemoveAdminCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);
        _addGroupCmd = new AddGroupCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);
        _editGroupCmd = new EditGroupCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);
        _removeGroupCmd = new RemoveGroupCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);
        _adminReloadCmd = new AdminReloadCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _groupDbManager, _chatTagConfigManager);

        // AdminDbManager + PlayerNameHistoryManager
        _renameCmd = new RenameCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _playerNameHistoryManager);
        _unrenameCmd = new UnrenameCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminDbManager, _playerNameHistoryManager);

        // AdminPlaytime commands
        _adminTimeCmd = new AdminTimeCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminPlaytimeDbManager, _config.AdminPlaytime);
        _adminTimeSendCmd = new AdminTimeSendCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminPlaytimeDbManager, _discord, _config.AdminPlaytime);

        // Map commands
        _mapCmd = new MapCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _config.GameMaps, _config.WorkshopMaps);
        _wsMapCmd = new WsMapCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _config.GameMaps, _config.WorkshopMaps);

        // AdminMenu command
        _adminMenuCmd = new AdminMenuCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _adminMenuManager);

        // Ban/IPBan/Unban/AddBan commands
        _banCmd = new BanCommand(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _playerIpDbManager, _playerSessionManager, _recentPlayersTracker, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _config.Sanctions, _config.MultiServer, _config.EffectiveBanType, _sanctionStateService, ps);
        _ipBanCmd = new IpBanCommand(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _playerIpDbManager, _playerSessionManager, _recentPlayersTracker, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _config.Sanctions, _config.MultiServer, _config.EffectiveBanType, _sanctionStateService, ps);
        _unbanCmd = new UnbanCommand(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _playerIpDbManager, _playerSessionManager, _recentPlayersTracker, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _config.Sanctions, _config.MultiServer, _config.EffectiveBanType, _sanctionStateService, ps);
        _addBanCmd = new AddBanCommand(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _playerIpDbManager, _playerSessionManager, _recentPlayersTracker, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _config.Sanctions, _config.MultiServer, _config.EffectiveBanType, _sanctionStateService, ps);
        _lastBanCmd = new LastBanCommand(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _playerIpDbManager, _playerSessionManager, _recentPlayersTracker, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _config.Sanctions, _config.MultiServer, _config.EffectiveBanType, _sanctionStateService, ps, _config.Commands.LastBan);

        // Mute/Gag/Silence commands
        _muteCmd = new MuteCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _muteManager, _gagManager, _adminDbManager, _discord, _sanctionStateService, _config.Permissions.Mute);
        _unmuteCmd = new UnmuteCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _muteManager, _gagManager, _adminDbManager, _discord, _sanctionStateService, _config.Permissions.Mute);
        _gagCmd = new GagCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _muteManager, _gagManager, _adminDbManager, _discord, _sanctionStateService, _config.Permissions.Gag);
        _ungagCmd = new UngagCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _muteManager, _gagManager, _adminDbManager, _discord, _sanctionStateService, _config.Permissions.Gag);
        _silenceCmd = new SilenceCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _muteManager, _gagManager, _adminDbManager, _discord, _sanctionStateService, _config.Permissions.Silence);
        _unsilenceCmd = new UnsilenceCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _muteManager, _gagManager, _adminDbManager, _discord, _sanctionStateService, _config.Permissions.Silence);

        // Warn/Unwarn
        _warnCmd = new WarnCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _warnManager, _adminDbManager, _discord, _sanctionStateService, _config.Sanctions);
        _unwarnCmd = new UnwarnCommand(Core, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _adminLogManager, ps, _warnManager, _adminDbManager, _discord, _sanctionStateService);

        // Who command

    }

    private void InitializeEventHandlers()
    {
        _eventRegistrar = new EventRegistrar(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _groupDbManager, _sanctionStateService, _config.MultiServer, _config.Tags, _config.Permissions, _chatTagConfigManager, _playerNameHistoryManager, _playerSessionManager, _discord, _config.Commands);
        _eventRegistrar.OnClientPutInServer(e =>
        {
            Core.Scheduler.DelayBySeconds(3f, () =>
            {
                var player = Core.PlayerManager.GetPlayer(e.PlayerId);
                if (player?.IsValid == true && !player.IsFakeClient)
                {
                    _connectedPlayersCache[e.PlayerId] = (player.SteamID, player.Controller.PlayerName ?? "", player.IPAddress ?? "");
                    _ = _playerSessionManager.OpenSessionAsync(player.SteamID, player.Controller.PlayerName, e.PlayerId, player.IPAddress);
                    if (_commandBlockerReady) _commandBlocker.WarmUp(player.SteamID);
                    // Snapshot'ı yenile VE aktif mute varsa voice'u tekrar uygula. VoiceFlags
                    // yalnızca komut anında ayarlandığı için reconnect'te muteli oyuncu aksi halde
                    // konuşabiliyordu; burada snapshot otoriteyle yeniden zorlanıyor.
                    _ = ReapplyVoiceMuteOnConnectAsync(e.PlayerId, player.SteamID, player.IPAddress);
                    _ = _playerNameHistoryManager.ObserveNameAsync(player.SteamID, player.Controller.PlayerName);
                    
                    int activePlayers = Core.PlayerManager.GetAllPlayers().Count(p => p.IsValid && !p.IsFakeClient);
                    if (_discord != null)
                        _ = _discord.SendConnectNotificationAsync(player.Controller.PlayerName, player.SteamID, player.IPAddress, activePlayers);

                    _ = _playerIpDbManager.UpsertPlayerIpAsync(player.SteamID, player.Controller.PlayerName, player.IPAddress);
                    _ = RunAltAccountCheckAsync(player.SteamID, player.Controller.PlayerName, player.IPAddress);
    
                    _ = Task.Run(async () =>
                    {
                        var customName = await _playerNameHistoryManager.GetCustomNameAsync(player.SteamID);
                        if (!string.IsNullOrWhiteSpace(customName))
                        {
                            Core.Scheduler.NextTick(() =>
                            {
                                var live = Core.PlayerManager.GetPlayer(e.PlayerId);
                                if (live?.IsValid == true && live.Controller != null)
                                {
                                    live.Controller.PlayerName = customName;
                                    live.Controller.PlayerNameUpdated();
                                }
                            });
                        }
                    });
                }
            });
        });
        _eventRegistrar.OnClientSteamAuthorize(e =>
        {
            var player = Core.PlayerManager.GetPlayer(e.PlayerId);
            if (player?.IsValid == true && !player.IsFakeClient)
            {
                var steamId = player.SteamID;
                var ipAddress = player.IPAddress;
                var playerId = e.PlayerId;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Banlı oyuncuyu bağlanma anında at (referans plugin de SteamAuthorize'da
                        // yapıyor). Periyodik 5 sn'lik enforcement'a bırakılırsa banlı oyuncu
                        // birkaç saniye sunucuda görünebiliyordu.
                        var state = await _sanctionStateService.RefreshAsync(steamId, ipAddress);
                        if (state.Ban != null && state.Ban.IsActive)
                        {
                            var ban = state.Ban;
                            var kickReason = ban.IsPermanent
                                ? LocalizerHelper.GetWithFallback(Core, "ban_kick_reason_permanent", "You are permanently banned. Reason: {0}", ban.Reason)
                                : LocalizerHelper.GetWithFallback(Core, "ban_kick_reason_minutes", "You are banned for {0} more minutes. Reason: {1}",
                                    (int)Math.Ceiling(Math.Max(1, ban.TimeRemaining?.TotalMinutes ?? 1)), ban.Reason);

                            Core.Scheduler.NextTick(() =>
                            {
                                var target = Core.PlayerManager.GetPlayer(playerId);
                                if (target?.IsValid == true)
                                    target.Kick(kickReason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
                            });
                            return;
                        }

                        var admin = await _adminDbManager.GetAdminAsync(steamId);
                        if (admin != null && admin.IsActive)
                        {
                            var effectiveFlags = await _adminDbManager.GetEffectiveFlagsAsync(steamId);
                            var hasRoot = false;
                            foreach (var flag in effectiveFlags)
                            {
                                if (string.IsNullOrWhiteSpace(flag)) continue;
                                var normalizedFlag = flag.Trim();
                                Core.Permission.AddPermission(steamId, normalizedFlag);
                                if (string.Equals(normalizedFlag, _config.Permissions.AdminRoot, StringComparison.OrdinalIgnoreCase))
                                    hasRoot = true;
                            }
                            if (hasRoot)
                            {
                                foreach (var bypassPermission in _config.Permissions.RootBypassPermissions)
                                {
                                    if (!string.IsNullOrWhiteSpace(bypassPermission))
                                        Core.Permission.AddPermission(steamId, bypassPermission.Trim());
                                }
                            }

                            if (_config.Tags.Enabled)
                            {
                                var tag = _groupDbManager.GetPrimaryGroupNameSync(admin.GroupList) ?? admin.GroupList.FirstOrDefault() ?? "ADMIN";
                                Core.Scheduler.NextTick(() =>
                                {
                                    var target = Core.PlayerManager.GetPlayer(e.PlayerId);
                                    if (target?.IsValid == true)
                                        PlayerUtils.SetScoreTagReliable(Core, target.PlayerID, tag);
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to assign permissions for player {PlayerId}: {Message}", e.PlayerId, ex.Message);
                    }
                });
            }
        });
        _eventRegistrar.OnClientDisconnected(e =>
        {
            if (_connectedPlayersCache.TryGetValue(e.PlayerId, out var cached))
            {
                _connectedPlayersCache.TryRemove(e.PlayerId, out _);
                _recentPlayersTracker.Add(new RecentPlayerInfo(cached.SteamId, cached.Name, cached.Ip, DateTime.UtcNow));
                _ = _playerSessionManager.CloseSessionAsync(cached.SteamId, cached.Name, e.PlayerId, cached.Ip);
                _ = _discord.SendDisconnectNotificationAsync(cached.Name, cached.SteamId, cached.Ip);
            }
        });
        _eventRegistrar.OnRoundStart(ev =>
        {
            EnsureCommandsRegistered();
            _ = RefreshAdminStateForAllOnlinePlayersAsync();
            return HookResult.Continue;
        });
        _eventRegistrar.OnPlayerConnectFull(e =>
        {
            var player = e.Accessor.GetPlayer("userid");
            if (player?.IsValid == true && !player.IsFakeClient)
            {
                _ = _sanctionStateService.RefreshAsync(player.SteamID, player.IPAddress);
            }
            return HookResult.Continue;
        });
        _eventRegistrar.OnPlayerDisconnect(e =>
        {
            var player = e.Accessor.GetPlayer("userid");
            if (player?.IsValid == true && !player.IsFakeClient)
            {
                _recentPlayersTracker.Add(new RecentPlayerInfo(player.SteamID, player.Controller.PlayerName ?? "", player.IPAddress ?? "", DateTime.UtcNow));
            }
            return HookResult.Continue;
        });
        _eventRegistrar.RegisterAll();
    }

    private async Task ReapplyVoiceMuteOnConnectAsync(int playerId, ulong steamId, string? ipAddress)
    {
        try
        {
            var state = await _sanctionStateService.RefreshAsync(steamId, ipAddress);
            if (state.Mute != null && state.Mute.IsActive)
            {
                Core.Scheduler.NextTick(() =>
                {
                    var live = Core.PlayerManager.GetPlayer(playerId);
                    if (live?.IsValid == true && !live.IsFakeClient)
                        live.VoiceFlags = VoiceFlagValue.Muted;
                });
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to reapply voice mute on connect for {SteamId}: {Msg}", steamId, ex.Message);
        }
    }

    private async Task RunAltAccountCheckAsync(ulong steamId, string? playerName, string? ipAddress)
    {
        try
        {
            var report = await _altAccountDetection.CheckAsync(steamId, ipAddress);
            if (report == null || report.IsEmpty)
            {
                return;
            }

            if (_discord != null)
            {
                _ = _discord.SendAltAccountAlertAsync(playerName, steamId, ipAddress, report);
            }

            var displayName = string.IsNullOrWhiteSpace(playerName) ? steamId.ToString() : playerName;
            string chatMessage;
            if (report.Matches.Count > 0)
            {
                var linkedNames = report.Matches
                    .Take(3)
                    .Select(m => string.IsNullOrWhiteSpace(m.Account.PlayerName) ? m.Account.SteamId.ToString() : m.Account.PlayerName)
                    .ToList();
                var namesText = string.Join(", ", linkedNames);
                var extraCount = report.Matches.Count - linkedNames.Count;
                if (extraCount > 0)
                {
                    namesText += $" (+{extraCount})";
                }

                chatMessage = LocalizerHelper.GetWithFallback(
                    Core,
                    "alt_alert_admin_chat",
                    "Possible alt account: {0} shares an IP with sanctioned account(s): {1}",
                    displayName,
                    namesText);
            }
            else
            {
                chatMessage = LocalizerHelper.GetWithFallback(
                    Core,
                    "alt_alert_admin_chat_ipban",
                    "Possible ban evasion: {0} connected from an IP with {1} ban(s) on record.",
                    displayName,
                    report.IpBans.Count);
            }
            var prefix = PluginLocalizer.Get(Core)["prefix"];
            var formatted = $" \x02{prefix}\x01 {chatMessage}";

            Core.Scheduler.NextTick(() =>
            {
                foreach (var p in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
                {
                    if (p.SteamID == steamId)
                    {
                        continue;
                    }

                    if (Core.Permission.PlayerHasPermission(p.SteamID, _config.Permissions.AdminRoot)
                        || Core.Permission.PlayerHasPermission(p.SteamID, _config.Permissions.Ban))
                    {
                        p.SendChat(formatted);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2_Admin] Alt account check failed for {SteamId}: {Message}", steamId, ex.Message);
        }
    }

    private async Task InitializeDatabasesAsync()
    {
        var maxRetries = 3;
        var retryDelayMs = 5000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var testConn = Core.Database.GetConnection("mysql_detailed");
                testConn.Open();
                if (testConn.State != System.Data.ConnectionState.Open)
                {
                    Core.Logger.LogWarningIfEnabled("[CS2Admin] Database connection not open (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                        continue;
                    }
                    Core.Logger.LogWarningIfEnabled("[CS2Admin] Database unavailable after {MaxRetries} attempts, continuing without database features", maxRetries);
                    return;
                }
                testConn.Close();
            }
            catch (Exception ex)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Database connection failed (attempt {Attempt}/{MaxRetries}): {Msg}", attempt, maxRetries, ex.Message);
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs);
                    continue;
                }
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Database unavailable after {MaxRetries} attempts, continuing without database features", maxRetries);
                return;
            }

            try
            {
                using var conn = Core.Database.GetConnection("mysql_detailed");
                MigrationRunner.RunMigrations(conn, Core);
                conn.Close();

                await _groupDbManager.InitializeAsync();
                await _banManager.InitializeAsync();
                await _muteManager.InitializeAsync();
                await _gagManager.InitializeAsync();
                await _warnManager.InitializeAsync();
                await _tagDbManager.InitializeAsync();
                await _adminDbManager.InitializeAsync();
                await _adminLogManager.InitializeAsync();
                await _serverInfoDbManager.InitializeAsync();
                await _discordServerStatusDbManager.InitializeAsync();
                await _discordMessageStateDbManager.InitializeAsync();
                await _discordLinkDbManager.InitializeAsync();
                await _adminPlaytimeDbManager.InitializeAsync();
                await _playerIpDbManager.InitializeAsync();
                await _playerSessionManager.InitializeAsync();
                await _playerNameHistoryManager.InitializeAsync();

                _eventRegistrar?.SetDatabaseReady(true);
                await _chatTagConfigManager.SyncWithGroupsAsync(_groupDbManager);
                await RefreshAdminStateForAllOnlinePlayersAsync();
                StartAdminPlaytimeTracking();
                StartAdminTimeAutoSend();
                _discord.StartBackgroundUpdates(_playerSessionManager, _discordServerStatusDbManager, _discordMessageStateDbManager);
                _commandBlockerReady = true;
                Core.Scheduler.NextTick(() =>
                {
                    foreach (var online in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid && !p.IsFakeClient))
                        _commandBlocker.WarmUp(online.SteamID);
                });

                break;
            }
            catch (Exception ex)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Database init failed (attempt {Attempt}/{MaxRetries}): {Msg}", attempt, maxRetries, ex.Message);
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs);
                }
                else
                {
                    Core.Logger.LogWarningIfEnabled("[CS2Admin] Database init failed after {MaxRetries} attempts, continuing without database features", maxRetries);
                }
            }
        }
    }

    private void StartAdminPlaytimeTracking()
    {
        var interval = Math.Max(1, _config.AdminPlaytime.TrackIntervalMinutes);
        _adminPlaytimeTimer?.Dispose();
        _adminPlaytimeTimer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _isTrackingAdminPlaytime, 1) == 1) return;
            Core.Scheduler.NextTick(() =>
            {
                var snapshots = Core.PlayerManager.GetAllPlayers()
                    .Where(p => p.IsValid && !p.IsFakeClient)
                    .Where(p => p.Controller.TeamNum >= 2)
                    .Select(p => new AdminPlaytimeSnapshot(p.SteamID, p.Controller.PlayerName ?? PluginLocalizer.Get(Core)["player_fallback_name", p.PlayerID]))
                    .ToList();
                _ = Task.Run(async () =>
                {
                    try { await _adminPlaytimeDbManager.TrackOnlineAdminsAsync(snapshots, interval); }
                    finally { Interlocked.Exchange(ref _isTrackingAdminPlaytime, 0); }
                });
            });
        }, null, TimeSpan.FromMinutes(interval), TimeSpan.FromMinutes(interval));
    }

    private void StartAdminTimeAutoSend()
    {
        var cfg = _config.AdminPlaytime;
        if (cfg.AutoSendDayOfWeek <= 0)
            return;

        var firstDelayMs = CalculateDayOfWeekDelay(cfg.AutoSendDayOfWeek);
        var periodMs = (int)TimeSpan.FromDays(7).TotalMilliseconds;

        _adminTimeAutoSendTimer?.Dispose();
        _adminTimeAutoSendTimer = new Timer(async _ =>
        {
            try
            {
                var topAdmins = await _adminPlaytimeDbManager.GetTopAdminsAsync(cfg.DiscordTopLimit);
                if (topAdmins.Count == 0)
                    return;

                var allDbAdmins = await _adminDbManager.GetAllAdminsAsync();
                var playedSteamIds = new HashSet<ulong>(topAdmins.Select(a => a.SteamId));
                var zeroPlaytimeAdmins = allDbAdmins
                    .Where(a => !playedSteamIds.Contains(a.SteamId))
                    .Select(a => string.IsNullOrWhiteSpace(a.Name) ? a.SteamId.ToString() : a.Name)
                    .ToList();

                await _discord.SendAdminTimeNotificationAsync(topAdmins, zeroPlaytimeAdmins);
                Core.Logger.LogInformationIfEnabled("[CS2Admin] Admin playtime auto-sent to Discord ({Count} admins)", topAdmins.Count);

                if (cfg.AutoSendResetAfterSend)
                {
                    await _adminPlaytimeDbManager.ResetAllAsync();
                    Core.Logger.LogInformationIfEnabled("[CS2Admin] Admin playtime reset after auto-send");
                }
            }
            catch (Exception ex)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Admin time auto-send failed: {Msg}", ex.Message);
            }
        }, null, firstDelayMs, periodMs);
    }

    private void StartPeriodicUpdateCheck(string currentVersion)
    {
        _periodicUpdateTimer?.Dispose();
        _periodicUpdateTimer = new Timer(_ =>
        {
            _ = global::CS2_Admin.Utils.AutoUpdater.CheckForUpdatesAsync(Core, currentVersion);
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    private async Task RefreshAdminStateForAllOnlinePlayersAsync()
    {
        try
        {
            var groups = await _groupDbManager.GetAllGroupsAsync();
            var admins = await _adminDbManager.GetAllAdminsAsync();
            var adminsBySteamId = admins.ToDictionary(a => a.SteamId, a => a);

            var managedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                foreach (var flag in SplitPermissions(group.Flags))
                    managedPermissions.Add(flag);
            }
            foreach (var admin in admins)
            {
                foreach (var flag in SplitPermissions(admin.Flags))
                    managedPermissions.Add(flag);
            }
            foreach (var bypassPermission in _config.Permissions.RootBypassPermissions)
            {
                if (!string.IsNullOrWhiteSpace(bypassPermission))
                    managedPermissions.Add(bypassPermission.Trim());
            }

            var onlinePlayers = Core.PlayerManager
                .GetAllPlayers()
                .Where(p => p.IsValid && !p.IsFakeClient)
                .Select(p => (p.PlayerID, p.SteamID))
                .ToList();

            var resolvedTags = new Dictionary<int, string>();

            foreach (var snapshot in onlinePlayers)
            {
                foreach (var permission in managedPermissions)
                    Core.Permission.RemovePermission(snapshot.SteamID, permission);

                var effectiveFlags = await _adminDbManager.GetEffectiveFlagsAsync(snapshot.SteamID);
                var hasRoot = false;

                foreach (var flag in effectiveFlags)
                {
                    if (string.IsNullOrWhiteSpace(flag)) continue;
                    var normalizedFlag = flag.Trim();
                    Core.Permission.AddPermission(snapshot.SteamID, normalizedFlag);
                    if (string.Equals(normalizedFlag, _config.Permissions.AdminRoot, StringComparison.OrdinalIgnoreCase))
                        hasRoot = true;
                }

                if (hasRoot)
                {
                    foreach (var bypassPermission in _config.Permissions.RootBypassPermissions)
                    {
                        if (!string.IsNullOrWhiteSpace(bypassPermission))
                            Core.Permission.AddPermission(snapshot.SteamID, bypassPermission.Trim());
                    }
                }

                if (_config.Tags.Enabled)
                {
                    adminsBySteamId.TryGetValue(snapshot.SteamID, out var activeAdmin);
                    var adminGroupTag = activeAdmin != null && activeAdmin.IsActive
                        ? _groupDbManager.GetPrimaryGroupNameSync(activeAdmin.GroupList) ?? activeAdmin.GroupList.FirstOrDefault()
                        : null;

                    if (string.IsNullOrWhiteSpace(adminGroupTag) &&
                        Core.Permission.PlayerHasPermission(snapshot.SteamID, _config.Permissions.AdminRoot))
                    {
                        adminGroupTag = "ADMIN";
                    }

                    resolvedTags[snapshot.PlayerID] = string.IsNullOrWhiteSpace(adminGroupTag)
                        ? _config.Tags.PlayerTag
                        : adminGroupTag;
                }
            }

            Core.Scheduler.NextTick(() =>
            {
                if (!_config.Tags.Enabled) return;
                foreach (var pair in resolvedTags)
                    PlayerUtils.SetScoreTagReliable(Core, pair.Key, pair.Value);
            });
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to refresh admin state: {Message}", ex.Message);
        }
    }

    private static IEnumerable<string> SplitPermissions(string rawPermissions)
    {
        return rawPermissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));
    }

    private static int CalculateDayOfWeekDelay(int targetDay)
    {
        var target = targetDay switch
        {
            1 => DayOfWeek.Monday,
            2 => DayOfWeek.Tuesday,
            3 => DayOfWeek.Wednesday,
            4 => DayOfWeek.Thursday,
            5 => DayOfWeek.Friday,
            6 => DayOfWeek.Saturday,
            7 => DayOfWeek.Sunday,
            _ => DayOfWeek.Sunday
        };

        var now = DateTime.UtcNow;
        var daysUntil = ((int)target - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0)
            daysUntil = 7;
        return (int)TimeSpan.FromDays(daysUntil).TotalMilliseconds;
    }
}
