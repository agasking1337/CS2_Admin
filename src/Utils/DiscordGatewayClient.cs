using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordGatewayClient
{
    private const string DefaultGatewayUrl = "wss://gateway.discord.gg";
    private const int MaxIdentifiesPerHour = 60;

    private readonly ISwiftlyCore _core;
    private readonly string _botToken;
    private readonly object _lifecycleLock = new();
    private readonly Queue<DateTime> _identifyTimestamps = new();
    private CancellationTokenSource? _cts;
    private ClientWebSocket? _socket;
    private Task? _gatewayTask;
    private int? _sequence;
    private string? _sessionId;
    private string? _resumeGatewayUrl;
    private volatile bool _heartbeatAcked = true;
    private volatile bool _ready;
    private volatile bool _fatalClose;
    private volatile bool _useMembersIntent = true;

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
        lock (_lifecycleLock)
        {
            if (_cts is { IsCancellationRequested: false } && _gatewayTask is { IsCompleted: false })
                return;

            _fatalClose = false;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _gatewayTask = Task.Run(() => RunLoopAsync(token));
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            _cts?.Cancel();
            _cts = null;

            try
            {
                _socket?.Abort();
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Discord gateway socket abort failed");
            }

            _socket = null;
            _gatewayTask = null;
            _sequence = null;
            _sessionId = null;
            _resumeGatewayUrl = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            _ready = false;
            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                using var socket = new ClientWebSocket();
                _socket = socket;

                var baseUrl = string.IsNullOrWhiteSpace(_resumeGatewayUrl) || string.IsNullOrWhiteSpace(_sessionId)
                    ? DefaultGatewayUrl
                    : _resumeGatewayUrl!;
                await socket.ConnectAsync(new Uri($"{baseUrl.TrimEnd('/')}/?v=10&encoding=json"), connectionCts.Token);
                await ReceiveMessagesAsync(socket, connectionCts);
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
                connectionCts.Cancel();
                _socket = null;
            }

            if (_fatalClose)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Discord gateway closed with a fatal code; not reconnecting. Fix the bot token/intents and reload the plugin.");
                break;
            }

            failures = _ready ? 0 : failures + 1;
            var delaySeconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures, 6)));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ReceiveMessagesAsync(ClientWebSocket socket, CancellationTokenSource connectionCts)
    {
        var cancellationToken = connectionCts.Token;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var payload = await ReceivePayloadAsync(socket, cancellationToken);
            if (payload == null)
                return;

            if (string.IsNullOrWhiteSpace(payload))
                continue;

            await HandlePayloadAsync(socket, payload, connectionCts);
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
                var code = (int?)result.CloseStatus ?? 0;
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord gateway closed by server code={Code} reason={Reason}", code, result.CloseStatusDescription ?? string.Empty);

                if (code == 4014 && _useMembersIntent)
                {
                    _useMembersIntent = false;
                    _sessionId = null;
                    _sequence = null;
                    _resumeGatewayUrl = null;
                    _core.Logger.LogErrorIfEnabled("[CS2_Admin] Discord gateway rejected the privileged GUILD_MEMBERS intent. Enable 'Server Members Intent' in the Discord Developer Portal (Bot tab) to auto-unlink members who leave. Retrying without it so buttons keep working.");
                }
                else if (code is 4004 or 4010 or 4011 or 4012 or 4013 or 4014)
                {
                    _fatalClose = true;
                }
                else if (code is 4007 or 4009 || code is 1000 or 1001)
                {
                    _sessionId = null;
                    _sequence = null;
                    _resumeGatewayUrl = null;
                }

                try
                {
                    if (socket.State == WebSocketState.CloseReceived)
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
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

    private async Task HandlePayloadAsync(ClientWebSocket socket, string payload, CancellationTokenSource connectionCts)
    {
        var cancellationToken = connectionCts.Token;
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
                            _ready = true;
                            if (readyData.TryGetProperty("session_id", out var sessionIdElement))
                            {
                                _sessionId = sessionIdElement.GetString();
                            }
                            if (readyData.TryGetProperty("resume_gateway_url", out var resumeUrlElement) && resumeUrlElement.ValueKind == JsonValueKind.String)
                            {
                                _resumeGatewayUrl = resumeUrlElement.GetString();
                            }
                        }
                        else if (eventName == "RESUMED")
                        {
                            _ready = true;
                        }
                        else if (eventName == "INTERACTION_CREATE" && root.TryGetProperty("d", out var dElement))
                        {
                            var interactionData = dElement.Clone();
                            _ = Task.Run(() => _onInteraction(interactionData));
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
                                _ = Task.Run(() => _onGuildMemberRemoved(discordId, guildIdElement.GetString() ?? string.Empty));
                            }
                        }
                    }
                    break;
                }
            case 10:
                {
                    var heartbeatIntervalMs = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                    _heartbeatAcked = true;
                    _ = Task.Run(() => RunHeartbeatLoopAsync(socket, heartbeatIntervalMs, connectionCts));
                    if (!string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
                    {
                        await SendResumeAsync(socket, cancellationToken);
                    }
                    else
                    {
                        if (!TryAcquireIdentifySlot())
                        {
                            _fatalClose = true;
                            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Discord gateway identify limit reached ({Max}/hour); stopping to protect the bot token.", MaxIdentifiesPerHour);
                            socket.Abort();
                            return;
                        }
                        await SendIdentifyAsync(socket, cancellationToken);
                    }
                    break;
                }
            case 1:
                await SendHeartbeatAsync(socket, cancellationToken);
                break;
            case 7:
                await CloseForReconnectAsync(socket);
                break;
            case 9:
                {
                    var resumable = root.TryGetProperty("d", out var dResumable) && dResumable.ValueKind == JsonValueKind.True;
                    if (!resumable)
                    {
                        _sessionId = null;
                        _sequence = null;
                        _resumeGatewayUrl = null;
                        await Task.Delay(Random.Shared.Next(1000, 5000), cancellationToken);
                    }
                    await CloseForReconnectAsync(socket);
                    break;
                }
            case 11:
                _heartbeatAcked = true;
                break;
        }
    }

    private bool TryAcquireIdentifySlot()
    {
        var now = DateTime.UtcNow;
        while (_identifyTimestamps.Count > 0 && (now - _identifyTimestamps.Peek()).TotalHours >= 1)
            _identifyTimestamps.Dequeue();

        if (_identifyTimestamps.Count >= MaxIdentifiesPerHour)
            return false;

        _identifyTimestamps.Enqueue(now);
        return true;
    }

    private async Task CloseForReconnectAsync(ClientWebSocket socket)
    {
        try
        {
            // Non-1000/1001 close code keeps the session resumable.
            await socket.CloseAsync((WebSocketCloseStatus)4000, "reconnect", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Discord gateway reconnect close failed");
            try { socket.Abort(); } catch { }
        }
    }

    private async Task RunHeartbeatLoopAsync(ClientWebSocket socket, int heartbeatIntervalMs, CancellationTokenSource connectionCts)
    {
        var cancellationToken = connectionCts.Token;
        try
        {
            var jitterMs = Random.Shared.Next(0, Math.Max(heartbeatIntervalMs, 1));
            await Task.Delay(jitterMs, cancellationToken);

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                if (!_heartbeatAcked)
                {
                    _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord gateway heartbeat not acknowledged; reconnecting (session kept for resume).");
                    socket.Abort();
                    return;
                }

                _heartbeatAcked = false;
                await SendHeartbeatAsync(socket, cancellationToken);
                await Task.Delay(heartbeatIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord gateway heartbeat loop ended: {Message}", ex.Message);
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
                intents = _useMembersIntent ? 1 | (1 << 1) : 1, // GUILDS | GUILD_MEMBERS (privileged)
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
