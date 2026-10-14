using System.Text.Json;
using CS2_Admin.Database;
using CS2_Admin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordServerStatusService
{
    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly string _serverStatusChannelId;
    private readonly string _bannerUrl;
    private readonly string _customConnectUrl;
    private readonly string _serverName;
    private DiscordServerStatusDbManager? _discordServerStatusDbManager;
    private DiscordMessageStateDbManager? _discordMessageStateDbManager;
    private string? _serverStatusMessageId;

    public DiscordServerStatusService(ISwiftlyCore core, DiscordRestClient restClient,
        string serverStatusChannelId, string bannerUrl, string customConnectUrl, string serverName)
    {
        _core = core;
        _restClient = restClient;
        _serverStatusChannelId = serverStatusChannelId;
        _bannerUrl = bannerUrl;
        _customConnectUrl = customConnectUrl;
        _serverName = serverName;
    }

    public void SetDatabaseManagers(DiscordServerStatusDbManager? dssdm,
        DiscordMessageStateDbManager? dmsdm)
    {
        _discordServerStatusDbManager = dssdm;
        _discordMessageStateDbManager = dmsdm;
    }

    public async Task PublishServerStatusAsync()
    {
        if (_discordServerStatusDbManager == null)
        {
            return;
        }

        try
        {
            var mapName = ServerIdentity.GetCurrentMap(_core);
            if (IsUnknownMapName(mapName))
            {
                _core.Logger.LogInformationIfEnabled("Skipping Discord server status publish because current map is unknown.");
                return;
            }

            await _discordServerStatusDbManager.UpsertStatusAsync(new DiscordServerStatusSnapshot(
                ServerIdentity.GetServerId(_core),
                "default",
                GetServerLabel(),
                string.Empty,
                ServerIdentity.GetIp(_core),
                ServerIdentity.GetPort(_core),
                mapName,
                GetActiveHumanPlayerCount(),
                ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
                BuildJoinUrl(ServerIdentity.GetIp(_core), ServerIdentity.GetPort(_core))));
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Error publishing shared server status: {Message}", ex.Message);
        }
    }

    public async Task UpsertServerStatusMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(_serverStatusChannelId))
        {
            return;
        }

        try
        {
            var status = BuildCurrentServerStatus();
            if (IsUnknownMapName(status.MapName))
            {
                await RemoveServerStatusMessageAsync();
                _core.Logger.LogInformationIfEnabled("Skipping Discord server status message because current map is unknown.");
                return;
            }

            var embed = BuildIndividualServerStatusEmbed(status, true);

            var messageKey = $"serverstatus:default:{status.ServerId}:{_serverStatusChannelId}";
            var dbMessageId = _discordMessageStateDbManager == null
                ? null
                : await _discordMessageStateDbManager.GetMessageIdAsync(messageKey);

            var previousMessageId = !string.IsNullOrWhiteSpace(dbMessageId)
                ? dbMessageId
                : _serverStatusMessageId;

            if (!string.IsNullOrWhiteSpace(previousMessageId))
            {
                var updateResult = await _restClient.UpdateEmbedAsync(_serverStatusChannelId, previousMessageId, embed);
                if (updateResult == true)
                {
                    if (_serverStatusMessageId != previousMessageId)
                    {
                        _serverStatusMessageId = previousMessageId;
                    }
                    return;
                }
                
                if (updateResult == false)
                {
                    // Rate limited or other error, do not create a new duplicate message
                    return;
                }
            }

            var newMessageId = await _restClient.SendEmbedAsync(_serverStatusChannelId, embed);
            if (!string.IsNullOrWhiteSpace(newMessageId))
            {
                _serverStatusMessageId = newMessageId;

                if (_discordMessageStateDbManager != null)
                {
                    await _discordMessageStateDbManager.UpsertMessageIdAsync(messageKey, _serverStatusChannelId, newMessageId);
                }

                if (!string.IsNullOrWhiteSpace(previousMessageId) && !string.Equals(previousMessageId, newMessageId, StringComparison.Ordinal))
                {
                    await _restClient.DeleteMessageAsync(_serverStatusChannelId, previousMessageId);
                }
                
                var displayName = string.IsNullOrWhiteSpace(status.ServerName) ? status.ButtonLabel : status.ServerName;
                var title = DiscordHelpers.EscapeMarkdown(DiscordHelpers.TrimLabel(displayName));
                _core.Scheduler.DelayBySeconds(5f, () => _ = _restClient.CleanupDuplicateEmbedsAsync(_serverStatusChannelId, title, newMessageId));
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Error updating server status message: {Message}", ex.Message);
        }
    }

    public async Task RemoveServerStatusMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(_serverStatusChannelId))
        {
            return;
        }

        var status = BuildCurrentServerStatus();
        var messageKey = $"serverstatus:default:{status.ServerId}:{_serverStatusChannelId}";
        var dbMessageId = _discordMessageStateDbManager == null
            ? null
            : await _discordMessageStateDbManager.GetMessageIdAsync(messageKey);

        var previousMessageId = !string.IsNullOrWhiteSpace(dbMessageId)
            ? dbMessageId
            : _serverStatusMessageId;

        if (!string.IsNullOrWhiteSpace(previousMessageId))
        {
            await _restClient.DeleteMessageAsync(_serverStatusChannelId, previousMessageId);
        }

        _serverStatusMessageId = null;
    }

    private DiscordServerStatus BuildCurrentServerStatus()
    {
        var ip = ServerIdentity.GetIp(_core);
        var port = ServerIdentity.GetPort(_core);
        return new DiscordServerStatus
        {
            ServerId = ServerIdentity.GetServerId(_core),
            HubKey = "default",
            ServerName = GetServerLabel(),
            ButtonLabel = string.Empty,
            ServerIp = ip,
            ServerPort = port,
            MapName = ServerIdentity.GetCurrentMap(_core),
            PlayerCount = GetActiveHumanPlayerCount(),
            MaxPlayers = ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
            JoinUrl = BuildJoinUrl(ip, port),
            LastHeartbeatAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private object BuildIndividualServerStatusEmbed(DiscordServerStatus status, bool isOnline)
    {
        var displayName = string.IsNullOrWhiteSpace(status.ServerName)
            ? status.ButtonLabel
            : status.ServerName;

        var title = DiscordHelpers.EscapeMarkdown(DiscordHelpers.TrimLabel(displayName));

        var bannerUrl = _bannerUrl;

        var statusText = isOnline
            ? $"{T("discord_server_status_online", "Online")} 🟢"
            : $"{T("discord_server_status_offline", "Offline")} 🔴";
        var statusColor = isOnline ? 0x2ECC71 : 0xE74C3C;
        var mapName = string.IsNullOrWhiteSpace(status.MapName) ? T("discord_server_status_unknown_map", "unknown") : status.MapName;

        return new
        {
            title = title,
            color = statusColor,
            fields = new[]
            {
                new { name = $"🗺️ {T("discord_server_status_map_field", "Map")}", value = DiscordHelpers.EscapeMarkdown(mapName), inline = true },
                new { name = $"👥 {T("discord_server_status_players_field", "Players")}", value = $"{status.PlayerCount}/{status.MaxPlayers}", inline = true },
                new { name = "\u200B", value = "\u200B", inline = true },
                new { name = $"🌐 {T("discord_server_status_ip_field", "IP Address")}", value = $"{status.ServerIp}:{status.ServerPort}", inline = true },
                new { name = $"📊 {T("discord_server_status_status_field", "Status")}", value = statusText, inline = true },
                new { name = "\u200B", value = "\u200B", inline = true },
                new { name = $"⌨️ {T("discord_server_status_connect_field", "Quick Connect")}", value = $"```\nconnect {status.ServerIp}:{status.ServerPort}\n```", inline = false }
            },
            image = string.IsNullOrWhiteSpace(bannerUrl) ? null : new { url = bannerUrl },
            footer = new { text = T("discord_last_update_footer", "Last update | {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private static bool IsUnknownMapName(string? mapName)
    {
        return string.IsNullOrWhiteSpace(mapName)
            || string.Equals(mapName.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapName.Trim(), "-", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildJoinUrl(string ip, int port)
    {
        if (string.IsNullOrWhiteSpace(ip) || port <= 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(_customConnectUrl))
        {
            return _customConnectUrl.Replace("{IP}", ip).Replace("{PORT}", port.ToString());
        }

        return $"https://cs2browser.net/?search={Uri.EscapeDataString($"{ip}:{port}")}";
    }

    private string BuildSteamServerUrl()
    {
        var ip = ServerIdentity.GetIp(_core);
        var port = ServerIdentity.GetPort(_core);
        if (string.IsNullOrWhiteSpace(ip) || port <= 0 || ip == "0.0.0.0")
        {
            return DiscordHelpers.BuildSteamProfileUrl(0);
        }

        return $"https://steamcommunity.com/linkfilter/?url=steam://connect/{ip}:{port}";
    }

    private string GetServerLabel()
    {
        if (!string.IsNullOrWhiteSpace(_serverName))
        {
            return _serverName.Trim();
        }

        var configuredName = ServerIdentity.GetName(_core);
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName;
        }

        var serverId = ServerIdentity.GetServerId(_core);
        return string.IsNullOrWhiteSpace(serverId) ? "Server" : serverId;
    }

    private int GetActiveHumanPlayerCount()
    {
        return _core.PlayerManager.GetAllPlayers().Count(p => p.IsValid && !p.IsFakeClient);
    }

    private string T(string key, string fallback, params object[] args)
    {
        // İsimli placeholder'lı ({player}, {reason}, {minutes}...) çeviri anahtarları chat ile AYNI
        // şekilde çalışsın diye LocalizerHelper üzerinden normalize edip biçimlendiriyoruz. Native
        // localizer[key, args] yalnızca pozisyonel {0} destekler ve isimli placeholder'ları bozardı.
        return args.Length == 0
            ? global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback)
            : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback, args);
    }
}
