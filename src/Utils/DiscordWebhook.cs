using System.Text.Json;
using System.Text.RegularExpressions;
using CS2_Admin.Config;
using CS2_Admin.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordWebhook
{
    private static readonly Regex ReportMenuMessageRegex = new(
        @"^Target:\s*(?<target>.+?)\s*\|\s*Reason:\s*(?<reason>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly ISwiftlyCore _core;
    private readonly string _defaultWebhookUrl;
    private readonly string _callAdminWebhookUrl;
    private readonly string _reportWebhookUrl;
    private readonly string _adminTimeWebhookUrl;
    private readonly HttpClient _httpClient;

    public DiscordWebhook(ISwiftlyCore core, DiscordConfig config)
    {
        _core = core;
        _defaultWebhookUrl = config.Webhook ?? string.Empty;
        _callAdminWebhookUrl = config.CallAdminWebhook ?? string.Empty;
        _reportWebhookUrl = config.ReportWebhook ?? string.Empty;
        _adminTimeWebhookUrl = config.AdminTimeWebhook ?? string.Empty;
        _httpClient = new HttpClient();
    }

    public async Task SendBanNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_ban_title"],
                15158332,
                "ban",
                adminName,
                "0",
                targetName,
                "-",
                $"duration={durationText} | reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending ban notification: {Message}", ex.Message);
        }
    }

    public async Task SendUnbanNotificationAsync(string adminName, string targetSteamId, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_unban_title"],
                3066993,
                "unban",
                adminName,
                "0",
                "-",
                targetSteamId,
                $"reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending unban notification: {Message}", ex.Message);
        }
    }

    public async Task SendMuteNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_mute_title"],
                15105570,
                "mute",
                adminName,
                "0",
                targetName,
                "-",
                $"duration={durationText} | reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending mute notification: {Message}", ex.Message);
        }
    }

    public async Task SendGagNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_gag_title"],
                15105570,
                "gag",
                adminName,
                "0",
                targetName,
                "-",
                $"duration={durationText} | reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending gag notification: {Message}", ex.Message);
        }
    }

    public async Task SendKickNotificationAsync(string adminName, string targetName, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_kick_title"],
                15844367,
                "kick",
                adminName,
                "0",
                targetName,
                "-",
                $"reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending kick notification: {Message}", ex.Message);
        }
    }

    public async Task SendSilenceNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_silence_title"],
                10181046,
                "silence",
                adminName,
                "0",
                targetName,
                "-",
                $"duration={durationText} | reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending silence notification: {Message}", ex.Message);
        }
    }

    public async Task SendWarnNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["discord_permanent"] : PluginLocalizer.Get(_core)["discord_minutes", duration];
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_warn_title"],
                16098851,
                "warn",
                adminName,
                "0",
                targetName,
                "-",
                $"duration={durationText} | reason={reason}");

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending warn notification: {Message}", ex.Message);
        }
    }

    public async Task SendCallAdminNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        var webhookUrl = ResolveWebhookUrl(_callAdminWebhookUrl);
        if (string.IsNullOrEmpty(webhookUrl))
            return;

        try
        {
            var embed = new
            {
                title = PluginLocalizer.Get(_core)["discord_calladmin_title"],
                color = 10181046,
                fields = new[]
                {
                    new { name = PluginLocalizer.Get(_core)["discord_player"], value = playerName, inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_steamid"], value = playerSteamId.ToString(), inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_server"], value = string.IsNullOrWhiteSpace(serverId) ? "-" : serverId, inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_calladmin_message"], value = string.IsNullOrWhiteSpace(message) ? "-" : message, inline = false }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await SendEmbedAsync(embed, webhookUrl, "@everyone");
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending calladmin notification: {Message}", ex.Message);
        }
    }

    public async Task SendReportNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        var webhookUrl = ResolveWebhookUrl(_reportWebhookUrl);
        if (string.IsNullOrEmpty(webhookUrl))
            return;

        try
        {
            var rawMessage = string.IsNullOrWhiteSpace(message) ? "-" : message.Trim();
            var targetValue = "-";
            var displayMessage = rawMessage;

            var parsed = ReportMenuMessageRegex.Match(rawMessage);
            if (parsed.Success)
            {
                targetValue = parsed.Groups["target"].Value.Trim();
                displayMessage = parsed.Groups["reason"].Value.Trim();
            }

            var embed = new
            {
                title = "Player Report",
                color = 16763904,
                fields = new[]
                {
                    new { name = PluginLocalizer.Get(_core)["discord_player"], value = playerName, inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_steamid"], value = playerSteamId.ToString(), inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_server"], value = string.IsNullOrWhiteSpace(serverId) ? "-" : serverId, inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_target"], value = targetValue, inline = true },
                    new { name = PluginLocalizer.Get(_core)["discord_calladmin_message"], value = displayMessage, inline = false }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await SendEmbedAsync(embed, webhookUrl, "@everyone");
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending report notification: {Message}", ex.Message);
        }
    }

    public async Task SendAdminTimeNotificationAsync(IReadOnlyList<AdminPlaytime> entries)
    {
        var webhookUrl = ResolveWebhookUrl(_adminTimeWebhookUrl);
        if (string.IsNullOrEmpty(webhookUrl))
            return;

        try
        {
            var maxRows = Math.Min(entries.Count, 23); // embed max field count is 25
            var fields = new List<object>
            {
                new { name = PluginLocalizer.Get(_core)["discord_server_name"], value = ServerIdentity.GetName(_core), inline = true },
                new { name = PluginLocalizer.Get(_core)["discord_server"], value = ServerIdentity.GetServerId(_core), inline = true }
            };

            for (var i = 0; i < maxRows; i++)
            {
                var entry = entries[i];
                fields.Add(new
                {
                    name = $"#{i + 1} {entry.PlayerName}",
                    value = $"{PluginLocalizer.Get(_core)["discord_steamid"]}: `{entry.SteamId}`\n{PluginLocalizer.Get(_core)["discord_duration"]}: {FormatPlaytime(entry.PlaytimeMinutes)} ({entry.PlaytimeMinutes}m)",
                    inline = false
                });
            }

            if (maxRows == 0)
            {
                fields.Add(new { name = "-", value = "-", inline = false });
            }

            var embed = new
            {
                title = PluginLocalizer.Get(_core)["discord_admin_playtime_title"],
                color = 3447003,
                fields = fields.ToArray(),
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await SendEmbedAsync(embed, webhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending admin playtime notification: {Message}", ex.Message);
        }
    }

    public async Task SendAdminActionNotificationAsync(string action, string adminName, ulong adminSteamId, ulong? targetSteamId, string? details, string serverId, string? targetName = null)
    {
        if (string.IsNullOrEmpty(_defaultWebhookUrl))
            return;

        try
        {
            var embed = BuildStandardAdminActionEmbed(
                PluginLocalizer.Get(_core)["discord_admin_action_title"],
                3447003,
                string.IsNullOrWhiteSpace(action) ? "-" : action,
                string.IsNullOrWhiteSpace(adminName) ? "-" : adminName,
                adminSteamId.ToString(),
                string.IsNullOrWhiteSpace(targetName) ? "-" : targetName,
                targetSteamId?.ToString() ?? "-",
                string.IsNullOrWhiteSpace(details) ? "-" : details,
                serverId);

            await SendEmbedAsync(embed, _defaultWebhookUrl);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending admin action notification: {Message}", ex.Message);
        }
    }

    private static string FormatPlaytime(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return "0m";
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0)
        {
            return $"{minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    private string ResolveWebhookUrl(string preferredWebhookUrl)
    {
        if (!string.IsNullOrWhiteSpace(preferredWebhookUrl))
        {
            return preferredWebhookUrl;
        }

        return _defaultWebhookUrl;
    }

    private async Task SendEmbedAsync(object embed, string webhookUrl, string? messageContent = null)
    {
        var payload = new
        {
            content = string.IsNullOrWhiteSpace(messageContent) ? null : messageContent,
            embeds = new[] { embed }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        await _httpClient.PostAsync(webhookUrl, content);
    }

    private object BuildStandardAdminActionEmbed(
        string title,
        int color,
        string command,
        string adminName,
        string adminSteamId,
        string playerName,
        string playerId,
        string message,
        string? serverId = null)
    {
        var resolvedServer = string.IsNullOrWhiteSpace(serverId) ? ServerIdentity.GetServerId(_core) : serverId;
        return new
        {
            title,
            color,
            fields = new object[]
            {
                new { name = "Command", value = string.IsNullOrWhiteSpace(command) ? "-" : command, inline = true },
                new { name = "Server", value = string.IsNullOrWhiteSpace(resolvedServer) ? "-" : resolvedServer, inline = true },
                new { name = "Admin", value = string.IsNullOrWhiteSpace(adminName) ? "-" : adminName, inline = true },
                new { name = "SteamID", value = string.IsNullOrWhiteSpace(adminSteamId) ? "-" : adminSteamId, inline = true },
                new { name = "Player", value = string.IsNullOrWhiteSpace(playerName) ? "-" : playerName, inline = true },
                new { name = "PlayerID", value = string.IsNullOrWhiteSpace(playerId) ? "-" : playerId, inline = true },
                new { name = "Message", value = string.IsNullOrWhiteSpace(message) ? "-" : message, inline = false }
            },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }
}


