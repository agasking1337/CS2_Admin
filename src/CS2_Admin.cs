using CS2_Admin.Commands;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Events;
using CS2_Admin.Menu;
using CS2_Admin.Utils;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Translation;
using System.Globalization;
using System.Reflection;
using System.Data;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace CS2_Admin;

[PluginMetadata(Id = "CS2_Admin", Version = "1.0.4", Name = "CS2_Admin", Author = "CanDaysa", Description = "Comprehensive admin plugin for CS2.")]
public partial class CS2_Admin : BasePlugin
{
    private PluginConfig _config = null!;
    public PluginConfig Config => _config;
    public AdminMenuManager AdminMenuManager { get; private set; } = null!;

    // Database managers
    private BanManager _banManager = null!;
    private MuteManager _muteManager = null!;
    private GagManager _gagManager = null!;
    private WarnManager _warnManager = null!;
    private GroupDbManager _groupDbManager = null!;
    private AdminDbManager _adminDbManager = null!;
    private AdminLogManager _adminLogManager = null!;
    private ServerInfoDbManager _serverInfoDbManager = null!;
    private AdminPlaytimeDbManager _adminPlaytimeDbManager = null!;
    private PlayerIpDbManager _playerIpDbManager = null!;

    // Command handlers
    private BanCommands _banCommands = null!;
    private MuteCommands _muteCommands = null!;
    private PlayerCommands _playerCommands = null!;
    private ServerCommands _serverCommands = null!;
    private AdminCommands _adminCommands = null!;
    private ChatCommands _chatCommands = null!;
    private WarnCommands _warnCommands = null!;
    private AdminPlaytimeCommands _adminPlaytimeCommands = null!;

    // Event handlers
    private EventHandlers _eventHandlers = null!;

    // Utils
    private DiscordWebhook _discord = null!;
    private RecentPlayersTracker _recentPlayersTracker = null!;
    private Timer? _adminPlaytimeTimer;
    private int _isTrackingAdminPlaytime;
    private string? _resolvedTranslationDirectory;
    private static readonly HashSet<string> BlockedCommandAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "groups"
    };

    public CS2_Admin(ISwiftlyCore core) : base(core)
    {
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void Load(bool hotReload)
    {
        // Load configuration
        LoadConfiguration();
        TryApplyConfiguredLocalizer(_config.Language);

        Core.Logger.LogInformationIfEnabled("[CS2Admin] Loading plugin...");

        // Initialize database managers
        InitializeDatabaseManagers();

        // Initialize Admin Menu Manager
        AdminMenuManager = new AdminMenuManager(Core, Config, _warnManager, _adminDbManager, _groupDbManager, _adminLogManager, _adminPlaytimeDbManager);

        // Initialize utilities
        _discord = new DiscordWebhook(Core, Config.Discord);
        _adminLogManager.SetDiscordWebhook(_discord);

        // Initialize command handlers
        InitializeCommandHandlers();

        // Initialize event handlers
        InitializeEventHandlers();

        // Register commands
        RegisterCommands();

        // Register events
        RegisterEvents();

        // Initialize databases
        _ = InitializeDatabasesAsync();

        Core.Logger.LogInformationIfEnabled("[CS2Admin] Plugin loaded successfully!");
    }

    public override void Unload()
    {
        Core.Logger.LogInformationIfEnabled("[CS2Admin] Unloading plugin...");
        _eventHandlers?.UnregisterHooks();
        _adminPlaytimeTimer?.Dispose();
    }

    private void LoadConfiguration()
    {
        _config = new PluginConfig();

        EnsureVersionedConfigFile("config.json", "CS2Admin", PluginConfig.CurrentVersion);
        EnsureVersionedConfigFile("commands.json", "CS2AdminCommands", CommandsConfig.CurrentVersion);
        EnsureVersionedConfigFile("permissions.json", "CS2AdminPermissions", PermissionsConfig.CurrentVersion);
        EnsureVersionedConfigFile("maps.json", "CS2AdminMaps", MapsFileConfig.CurrentVersion);

        try
        {
            // Initialize config file with model - this will auto-create config.json if it doesn't exist
            Core.Configuration
                .InitializeJsonWithModel<PluginConfig>("config.json", "CS2Admin")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("config.json"), optional: false, reloadOnChange: true));

            // Bind configuration to our model. Support both:
            // 1) { "CS2Admin": { ... } }
            // 2) { ... }  (root-level keys)
            var pluginSection = Core.Configuration.Manager.GetSection("CS2Admin");
            if (pluginSection.GetChildren().Any())
            {
                pluginSection.Bind(_config);
            }
            else
            {
                Core.Configuration.Manager.Bind(_config);
            }

            // Resolve language defensively from multiple config layouts.
            // Priority: root Language > CS2_Admin.Language > CS2Admin.Language > bound value.
            _config.Language = ResolveConfiguredLanguage(Core.Configuration.GetConfigPath("config.json"), _config.Language);

            ApplyLanguageCulture(_config.Language);

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Configuration loaded from {Path}", Core.Configuration.GetConfigPath("config.json"));
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Config language set to: {Language}", _config.Language);
        }
        catch (Exception ex)
        {
            _config.Language = "en";
            ApplyLanguageCulture(_config.Language);
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to fully load config.json, continuing with defaults/partial values: {Message}", ex.Message);
        }

        try
        {
            // Load command aliases from a dedicated config file (commands.json)
            Core.Configuration
                .InitializeJsonWithModel<CommandsConfig>("commands.json", "CS2AdminCommands")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("commands.json"), optional: false, reloadOnChange: true));
            var commandsConfig = new CommandsConfig();
            Core.Configuration.Manager.GetSection("CS2AdminCommands").Bind(commandsConfig);
            _config.Commands = commandsConfig;
            SanitizeCommandAliases();

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Command aliases loaded from {Path}", Core.Configuration.GetConfigPath("commands.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load commands.json, using command aliases from main/default config: {Message}", ex.Message);
            SanitizeCommandAliases();
        }

        try
        {
            // Load permissions from a dedicated config file (permissions.json)
            Core.Configuration
                .InitializeJsonWithModel<PermissionsConfig>("permissions.json", "CS2AdminPermissions")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("permissions.json"), optional: false, reloadOnChange: true));
            var permissionsConfig = new PermissionsConfig();
            Core.Configuration.Manager.GetSection("CS2AdminPermissions").Bind(permissionsConfig);
            _config.Permissions = permissionsConfig;

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Permissions loaded from {Path}", Core.Configuration.GetConfigPath("permissions.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load permissions.json, using default permissions: {Message}", ex.Message);
        }

        try
        {
            Core.Configuration
                .InitializeJsonWithModel<MapsFileConfig>("maps.json", "CS2AdminMaps")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("maps.json"), optional: false, reloadOnChange: true));

            var mapsFileConfig = new MapsFileConfig();
            Core.Configuration.Manager.GetSection("CS2AdminMaps").Bind(mapsFileConfig);
            _config.MapsFile = mapsFileConfig;

            if (mapsFileConfig.Maps.Count > 0)
            {
                _config.GameMaps.Maps = mapsFileConfig.Maps;
            }

            if (mapsFileConfig.WorkshopMaps.Count > 0)
            {
                _config.WorkshopMaps.Maps = mapsFileConfig.WorkshopMaps;
            }

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Maps loaded from {Path}", Core.Configuration.GetConfigPath("maps.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load maps.json, using default maps: {Message}", ex.Message);
        }

        CleanupLegacyCommandsFromConfig();
        DebugSettings.LoggingEnabled = _config.Debug.Enabled;
    }

    private void EnsureVersionedConfigFile(string fileName, string sectionName, int expectedVersion)
    {
        var filePath = Core.Configuration.GetConfigPath(fileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
            {
                RecreateConfigFile(filePath, fileName, expectedVersion, "root is not a JSON object");
                return;
            }

            if (!TryReadVersionFromNode(root, sectionName, out var currentVersion) || currentVersion != expectedVersion)
            {
                var currentText = TryReadVersionFromNode(root, sectionName, out var parsedVersion)
                    ? parsedVersion.ToString(CultureInfo.InvariantCulture)
                    : "missing";
                RecreateConfigFile(filePath, fileName, expectedVersion, $"found {currentText}");
            }
        }
        catch (Exception ex)
        {
            RecreateConfigFile(filePath, fileName, expectedVersion, ex.Message);
        }
    }

    private void RecreateConfigFile(string filePath, string fileName, int expectedVersion, string reason)
    {
        try
        {
            File.Delete(filePath);
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] {File} version mismatch/corruption ({Reason}). File deleted and will be regenerated with version {Version}.",
                fileName,
                reason,
                expectedVersion);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Failed to delete {File} for version reset: {Message}",
                fileName,
                ex.Message);
        }
    }

    private static bool TryReadVersionFromNode(JsonObject root, string sectionName, out int version)
    {
        version = 0;

        if (TryParseVersionNode(root["Version"], out version))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sectionName) && root[sectionName] is JsonObject section && TryParseVersionNode(section["Version"], out version))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseVersionNode(JsonNode? node, out int version)
    {
        version = 0;
        if (node == null)
        {
            return false;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
            {
                version = intValue;
                return true;
            }

            if (value.TryGetValue<string>(out var textValue) &&
                int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                version = parsed;
                return true;
            }
        }

        return false;
    }

    private void InitializeDatabaseManagers()
    {
        _groupDbManager = new GroupDbManager(Core);
        _banManager = new BanManager(Core);
        _muteManager = new MuteManager(Core);
        _gagManager = new GagManager(Core);
        _warnManager = new WarnManager(Core);
        _adminDbManager = new AdminDbManager(Core, _groupDbManager);
        _adminLogManager = new AdminLogManager(Core);
        _serverInfoDbManager = new ServerInfoDbManager(Core);
        _adminPlaytimeDbManager = new AdminPlaytimeDbManager(Core, _adminDbManager);
        _playerIpDbManager = new PlayerIpDbManager(Core);
        _recentPlayersTracker = new RecentPlayersTracker();
    }

    private void InitializeCommandHandlers()
    {
        _banCommands = new BanCommands(
            Core,
            _banManager,
            _muteManager,
            _gagManager,
            _warnManager,
            _adminDbManager,
            _adminLogManager,
            _playerIpDbManager,
            _recentPlayersTracker,
            _discord,
            _config.Permissions,
            _config.Commands,
            _config.Messages,
            _config.Sanctions,
            _config.MultiServer);
        _muteCommands = new MuteCommands(
            Core, 
            _muteManager, 
            _gagManager, 
            _adminDbManager,
            _adminLogManager,
            _discord, 
            _config.Commands,
            _config.Permissions.Mute,
            _config.Permissions.Gag,
            _config.Permissions.Silence,
            _config.Permissions.AdminRoot,
            _config.Messages);
        _warnCommands = new WarnCommands(
            Core,
            _warnManager,
            _adminDbManager,
            _adminLogManager,
            _discord,
            _config.Permissions.Warn,
            _config.Permissions.Unwarn,
            _config.Permissions.AdminRoot,
            _config.Messages,
            _config.Sanctions,
            _config.Commands.Warn,
            _config.Commands.Unwarn);
        _playerCommands = new PlayerCommands(Core, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _config.MultiServer);
        _serverCommands = new ServerCommands(Core, _adminLogManager, _config.Permissions, _config.GameMaps, _config.WorkshopMaps, _config.Commands);
        _adminCommands = new AdminCommands(Core, _adminDbManager, _groupDbManager, _adminLogManager, _config.Permissions, _config.Tags, _config.Commands, AdminMenuManager);
        _chatCommands = new ChatCommands(
            Core,
            _adminLogManager,
            _discord,
            _config.Permissions,
            _config.Messages,
            _config.Commands,
            _config.Sanctions);
        _adminPlaytimeCommands = new AdminPlaytimeCommands(Core, _adminPlaytimeDbManager, _adminLogManager, _discord, _config.Permissions, _config.AdminPlaytime);
    }

    private void InitializeEventHandlers()
    {
        _eventHandlers = new EventHandlers(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _groupDbManager, _playerIpDbManager, _recentPlayersTracker, _config.Permissions, _config.Tags, _config.Commands, _config.MultiServer);
        _eventHandlers.SetDatabaseReady(false);
        _eventHandlers.OnPlayerDisconnected += playerId => _playerCommands.OnPlayerDisconnect(playerId);
        _eventHandlers.RegisterHooks();
    }

    private async Task InitializeDatabasesAsync()
    {
        if (!CanConnectToDatabase())
        {
            _eventHandlers.SetDatabaseReady(false);
            return;
        }

        await _groupDbManager.InitializeAsync();
        await _banManager.InitializeAsync();
        await _muteManager.InitializeAsync();
        await _gagManager.InitializeAsync();
        await _warnManager.InitializeAsync();
        await _adminDbManager.InitializeAsync();
        await _adminLogManager.InitializeAsync();
        await _serverInfoDbManager.InitializeAsync();
        await _adminPlaytimeDbManager.InitializeAsync();
        await _playerIpDbManager.InitializeAsync();

        if (!ValidateRequiredTables())
        {
            _eventHandlers.SetDatabaseReady(false);
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Required DB tables are missing. Database-backed features will stay disabled until migrations are fixed.");
            return;
        }

        _eventHandlers.SetDatabaseReady(true);
        StartAdminPlaytimeTracking();
        await _eventHandlers.RefreshAdminStateForAllOnlinePlayersAsync();

        Core.Logger.LogInformationIfEnabled(
            "[CS2Admin] Server IP: {Ip} Port: {Port}",
            ServerIdentity.GetIp(Core),
            ServerIdentity.GetPort(Core));
    }

    private bool CanConnectToDatabase()
    {
        try
        {
            using var connection = Core.Database.GetConnection("admins");
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Database connection is unavailable. Plugin will continue with limited functionality. Details: {Message}",
                ex.Message);
            return false;
        }
    }

    private bool ValidateRequiredTables()
    {
        var requiredTables = new[]
        {
            "admin_admins",
            "admin_groups",
            "admin_bans",
            "admin_mutes",
            "admin_gags",
            "admin_warns",
            "admin_log",
            "admin_playtime",
            "admin_player_ips",
            "admin_player_ip_history"
        };

        try
        {
            using var connection = Core.Database.GetConnection("admins");
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            foreach (var tableName in requiredTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT 1 FROM `{tableName}` LIMIT 1";
                _ = cmd.ExecuteScalar();
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Database schema validation failed: {Message}",
                ex.Message);
            return false;
        }
    }

    private void ApplyLanguageCulture(string language)
    {
        var cultureName = language.ToLowerInvariant() switch
        {
            "tr" => "tr-TR",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "it" => "it-IT",
            "el" => "el-GR",
            "ru" => "ru-RU",
            "bg" => "bg-BG",
            "hu" => "hu-HU",
            _ => "en-US"
        };

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to apply language culture '{Culture}': {Message}", cultureName, ex.Message);
        }
    }

    private string ResolveConfiguredLanguage(string configPath, string? fallbackLanguage)
    {
        var fromRoot = Core.Configuration.Manager["Language"];
        if (!string.IsNullOrWhiteSpace(fromRoot))
        {
            return NormalizeSupportedLanguage(fromRoot);
        }

        var fromAltSection = Core.Configuration.Manager["CS2_Admin:Language"];
        if (!string.IsNullOrWhiteSpace(fromAltSection))
        {
            return NormalizeSupportedLanguage(fromAltSection);
        }

        var fromMainSection = Core.Configuration.Manager["CS2Admin:Language"];
        if (!string.IsNullOrWhiteSpace(fromMainSection))
        {
            return NormalizeSupportedLanguage(fromMainSection);
        }

        try
        {
            if (File.Exists(configPath))
            {
                var raw = File.ReadAllText(configPath);
                var node = JsonNode.Parse(raw) as JsonObject;
                if (node != null)
                {
                    if (node["Language"]?.GetValue<string>() is { } rawRoot && !string.IsNullOrWhiteSpace(rawRoot))
                    {
                        return NormalizeSupportedLanguage(rawRoot);
                    }

                    if (node["CS2_Admin"] is JsonObject cs2AdminAlt
                        && cs2AdminAlt["Language"]?.GetValue<string>() is { } rawAlt
                        && !string.IsNullOrWhiteSpace(rawAlt))
                    {
                        return NormalizeSupportedLanguage(rawAlt);
                    }

                    if (node["CS2Admin"] is JsonObject cs2Admin
                        && cs2Admin["Language"]?.GetValue<string>() is { } rawSection
                        && !string.IsNullOrWhiteSpace(rawSection))
                    {
                        return NormalizeSupportedLanguage(rawSection);
                    }
                }
            }
        }
        catch
        {
            // Keep fallback language when raw parsing fails.
        }

        return NormalizeSupportedLanguage(fallbackLanguage);
    }

    private static string NormalizeSupportedLanguage(string? language)
    {
        var normalized = (language ?? "en").Trim().ToLowerInvariant();
        return normalized switch
        {
            "tr" or "de" or "fr" or "it" or "el" or "ru" or "bg" or "hu" => normalized,
            _ => "en"
        };
    }

    private void TryApplyConfiguredLocalizer(string language)
    {
        try
        {
            var resourceDir = ResolveTranslationDirectory(language);

            if (!Directory.Exists(resourceDir))
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Translation folder not found. Expected: {Path}", resourceDir);
                return;
            }

            var fileLocalizer = JsonFileLocalizer.TryCreate(resourceDir, language);
            if (fileLocalizer != null)
            {
                PluginLocalizer.SetOverride(fileLocalizer);
                Core.Logger.LogInformationIfEnabled("[CS2Admin] Plugin localizer override loaded from: {Path}", resourceDir);
            }

            var coreAssembly = typeof(ISwiftlyCore).Assembly;
            var translationFactoryType = coreAssembly.GetType("SwiftlyS2.Core.Translations.TranslationFactory");
            var createResourceMethod = translationFactoryType?.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            var createLocalizerMethod = translationFactoryType?.GetMethod("CreateLocalizer", BindingFlags.Public | BindingFlags.Static);
            if (createResourceMethod == null || createLocalizerMethod == null)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Translation factory methods were not found in Swiftly runtime.");
                return;
            }

            var translationResource = createResourceMethod.Invoke(null, new object[] { resourceDir });
            if (translationResource == null)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to create translation resource from {Path}", resourceDir);
                return;
            }

            var swiftLanguage = ToSwiftLanguage(language);
            var localizer = createLocalizerMethod.Invoke(null, new object[] { translationResource, swiftLanguage });
            if (localizer == null)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to create localizer for language {Language}", language);
                return;
            }

            var coreType = Core.GetType();
            var localizerProperty = coreType.GetProperty("Localizer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (localizerProperty?.CanWrite != true)
            {
                var localizerField = coreType.GetField("<Localizer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (localizerField != null)
                {
                    localizerField.SetValue(Core, localizer);
                }
                else
                {
                    Core.Logger.LogWarningIfEnabled("[CS2Admin] PluginLocalizer.Get(Core) property is not writable in current Swiftly runtime.");
                }
            }
            else
            {
                localizerProperty.SetValue(Core, localizer);
            }
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Runtime localizer forced to language: {Language}", language);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Localizer probe menu_admin_title: {Text}", PluginLocalizer.Get(Core)["menu_admin_title"]);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to force runtime localizer to '{Language}': {Message}", language, ex.Message);
        }
    }

    private string ResolveTranslationDirectory(string language)
    {
        var normalizedLanguage = (language ?? "en").Trim().ToLowerInvariant();
        var requestedFile = $"{normalizedLanguage}.jsonc";

        if (!string.IsNullOrWhiteSpace(_resolvedTranslationDirectory)
            && Directory.Exists(_resolvedTranslationDirectory)
            && File.Exists(Path.Combine(_resolvedTranslationDirectory, "en.jsonc"))
            && File.Exists(Path.Combine(_resolvedTranslationDirectory, requestedFile)))
        {
            return _resolvedTranslationDirectory;
        }

        // Always prefer embedded translations shipped with this DLL, so stale on-disk files
        // cannot force English unexpectedly.
        var embedded = ExtractEmbeddedTranslationsToPluginData();
        if (!string.IsNullOrWhiteSpace(embedded)
            && File.Exists(Path.Combine(embedded, "en.jsonc"))
            && File.Exists(Path.Combine(embedded, requestedFile)))
        {
            _resolvedTranslationDirectory = embedded;
            return embedded;
        }

        var candidates = new List<string>();

        candidates.Add(Path.Combine(Core.PluginPath, "resources", "translations"));

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            candidates.Add(Path.Combine(assemblyDir, "resources", "translations"));
            candidates.Add(Path.Combine(assemblyDir, "..", "resources", "translations"));
        }

        candidates.Add(Path.Combine(Core.PluginDataDirectory, "translations"));

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full)
                    && File.Exists(Path.Combine(full, "en.jsonc"))
                    && File.Exists(Path.Combine(full, requestedFile)))
                {
                    _resolvedTranslationDirectory = full;
                    return full;
                }
            }
            catch
            {
                // Ignore invalid path candidates.
            }
        }

        // No usable folder found: extract embedded translations to plugin data directory.
        var extracted = ExtractEmbeddedTranslationsToPluginData();
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            _resolvedTranslationDirectory = extracted;
            return extracted;
        }

        return Path.Combine(Core.PluginPath, "resources", "translations");
    }

    private string? ExtractEmbeddedTranslationsToPluginData()
    {
        try
        {
            var outputDir = Path.Combine(Core.PluginDataDirectory, "translations");
            Directory.CreateDirectory(outputDir);

            var asm = Assembly.GetExecutingAssembly();
            var resources = asm.GetManifestResourceNames()
                .Where(x => x.StartsWith("CS2_Admin.Translations.", StringComparison.OrdinalIgnoreCase)
                            && x.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (resources.Count == 0)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] No embedded translation resources were found in assembly.");
                return null;
            }

            foreach (var resourceName in resources)
            {
                var fileName = resourceName["CS2_Admin.Translations.".Length..];
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = reader.ReadToEnd();
                File.WriteAllText(Path.Combine(outputDir, fileName), content, Encoding.UTF8);
            }

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Extracted embedded translations to: {Path}", outputDir);
            return outputDir;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to extract embedded translations: {Message}", ex.Message);
            return null;
        }
    }

    private static Language ToSwiftLanguage(string language)
    {
        return (language ?? "en").Trim().ToLowerInvariant() switch
        {
            "tr" => Language.Turkish,
            "de" => Language.German,
            "fr" => Language.French,
            "it" => Language.Italian,
            "el" => Language.Greek,
            "ru" => Language.Russian,
            "bg" => Language.Bulgarian,
            "hu" => Language.Hungarian,
            _ => Language.English
        };
    }

    private void RegisterCommands()
    {
        // Admin root/menu commands
        foreach (var cmd in _config.Commands.AdminRoot)
            RegisterCommand(cmd, _adminCommands.OnAdminRootCommand);
        foreach (var cmd in _config.Commands.AdminMenu)
            RegisterCommand(cmd, _adminCommands.OnAdminRootCommand);

        // Admin communication commands
        foreach (var cmd in _config.Commands.Asay)
            RegisterCommand(cmd, _chatCommands.OnAsayCommand);
        foreach (var cmd in _config.Commands.Say)
            RegisterCommand(cmd, _chatCommands.OnSayCommand);
        foreach (var cmd in _config.Commands.Psay)
            RegisterCommand(cmd, _chatCommands.OnPsayCommand);
        foreach (var cmd in _config.Commands.Csay)
            RegisterCommand(cmd, _chatCommands.OnCsayCommand);
        foreach (var cmd in _config.Commands.Hsay)
            RegisterCommand(cmd, _chatCommands.OnHsayCommand);
        foreach (var cmd in _config.Commands.CallAdmin)
            RegisterCommand(cmd, _chatCommands.OnCallAdminCommand);
        foreach (var cmd in _config.Commands.Report)
            RegisterCommand(cmd, _chatCommands.OnReportCommand);
        foreach (var cmd in _config.Commands.AdminTime)
            RegisterCommand(cmd, _adminPlaytimeCommands.OnAdminTimeCommand);
        foreach (var cmd in _config.Commands.AdminTimeSend)
            RegisterCommand(cmd, _adminPlaytimeCommands.OnAdminTimeSendCommand);

        // Ban commands
        foreach (var cmd in _config.Commands.Ban)
            RegisterCommand(cmd, _banCommands.OnBanCommand);
        foreach (var cmd in _config.Commands.IpBan)
            RegisterCommand(cmd, _banCommands.OnIpBanCommand);
        foreach (var cmd in _config.Commands.LastBan)
            RegisterCommand(cmd, _banCommands.OnLastBanCommand);
        foreach (var cmd in _config.Commands.AddBan)
            RegisterCommand(cmd, _banCommands.OnAddBanCommand);
        foreach (var cmd in _config.Commands.Unban)
            RegisterCommand(cmd, _banCommands.OnUnbanCommand);
        foreach (var cmd in _config.Commands.Warn)
            RegisterCommand(cmd, _warnCommands.OnWarnCommand);
        foreach (var cmd in _config.Commands.Unwarn)
            RegisterCommand(cmd, _warnCommands.OnUnwarnCommand);

        // Mute/Gag commands
        foreach (var cmd in _config.Commands.Mute)
            RegisterCommand(cmd, _muteCommands.OnMuteCommand);
        foreach (var cmd in _config.Commands.Unmute)
            RegisterCommand(cmd, _muteCommands.OnUnmuteCommand);
        foreach (var cmd in _config.Commands.Gag)
            RegisterCommand(cmd, _muteCommands.OnGagCommand);
        foreach (var cmd in _config.Commands.Ungag)
            RegisterCommand(cmd, _muteCommands.OnUngagCommand);
        foreach (var cmd in _config.Commands.Silence)
            RegisterCommand(cmd, _muteCommands.OnSilenceCommand);
        foreach (var cmd in _config.Commands.Unsilence)
            RegisterCommand(cmd, _muteCommands.OnUnsilenceCommand);

        // Player commands
        foreach (var cmd in _config.Commands.Kick)
            RegisterCommand(cmd, _playerCommands.OnKickCommand);
        foreach (var cmd in _config.Commands.Slap)
            RegisterCommand(cmd, _playerCommands.OnSlapCommand);
        foreach (var cmd in _config.Commands.Slay)
            RegisterCommand(cmd, _playerCommands.OnSlayCommand);
        foreach (var cmd in _config.Commands.Respawn)
            RegisterCommand(cmd, _playerCommands.OnRespawnCommand);
        foreach (var cmd in _config.Commands.ChangeTeam)
            RegisterCommand(cmd, _playerCommands.OnTeamCommand);
        foreach (var cmd in _config.Commands.NoClip)
            RegisterCommand(cmd, _playerCommands.OnNoclipCommand);
        foreach (var cmd in _config.Commands.Goto)
            RegisterCommand(cmd, _playerCommands.OnGotoCommand);
        foreach (var cmd in _config.Commands.Bring)
            RegisterCommand(cmd, _playerCommands.OnBringCommand);
        foreach (var cmd in _config.Commands.Freeze)
            RegisterCommand(cmd, _playerCommands.OnFreezeCommand);
        foreach (var cmd in _config.Commands.Unfreeze)
            RegisterCommand(cmd, _playerCommands.OnUnfreezeCommand);
        foreach (var cmd in _config.Commands.Resize)
            RegisterCommand(cmd, _playerCommands.OnResizeCommand);
        foreach (var cmd in _config.Commands.Drug)
            RegisterCommand(cmd, _playerCommands.OnDrugCommand);
        foreach (var cmd in _config.Commands.Burn)
            RegisterCommand(cmd, _playerCommands.OnBurnCommand);
        foreach (var cmd in _config.Commands.Disarm)
            RegisterCommand(cmd, _playerCommands.OnDisarmCommand);
        foreach (var cmd in _config.Commands.Speed)
            RegisterCommand(cmd, _playerCommands.OnSpeedCommand);
        foreach (var cmd in _config.Commands.Gravity)
            RegisterCommand(cmd, _playerCommands.OnGravityCommand);
        foreach (var cmd in _config.Commands.Rename)
            RegisterCommand(cmd, _playerCommands.OnRenameCommand);
        foreach (var cmd in _config.Commands.Hp)
            RegisterCommand(cmd, _playerCommands.OnHpCommand);
        foreach (var cmd in _config.Commands.Money)
            RegisterCommand(cmd, _playerCommands.OnMoneyCommand);
        foreach (var cmd in _config.Commands.Give)
            RegisterCommand(cmd, _playerCommands.OnGiveCommand);
        foreach (var cmd in _config.Commands.Who)
            RegisterCommand(cmd, _playerCommands.OnWhoCommand);

        // Server commands
        foreach (var cmd in _config.Commands.ChangeMap)
            RegisterCommand(cmd, _serverCommands.OnMapCommand);
        foreach (var cmd in _config.Commands.ChangeWSMap)
            RegisterCommand(cmd, _serverCommands.OnWSMapCommand);
        foreach (var cmd in _config.Commands.RestartGame)
            RegisterCommand(cmd, _serverCommands.OnRestartCommand);
        foreach (var cmd in _config.Commands.HeadshotOn)
            RegisterCommand(cmd, _serverCommands.OnHeadshotOnCommand);
        foreach (var cmd in _config.Commands.HeadshotOff)
            RegisterCommand(cmd, _serverCommands.OnHeadshotOffCommand);
        foreach (var cmd in _config.Commands.BunnyOn)
            RegisterCommand(cmd, _serverCommands.OnBunnyOnCommand);
        foreach (var cmd in _config.Commands.BunnyOff)
            RegisterCommand(cmd, _serverCommands.OnBunnyOffCommand);
        foreach (var cmd in _config.Commands.RespawnOn)
            RegisterCommand(cmd, _serverCommands.OnRespawnOnCommand);
        foreach (var cmd in _config.Commands.RespawnOff)
            RegisterCommand(cmd, _serverCommands.OnRespawnOffCommand);
        foreach (var cmd in _config.Commands.Rcon)
            RegisterCommand(cmd, _serverCommands.OnRconCommand);
        foreach (var cmd in _config.Commands.Cvar)
            RegisterCommand(cmd, _serverCommands.OnCvarCommand);
        foreach (var cmd in _config.Commands.Vote)
            RegisterCommand(cmd, _serverCommands.OnVoteCommand);

        // Admin commands
        foreach (var cmd in _config.Commands.AddAdmin)
            RegisterCommand(cmd, _adminCommands.OnAddAdminCommand);
        foreach (var cmd in _config.Commands.EditAdmin)
            RegisterCommand(cmd, _adminCommands.OnEditAdminCommand);
        foreach (var cmd in _config.Commands.RemoveAdmin)
            RegisterCommand(cmd, _adminCommands.OnRemoveAdminCommand);
        foreach (var cmd in _config.Commands.ListAdmins)
            RegisterCommand(cmd, _adminCommands.OnListAdminsCommand);
        foreach (var cmd in _config.Commands.AddGroup)
            RegisterCommand(cmd, _adminCommands.OnAddGroupCommand);
        foreach (var cmd in _config.Commands.EditGroup)
            RegisterCommand(cmd, _adminCommands.OnEditGroupCommand);
        foreach (var cmd in _config.Commands.RemoveGroup)
            RegisterCommand(cmd, _adminCommands.OnRemoveGroupCommand);
        foreach (var cmd in _config.Commands.ListGroups)
            RegisterCommand(cmd, _adminCommands.OnListGroupsCommand);
        foreach (var cmd in _config.Commands.AdminReload)
            RegisterCommand(cmd, _adminCommands.OnAdminReloadCommand);
    }

    private void RegisterCommand(string name, ICommandService.CommandListener handler)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        TryRegisterCommand(name, context =>
        {
            TryApplyConfiguredLocalizer(_config.Language);

            var commandName = context.CommandName ?? string.Empty;
            if (!context.IsSentByPlayer && !commandName.StartsWith("sw_", StringComparison.OrdinalIgnoreCase))
            {
                // Keep unprefixed commands usable in chat/player context,
                // but block direct server-console execution unless `sw_` is used.
                return;
            }

            handler(context);
        });

        var swAlias = CommandAliasUtils.ToSwAlias(name);
        if (!string.Equals(swAlias, name, StringComparison.OrdinalIgnoreCase))
        {
            TryRegisterCommand(swAlias, handler);
        }
    }

    private void TryRegisterCommand(string name, ICommandService.CommandListener handler)
    {
        if (Core.Command.IsCommandRegistered(name))
        {
            return;
        }

        Core.Command.RegisterCommand(name, handler, registerRaw: true);
    }

    private void SanitizeCommandAliases()
    {
        var commands = _config.Commands;
        foreach (var prop in typeof(CommandsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(List<string>))
            {
                continue;
            }

            var aliases = prop.GetValue(commands) as List<string>;
            if (aliases == null || aliases.Count == 0)
            {
                continue;
            }

            var blockedRemoved = aliases.Count(x => !string.IsNullOrWhiteSpace(x) && BlockedCommandAliases.Contains(x.Trim()));
            var cleaned = aliases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !BlockedCommandAliases.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (blockedRemoved > 0)
            {
                Core.Logger.LogWarningIfEnabled(
                    "[CS2Admin] Removed blocked command alias(es) from {Property}. Final: {Aliases}",
                    prop.Name,
                    string.Join(", ", cleaned));
            }

            prop.SetValue(commands, cleaned);
        }
    }

    private void CleanupLegacyCommandsFromConfig()
    {
        try
        {
            var configPath = Core.Configuration.GetConfigPath("config.json");
            if (!File.Exists(configPath))
            {
                return;
            }

            var json = File.ReadAllText(configPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root?["CS2Admin"] is not JsonObject pluginSection)
            {
                return;
            }

            if (!pluginSection.Remove("Commands"))
            {
                return;
            }

            var rewritten = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, rewritten);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Removed legacy Commands block from config.json; commands are now managed by commands.json.");
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to cleanup legacy Commands block from config.json: {Message}", ex.Message);
        }
    }

    private void RegisterEvents()
    {
        Core.Event.OnClientSteamAuthorize += _eventHandlers.OnClientSteamAuthorize;
        Core.Event.OnClientDisconnected += _eventHandlers.OnClientDisconnected;

        Core.GameEvent.HookPost<EventRoundStart>(_eventHandlers.OnRoundStart);
        Core.GameEvent.HookPost<EventRoundStart>(OnRoundStartEnsureCommands);
    }

    private HookResult OnRoundStartEnsureCommands(EventRoundStart @event)
    {
        TryApplyConfiguredLocalizer(_config.Language);
        EnsureCommandsRegistered();
        return HookResult.Continue;
    }

    private void EnsureCommandsRegistered()
    {
        try
        {
            var probe = _config?.Commands?.AdminMenu?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(probe) && Core.Command.IsCommandRegistered(probe))
                return;

            RegisterCommands();
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled("[CS2Admin] Failed while ensuring commands are registered: {Message}", ex.Message);
        }
    }

    private void StartAdminPlaytimeTracking()
    {
        var intervalMinutes = Math.Max(1, _config.AdminPlaytime.TrackIntervalMinutes);
        _adminPlaytimeTimer?.Dispose();

        _adminPlaytimeTimer = new Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref _isTrackingAdminPlaytime, 1) == 1)
                {
                    return;
                }

                Core.Scheduler.NextTick(() =>
                {
                    var snapshots = Core.PlayerManager.GetAllPlayers()
                        .Where(p => p.IsValid && !p.IsFakeClient)
                        .Select(p => new AdminPlaytimeSnapshot(
                            p.SteamID,
                            p.Controller.PlayerName ?? PluginLocalizer.Get(Core)["player_fallback_name", p.PlayerID]))
                        .ToList();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _adminPlaytimeDbManager.TrackOnlineAdminsAsync(snapshots, intervalMinutes);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isTrackingAdminPlaytime, 0);
                        }
                    });
                });
            },
            null,
            TimeSpan.FromMinutes(intervalMinutes),
            TimeSpan.FromMinutes(intervalMinutes));
    }
}


