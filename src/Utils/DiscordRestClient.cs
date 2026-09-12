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
