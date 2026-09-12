using System.Collections.Concurrent;
using System.Text.Json;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Utils;

public class DiscordBotService
{
    private readonly ISwiftlyCore _core;
    private readonly string _serverName;
    private readonly string _botToken;
    private readonly string _defaultChannelId;
    private readonly string _chatChannelId;
    private readonly string _connectionChannelId;
    private readonly string _callAdminChannelId;
    private readonly string _reportChannelId;
    private readonly string _adminTimeChannelId;
    private readonly string _serverStatusChannelId;
    private readonly string _customConnectUrl;

    private readonly int _serverStatusUpdateSeconds;

    private readonly string _bannerUrl;
    private readonly ConcurrentDictionary<string, DateTime> _configurationWarningTimestamps = new(StringComparer.Ordinal);

    private CancellationTokenSource? _serverStatusPublishCts;
    private CancellationTokenSource? _serverStatusUpdateCts;
    private CancellationTokenSource? _verifyPanelCts;
    private PlayerSessionManager? _playerSessionManager;
    private DiscordServerStatusDbManager? _discordServerStatusDbManager;
    private DiscordMessageStateDbManager? _discordMessageStateDbManager;
    private WarnManager? _warnManager;
    private AdminLogManager? _adminLogManager;
    private DiscordLinkDbManager? _discordLinkDbManager;

    private readonly DiscordRestClient _restClient;
    private readonly DiscordNotificationService _notificationService;
    private readonly DiscordServerStatusService _serverStatusService;
    private readonly DiscordInteractionHandler _interactionHandler;
    private readonly DiscordGatewayClient? _gatewayClient;
    private readonly DiscordVerifyService _verifyService;

    public DiscordBotService(ISwiftlyCore core, DiscordFileConfig config, CommandsConfig? commandsConfig = null)
    {
        _core = core;
        _serverName = config.ServerName ?? string.Empty;
        _botToken = config.BotToken ?? string.Empty;
        _defaultChannelId = config.AdminLogChannelId ?? string.Empty;
        _chatChannelId = config.ChatLogChannelId ?? string.Empty;
        _connectionChannelId = config.ConnectionLogChannelId ?? string.Empty;
        _callAdminChannelId = config.CallAdminChannelId ?? string.Empty;
        _reportChannelId = config.ReportChannelId ?? string.Empty;
        _adminTimeChannelId = config.AdminTimeChannelId ?? string.Empty;
        _serverStatusChannelId = config.ServerStatusChannelId ?? string.Empty;
        _customConnectUrl = config.CustomConnectUrl ?? string.Empty;

        _serverStatusUpdateSeconds = Math.Max(10, config.ServerStatusUpdateSeconds);
        _bannerUrl = config.BannerUrl ?? string.Empty;

        _restClient = new DiscordRestClient(_core, _botToken);
        _interactionHandler = new DiscordInteractionHandler(_core, _restClient, _botToken);
        _gatewayClient = HasBotConfiguration()
            ? new DiscordGatewayClient(_core, _botToken, _interactionHandler.HandleInteractionAsync)
            : null;
        _notificationService = new DiscordNotificationService(_core, _restClient, _serverName,
            _defaultChannelId, _connectionChannelId, _chatChannelId,
            _callAdminChannelId, _reportChannelId, _adminTimeChannelId,
            CommandAliasResolver.BuildSet(commandsConfig));
        _serverStatusService = new DiscordServerStatusService(_core, _restClient,
            _serverStatusChannelId, _bannerUrl, _customConnectUrl, _serverName);
        _verifyService = new DiscordVerifyService(_core, _restClient, config);
        _interactionHandler.SetVerifyService(_verifyService);

        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][Debug][Discord] config botConfigured={BotConfigured} adminLogChannel={AdminLogChannel} chatChannel={ChatChannel} connectionChannel={ConnectionChannel} reportChannel={ReportChannel}",
            HasBotConfiguration(),
            DiscordHelpers.MaskChannelId(_defaultChannelId),
            DiscordHelpers.MaskChannelId(_chatChannelId),
            DiscordHelpers.MaskChannelId(_connectionChannelId),
            DiscordHelpers.MaskChannelId(_reportChannelId));
    }

    public DiscordVerifyService Verify => _verifyService;

    public void SetDatabaseManagers(WarnManager warnManager, AdminLogManager adminLogManager, DiscordLinkDbManager discordLinkDbManager)
    {
        _warnManager = warnManager;
        _adminLogManager = adminLogManager;
        _discordLinkDbManager = discordLinkDbManager;
        _interactionHandler.SetDatabaseManagers(warnManager, adminLogManager);
        if (_discordMessageStateDbManager != null)
        {
            _verifyService.SetDatabaseManagers(discordLinkDbManager, _discordMessageStateDbManager);
        }
    }

    public void StartBackgroundUpdates(
        PlayerSessionManager playerSessionManager,
        DiscordServerStatusDbManager discordServerStatusDbManager,
        DiscordMessageStateDbManager discordMessageStateDbManager)
    {
        _playerSessionManager = playerSessionManager;
        _discordServerStatusDbManager = discordServerStatusDbManager;
        _discordMessageStateDbManager = discordMessageStateDbManager;

        StopBackgroundUpdates();

        _gatewayClient?.Start();

        _serverStatusPublishCts = _core.Scheduler.RepeatBySeconds(_serverStatusUpdateSeconds, () => _ = _serverStatusService.PublishServerStatusAsync());
        _ = _serverStatusService.PublishServerStatusAsync();

        _serverStatusService.SetDatabaseManagers(discordServerStatusDbManager, discordMessageStateDbManager);

        if (_discordLinkDbManager != null)
        {
            _verifyService.SetDatabaseManagers(_discordLinkDbManager, discordMessageStateDbManager);
        }

        if (HasBotConfiguration() && !string.IsNullOrWhiteSpace(_serverStatusChannelId))
        {
            _serverStatusUpdateCts = _core.Scheduler.RepeatBySeconds(_serverStatusUpdateSeconds, () => _ = _serverStatusService.UpsertServerStatusMessageAsync());
            _ = _serverStatusService.UpsertServerStatusMessageAsync();
        }

        _ = _verifyService.PublishVerifyPanelAsync();
        if (_verifyService.IsEnabled)
        {
            _verifyPanelCts = _core.Scheduler.RepeatBySeconds(300, () => _ = _verifyService.PublishVerifyPanelAsync());
        }
    }

    public void StopBackgroundUpdates()
    {
        _gatewayClient?.Stop();

        _serverStatusPublishCts?.Cancel();
        _serverStatusPublishCts = null;

        _serverStatusUpdateCts?.Cancel();
        _serverStatusUpdateCts = null;

        _verifyPanelCts?.Cancel();
        _verifyPanelCts = null;
    }

    public void EnsureGatewayConnection()
    {
        if (!HasBotConfiguration())
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][Discord] gateway skipped reason=no_bot_token");
            return;
        }

        if (_gatewayClient == null)
            return;

        _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][Discord] gateway starting");
        _gatewayClient.Start();
    }

    public async Task SendConnectNotificationAsync(IPlayer player)
    {
        await _notificationService.SendConnectNotificationAsync(player);
    }

    public async Task SendConnectNotificationAsync(string? playerName, ulong steamId, string? ipAddress, int activePlayers)
    {
        await _notificationService.SendConnectNotificationAsync(playerName, steamId, ipAddress, activePlayers);
    }

    public async Task SendDisconnectNotificationAsync(string? playerName, ulong steamId, string? ipAddress)
    {
        await _notificationService.SendDisconnectNotificationAsync(playerName, steamId, ipAddress);
    }

    public async Task SendChatNotificationAsync(IPlayer player, string message, bool teamOnly)
    {
        await _notificationService.SendChatNotificationAsync(player, message, teamOnly);
    }

    public async Task SendBanNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        await _notificationService.SendBanNotificationAsync(adminName, targetName, duration, reason);
    }

    public async Task SendUnbanNotificationAsync(string adminName, string targetSteamId, string reason)
    {
        await _notificationService.SendUnbanNotificationAsync(adminName, targetSteamId, reason);
    }

    public async Task SendMuteNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        await _notificationService.SendMuteNotificationAsync(adminName, targetName, duration, reason);
    }

    public async Task SendGagNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        await _notificationService.SendGagNotificationAsync(adminName, targetName, duration, reason);
    }

    public async Task SendKickNotificationAsync(string adminName, string targetName, string reason)
    {
        await _notificationService.SendKickNotificationAsync(adminName, targetName, reason);
    }

    public async Task SendSilenceNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        await _notificationService.SendSilenceNotificationAsync(adminName, targetName, duration, reason);
    }

    public async Task SendWarnNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        await _notificationService.SendWarnNotificationAsync(adminName, targetName, duration, reason);
    }

    public async Task SendCallAdminNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        await _notificationService.SendCallAdminNotificationAsync(playerName, playerSteamId, message, serverId);
    }

    public async Task SendReportNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        await _notificationService.SendReportNotificationAsync(playerName, playerSteamId, message, serverId);
    }

    public async Task SendAdminTimeNotificationAsync(IReadOnlyList<AdminPlaytime> entries, List<string>? zeroPlaytimeAdmins = null)
    {
        await _notificationService.SendAdminTimeNotificationAsync(entries, zeroPlaytimeAdmins);
    }

    public async Task SendAdminActionNotificationAsync(string action, string adminName, ulong adminSteamId, ulong? targetSteamId, string? details, string serverId, string? targetName = null)
    {
        await _notificationService.SendAdminActionNotificationAsync(action, adminName, adminSteamId, targetSteamId, details, serverId, targetName);
    }

    private bool HasBotConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_botToken);
    }

    private bool IsDiscordChannelReady(string action, string channelId)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            LogDiscordConfigurationWarning(action, "BotToken is empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            LogDiscordConfigurationWarning(action, "channel id is empty");
            return false;
        }

        return true;
    }

    private void LogDiscordConfigurationWarning(string action, string reason)
    {
        var key = $"{action}:{reason}";
        var now = DateTime.UtcNow;
        if (_configurationWarningTimestamps.TryGetValue(key, out var lastLogged) && (now - lastLogged).TotalSeconds < 60)
        {
            return;
        }

        _configurationWarningTimestamps[key] = now;
        _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord bot cannot {Action}: {Reason}.", action, reason);
    }
}
