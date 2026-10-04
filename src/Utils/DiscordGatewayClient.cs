using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordGatewayClient
{
    private readonly ISwiftlyCore _core;
    private readonly string _botToken;
    private CancellationTokenSource? _cts;
    private ClientWebSocket? _socket;
    private Task? _gatewayTask;
    private Task? _heartbeatTask;
    private int? _sequence;
    private string? _sessionId;

    public delegate Task InteractionCallback(JsonElement interactionData);
    public delegate Task GuildMemberRemovedCallback(ulong discordId, string guildId);
    private readonly InteractionCallback _onInteraction;
    private readonly GuildMemberRemovedCallback _onGuildMemberRemoved;

    public DiscordGatewayClient(ISwiftlyCore core, string botToken, InteractionCallback onInteraction, GuildMemberRemovedCallback onGuildMemberRemoved)
    {
        _core = core;
        _botToken = botToken;
        _onInteraction = onInteraction;
        _onGuildMemberRemoved = onGuildMemberRemoved;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _gatewayTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;

        try
        {
            _socket?.Abort();
            _socket?.Dispose();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Discord gateway socket dispose failed");
        }

        _socket = null;
        _gatewayTask = null;
        _heartbeatTask = null;
        _sequence = null;
        _sessionId = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                _socket = socket;

                await socket.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), cancellationToken);
                await ReceiveMessagesAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord gateway connection failed: {Message}", ex.Message);
            }
            finally
            {
                _socket = null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ReceiveMessagesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var payload = await ReceivePayloadAsync(socket, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            await HandlePayloadAsync(socket, payload, cancellationToken);
        }
    }

    private async Task<string?> ReceivePayloadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", cancellationToken);
                }
                catch (Exception ex)
                {
                    _core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Discord gateway close failed");
                }

                return null;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task HandlePayloadAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number)
        {
            _sequence = seqElement.GetInt32();
        }

        var op = root.GetProperty("op").GetInt32();
        switch (op)
        {
            case 0:
                {
                    if (root.TryGetProperty("t", out var tElement) && tElement.ValueKind == JsonValueKind.String)
                    {
                        var eventName = tElement.GetString();
                        if (eventName == "READY" && root.TryGetProperty("d", out var readyData))
                        {
                            if (readyData.TryGetProperty("session_id", out var sessionIdElement))
                            {
                                _sessionId = sessionIdElement.GetString();
                            }
                        }
                        else if (eventName == "INTERACTION_CREATE" && root.TryGetProperty("d", out var dElement))
                        {
                            var interactionData = dElement.Clone();
                            _ = Task.Run(() => _onInteraction(interactionData), cancellationToken);
                        }
                        else if (eventName == "GUILD_MEMBER_REMOVE" && root.TryGetProperty("d", out var memberData))
                        {
                            if (memberData.TryGetProperty("user", out var userElement)
                                && userElement.TryGetProperty("id", out var idElement)
                                && idElement.ValueKind == JsonValueKind.String
                                && ulong.TryParse(idElement.GetString(), out var discordId)
                                && memberData.TryGetProperty("guild_id", out var guildIdElement)
                                && guildIdElement.ValueKind == JsonValueKind.String)
                            {
                                _ = Task.Run(() => _onGuildMemberRemoved(discordId, guildIdElement.GetString() ?? string.Empty), cancellationToken);
                            }
                        }
                    }
                    break;
                }
            case 10:
                {
                    var heartbeatIntervalMs = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                    _heartbeatTask = Task.Run(() => RunHeartbeatLoopAsync(socket, heartbeatIntervalMs, cancellationToken), cancellationToken);
                    if (!string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
                    {
                        await SendResumeAsync(socket, cancellationToken);
                    }
                    else
                    {
                        await SendIdentifyAsync(socket, cancellationToken);
                    }
                    break;
                }
            case 1:
                await SendHeartbeatAsync(socket, cancellationToken);
                break;
            case 7:
            case 9:
                if (op == 9)
                {
                    _sessionId = null;
                    _sequence = null;
                }
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", cancellationToken);
                }
                catch (Exception ex)
                {
                    _core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Discord gateway reconnect close failed");
                }
                break;
            case 11:
                break;
        }
    }

    private async Task RunHeartbeatLoopAsync(ClientWebSocket socket, int heartbeatIntervalMs, CancellationToken cancellationToken)
    {
        var jitterMs = Random.Shared.Next(0, Math.Max(heartbeatIntervalMs, 1));
        await Task.Delay(jitterMs, cancellationToken);

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            await SendHeartbeatAsync(socket, cancellationToken);
            await Task.Delay(heartbeatIntervalMs, cancellationToken);
        }
    }

    private async Task SendHeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 1,
            d = _sequence
        });

        await SendPayloadAsync(socket, payload, cancellationToken);
    }

    private async Task SendIdentifyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 2,
            d = new
            {
                token = _botToken,
                intents = 1 | (1 << 1), // GUILDS | GUILD_MEMBERS
                properties = new
                {
                    os = Environment.OSVersion.Platform.ToString(),
                    browser = "CS2_Admin",
                    device = "CS2_Admin"
                },
                presence = new
                {
                    since = (long?)null,
                    activities = Array.Empty<object>(),
                    status = "online",
                    afk = false
                }
            }
        });

        await SendPayloadAsync(socket, payload, cancellationToken);
    }

    private async Task SendResumeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 6,
            d = new
            {
                token = _botToken,
                session_id = _sessionId,
                seq = _sequence
            }
        });

        await SendPayloadAsync(socket, payload, cancellationToken);
    }

    private static async Task SendPayloadAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }
}
