using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordRestClient
{
    private const string DiscordApiBaseUrl = "https://discord.com/api/v10";

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly ISwiftlyCore _core;
    private readonly string _botToken;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, DateTime> _configurationWarningTimestamps = new(StringComparer.Ordinal);

    public DiscordRestClient(ISwiftlyCore core, string botToken)
    {
        _core = core;
        _botToken = botToken;
        _httpClient = SharedHttpClient;
    }

    public async Task<string?> SendEmbedAsync(string channelId, object embed, string? messageContent = null, object[]? components = null, bool allowEveryoneMention = false)
    {
        if (!IsDiscordChannelReady("send embed", channelId))
        {
            return null;
        }

        return await CreateMessageAsync(channelId, BuildMessagePayload(messageContent, embed, components, allowEveryoneMention));
    }

    public async Task<string?> SendMessageAsync(string channelId, string messageContent)
    {
        if (!IsDiscordChannelReady("send message", channelId) || string.IsNullOrWhiteSpace(messageContent))
        {
            return null;
        }

        return await CreateMessageAsync(channelId, BuildMessagePayload(messageContent, null, null));
    }

    public async Task<bool?> UpdateEmbedAsync(string channelId, string messageId, object embed, string? messageContent = null, object[]? components = null, bool allowEveryoneMention = false)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        return await UpdateMessageAsync(channelId, messageId, BuildMessagePayload(messageContent, embed, components, allowEveryoneMention));
    }

    public async Task<bool?> UpdateMessageRawAsync(string channelId, string messageId, object payload)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        return await UpdateMessageAsync(channelId, messageId, payload);
    }

    private async Task<string?> CreateMessageAsync(string channelId, object payload)
    {
        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages";
        // _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordREST] create message request channel={ChannelId}", DiscordHelpers.MaskChannelId(channelId));
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await LogDiscordFailureAsync("create message", response);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<DiscordMessageResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // _core.Logger.LogInformationIfEnabled(
        //     "[CS2_Admin][Debug][DiscordREST] create message success channel={ChannelId} messageId={MessageId}",
        //     DiscordHelpers.MaskChannelId(channelId),
        //     string.IsNullOrWhiteSpace(data?.Id) ? "-" : data.Id);

        return data?.Id;
    }

    public async Task<bool> RespondToInteractionAsync(string interactionId, string interactionToken, int type, object? data = null)
    {
        if (!HasBotConfiguration())
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/interactions/{interactionId}/{interactionToken}/callback";
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, new { type, data });
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("respond to interaction", response);
        return false;
    }

    private async Task<bool?> UpdateMessageAsync(string channelId, string messageId, object payload)
    {
        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages/{messageId}";
        using var request = BuildDiscordRequest(HttpMethod.Patch, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await LogDiscordFailureAsync("update message", response);
        return false;
    }

    public async Task<bool> AddGuildMemberRoleAsync(string guildId, string userId, string roleId)
    {
        return await SendGuildMemberRoleRequestAsync(HttpMethod.Put, guildId, userId, roleId);
    }

    public async Task<bool> RemoveGuildMemberRoleAsync(string guildId, string userId, string roleId)
    {
        return await SendGuildMemberRoleRequestAsync(HttpMethod.Delete, guildId, userId, roleId);
    }

    private async Task<bool> SendGuildMemberRoleRequestAsync(HttpMethod method, string guildId, string userId, string roleId)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(roleId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/guilds/{guildId}/members/{userId}/roles/{roleId}";
        using var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        if (method == HttpMethod.Put)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("update member role", response);
        return false;
    }

    public async Task<string?> CreateGuildChannelAsync(string guildId, object payload)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(guildId))
        {
            return null;
        }

        var endpoint = $"{DiscordApiBaseUrl}/guilds/{guildId}/channels";
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await LogDiscordFailureAsync("create channel", response);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
    }

    public async Task<List<DiscordGuildChannel>?> GetGuildChannelsAsync(string guildId)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(guildId))
        {
            return null;
        }

        var endpoint = $"{DiscordApiBaseUrl}/guilds/{guildId}/channels";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await LogDiscordFailureAsync("fetch guild channels", response);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var channels = new List<DiscordGuildChannel>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = element.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            channels.Add(new DiscordGuildChannel
            {
                Id = id,
                Name = element.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null,
                Type = element.TryGetProperty("type", out var typeElement) && typeElement.TryGetInt32(out var type) ? type : 0,
                ParentId = element.TryGetProperty("parent_id", out var parentElement) ? parentElement.GetString() : null
            });
        }

        return channels;
    }

    public async Task<DiscordChannelUpdateResult> UpdateChannelNameAsync(string channelId, string name)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(name))
        {
            return new DiscordChannelUpdateResult(DiscordChannelUpdateStatus.Error, 0);
        }

        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}";
        using var request = BuildDiscordRequest(HttpMethod.Patch, endpoint, new { name });
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return new DiscordChannelUpdateResult(DiscordChannelUpdateStatus.Success, 0);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new DiscordChannelUpdateResult(DiscordChannelUpdateStatus.NotFound, 0);
        }

        if ((int)response.StatusCode == 429)
        {
            return new DiscordChannelUpdateResult(DiscordChannelUpdateStatus.RateLimited,
                await ReadRetryAfterSecondsAsync(response));
        }

        await LogDiscordFailureAsync("update channel", response);
        return new DiscordChannelUpdateResult(DiscordChannelUpdateStatus.Error, 0);
    }

    private static async Task<double> ReadRetryAfterSecondsAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("retry_after", out var retryAfterElement)
                && retryAfterElement.TryGetDouble(out var retryAfterSeconds)
                && retryAfterSeconds > 0)
            {
                return retryAfterSeconds;
            }
        }
        catch
        {
        }

        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta.TotalSeconds;
        }

        return 60;
    }

    public async Task<bool> DeleteMessageAsync(string channelId, string messageId)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages/{messageId}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        await LogDiscordFailureAsync("delete message", response);
        return false;
    }

    public async Task CleanupDuplicateEmbedsAsync(string channelId, string? title, string keepMessageId)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(keepMessageId))
        {
            return;
        }

        var duplicateIds = await GetDuplicateMessageIdsByTitleAsync(channelId, title, keepMessageId);
        foreach (var duplicateId in duplicateIds)
        {
            await DeleteMessageAsync(channelId, duplicateId);
        }
    }

    private async Task<List<string>> GetDuplicateMessageIdsByTitleAsync(string channelId, string title, string keepMessageId)
    {
        if (!HasBotConfiguration() || string.IsNullOrWhiteSpace(channelId))
        {
            return [];
        }

        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages?limit=50";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await LogDiscordFailureAsync("fetch messages", response);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var duplicateIds = new List<string>();

        foreach (var messageElement in document.RootElement.EnumerateArray())
        {
            var messageId = messageElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(messageId) || string.Equals(messageId, keepMessageId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!messageElement.TryGetProperty("embeds", out var embedsElement) || embedsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var embedElement in embedsElement.EnumerateArray())
            {
                var embedTitle = embedElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                if (string.Equals(embedTitle, title, StringComparison.Ordinal))
                {
                    duplicateIds.Add(messageId);
                    break;
                }
            }
        }

        return duplicateIds;
    }

    private static JsonObject BuildMessagePayload(string? messageContent, object? embed, object[]? components, bool allowEveryoneMention = false)
    {
        var payload = new JsonObject
        {
            ["content"] = string.IsNullOrWhiteSpace(messageContent) ? null : messageContent,
            ["allowed_mentions"] = JsonSerializer.SerializeToNode(
                allowEveryoneMention
                    ? new { parse = new[] { "everyone" } }
                    : new { parse = Array.Empty<string>() },
                JsonOptions)
        };

        if (embed != null)
        {
            payload["embeds"] = JsonSerializer.SerializeToNode(new[] { embed }, JsonOptions);
        }

        if (components is { Length: > 0 })
        {
            payload["components"] = JsonSerializer.SerializeToNode(components, JsonOptions);
        }

        return payload;
    }

    private HttpRequestMessage BuildDiscordRequest(HttpMethod method, string endpoint, object payload)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task LogDiscordFailureAsync(string action, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body) && (body.Contains("\"code\": 10062") || body.Contains("\"code\": 40060")))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(body) && body.Contains("\"code\": 10004"))
        {
            _core.Logger.LogWarningIfEnabled(
                "[CS2_Admin] Discord bot {Action} failed: Unknown Guild. GuildId must be the server ID (right-click server name > Copy Server ID), not a channel ID.",
                action);
            return;
        }

        _core.Logger.LogWarningIfEnabled(
            "[CS2_Admin] Discord bot {Action} failed with status {StatusCode}. Response: {Body}",
            action,
            response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? "-" : body);
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

    private sealed class DiscordMessageResponse
    {
        public string? Id { get; set; }
    }
}

public enum DiscordChannelUpdateStatus
{
    Success,
    NotFound,
    RateLimited,
    Error
}

public readonly record struct DiscordChannelUpdateResult(DiscordChannelUpdateStatus Status, double RetryAfterSeconds);

public sealed class DiscordGuildChannel
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int Type { get; set; }
    public string? ParentId { get; set; }
}
