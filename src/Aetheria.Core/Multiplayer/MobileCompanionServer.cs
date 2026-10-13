using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aetheria.Core.Domain;
using Aetheria.Core.Services;

namespace Aetheria.Core.Multiplayer;

public sealed class MobileCompanionServer : IAsyncDisposable
{
    private readonly MultiplayerHostServer _host;
    private TcpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, SseClientContext> _sseClients = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public int ListeningPort { get; private set; }
    public bool IsRunning { get; private set; }

    private sealed class SseClientContext
    {
        public required string ClientId { get; init; }
        public required TcpClient TcpClient { get; init; }
        public required NetworkStream Stream { get; init; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    public MobileCompanionServer(MultiplayerHostServer host)
    {
        _host = host;
    }

    public void Start(int port, CancellationToken parentCt = default)
    {
        if (IsRunning) return;
        ListeningPort = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);

        try
        {
            _httpListener = new TcpListener(IPAddress.Any, port);
            _httpListener.Start();
            IsRunning = true;
            _ = AcceptHttpClientsLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _host.SubmitSystemLog($"Mobile companion server note: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();

        try { _httpListener?.Stop(); } catch { }

        foreach (var kv in _sseClients)
        {
            try { kv.Value.TcpClient.Close(); } catch { }
        }
        _sseClients.Clear();
        await Task.CompletedTask;
    }

    public void BroadcastMessage(SessionMessage message)
    {
        if (!IsRunning) return;
        var json = JsonSerializer.Serialize(message);
        _ = PushSseEventAsync("message", json);
    }

    public void BroadcastAiDelta(string streamId, string delta)
    {
        if (!IsRunning) return;
        var payload = JsonSerializer.Serialize(new { streamId, delta });
        _ = PushSseEventAsync("ai_delta", payload);
    }

    public void BroadcastSessionState(RoleplaySession session)
    {
        if (!IsRunning) return;
        var summary = new
        {
            session.SessionId,
            session.Title,
            session.GameMode,
            session.TurnNumber,
            session.CurrentLocation,
            session.ActiveWorldState,
            session.TurnOrderParticipantIds,
            Participants = session.Participants.Select(p => new
            {
                p.ParticipantId,
                p.DisplayName,
                p.PersonaName,
                p.IsHost,
                p.IsAiCharacter,
                p.IsConnected
            })
        };
        var json = JsonSerializer.Serialize(summary);
        _ = PushSseEventAsync("session_state", json);
    }

    private async Task PushSseEventAsync(string eventName, string dataJson)
    {
        var sseText = $"event: {eventName}\ndata: {dataJson}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseText);

        foreach (var kv in _sseClients)
        {
            var ctx = kv.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    await ctx.WriteLock.WaitAsync();
                    try
                    {
                        if (ctx.TcpClient.Connected)
                        {
                            await ctx.Stream.WriteAsync(bytes);
                            await ctx.Stream.FlushAsync();
                        }
                    }
                    finally
                    {
                        ctx.WriteLock.Release();
                    }
                }
                catch
                {
                    _sseClients.TryRemove(ctx.ClientId, out _);
                    try { ctx.TcpClient.Close(); } catch { }
                }
            });
        }
        await Task.CompletedTask;
    }

    private async Task AcceptHttpClientsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _httpListener != null)
        {
            try
            {
                var client = await _httpListener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                _ = HandleHttpRequestAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (!IsRunning) break;
            }
        }
    }

    private async Task HandleHttpRequestAsync(TcpClient tcpClient, CancellationToken ct)
    {
        using var stream = tcpClient.GetStream();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            // Read HTTP request header
            var headerBuffer = new byte[8192];
            int read = await stream.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), timeoutCts.Token);
            if (read <= 0)
            {
                tcpClient.Close();
                return;
            }

            var requestHeader = Encoding.UTF8.GetString(headerBuffer, 0, read);
            var firstLine = requestHeader.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var parts = firstLine.Split(' ');
            if (parts.Length < 2)
            {
                tcpClient.Close();
                return;
            }

            var method = parts[0].ToUpperInvariant();
            var path = parts[1];

            // Strip query string for route dispatch
            var rawPath = path.Contains('?') ? path.Split('?')[0] : path;

            // Handle OPTIONS preflight
            if (method == "OPTIONS")
            {
                await SendHttpResponseAsync(stream, 204, "No Content", "text/plain", string.Empty);
                return;
            }

            if (method == "GET" && (rawPath == "/" || rawPath == "/index.html"))
            {
                var html = MobileCompanionHtml.GetMobileAppHtml(_host.ListeningPort, ListeningPort, _host.InviteToken);
                await SendHttpResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html);
                return;
            }

            if (method == "GET" && rawPath == "/manifest.json")
            {
                var manifest = MobileCompanionHtml.GetManifestJson();
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", manifest);
                return;
            }

            if (method == "GET" && rawPath == "/api/ping")
            {
                var pingResp = JsonSerializer.Serialize(new
                {
                    status = "ok",
                    appName = "Aetheria",
                    sessionTitle = _host.ActiveSession.Title,
                    gameMode = _host.ActiveSession.GameMode.ToString(),
                    tcpPort = _host.ListeningPort,
                    httpPort = ListeningPort
                });
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", pingResp);
                return;
            }

            if (method == "GET" && rawPath == "/api/session")
            {
                var snapshot = new
                {
                    _host.ActiveSession.SessionId,
                    _host.ActiveSession.Title,
                    GameMode = _host.ActiveSession.GameMode.ToString(),
                    _host.ActiveSession.TurnNumber,
                    _host.ActiveSession.CurrentLocation,
                    _host.ActiveSession.ActiveWorldState,
                    HostPersonaName = _host.HostPersona.Name,
                    _host.ActiveSession.Participants,
                    Messages = _host.ActiveSession.Messages.TakeLast(60),
                    Characters = _host.ActiveCharacters.Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Title,
                        c.Description,
                        c.Appearance,
                        c.Personality,
                        c.Background,
                        c.Tags
                    }),
                    World = _host.ActiveWorld == null ? null : new
                    {
                        _host.ActiveWorld.Id,
                        _host.ActiveWorld.Name,
                        Description = _host.ActiveWorld.Setting,
                        _host.ActiveWorld.Genre,
                        _host.ActiveWorld.Tone,
                        _host.ActiveWorld.WorldHistory,
                        _host.ActiveWorld.Locations,
                        _host.ActiveWorld.Factions,
                        _host.ActiveWorld.LoreEntries
                    }
                };
                var json = JsonSerializer.Serialize(snapshot);
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", json);
                return;
            }

            if (method == "GET" && rawPath == "/api/translate")
            {
                string text = "";
                string target = "tr";
                string source = "auto";
                if (path.Contains('?'))
                {
                    var q = path.Substring(path.IndexOf('?') + 1);
                    foreach (var part in q.Split('&'))
                    {
                        var kv = part.Split('=');
                        if (kv.Length == 2)
                        {
                            var k = Uri.UnescapeDataString(kv[0].Replace("+", "%20"));
                            var v = Uri.UnescapeDataString(kv[1].Replace("+", "%20"));
                            if (k.Equals("text", StringComparison.OrdinalIgnoreCase)) text = v;
                            else if (k.Equals("target", StringComparison.OrdinalIgnoreCase)) target = v;
                            else if (k.Equals("source", StringComparison.OrdinalIgnoreCase)) source = v;
                        }
                    }
                }

                var translationService = PlatformContext.Current?.Translation ?? new TranslationService();
                var translated = await translationService.TranslateAsync(text, target, source, ct);
                var transResp = JsonSerializer.Serialize(new { original = text, translated, target, source }, JsonOptions);
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", transResp);
                return;
            }

            if (method == "GET" && rawPath == "/api/stream")
            {
                // Upgrade to Server-Sent Events (SSE)
                var clientId = Guid.NewGuid().ToString("N");
                var sseHeader = "HTTP/1.1 200 OK\r\n" +
                                "Content-Type: text/event-stream\r\n" +
                                "Cache-Control: no-cache\r\n" +
                                "Connection: keep-alive\r\n" +
                                "Access-Control-Allow-Origin: *\r\n\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(sseHeader), ct);
                await stream.FlushAsync(ct);

                var sseCtx = new SseClientContext
                {
                    ClientId = clientId,
                    TcpClient = tcpClient,
                    Stream = stream
                };
                _sseClients[clientId] = sseCtx;

                // Send initial connect ping
                var initJson = JsonSerializer.Serialize(new { status = "connected", clientId, session = _host.ActiveSession.Title });
                await stream.WriteAsync(Encoding.UTF8.GetBytes($"event: connect\ndata: {initJson}\n\n"), ct);
                await stream.FlushAsync(ct);

                // Keep socket alive while client reads
                var dummyBuf = new byte[128];
                try
                {
                    while (!ct.IsCancellationRequested && tcpClient.Connected)
                    {
                        int r = await stream.ReadAsync(dummyBuf.AsMemory(0, dummyBuf.Length), ct);
                        if (r <= 0) break;
                    }
                }
                finally
                {
                    _sseClients.TryRemove(clientId, out _);
                }
                return;
            }

            if (method == "POST" && rawPath == "/api/action")
            {
                string body = ExtractBody(requestHeader, headerBuffer, read, stream);
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var token = root.TryGetProperty("inviteToken", out var tProp) ? tProp.GetString() ?? "" : "";
                if (!string.Equals(token.Trim(), _host.InviteToken.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var err = JsonSerializer.Serialize(new { error = "Invalid invite token." });
                    await SendHttpResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", err);
                    return;
                }

                var content = root.TryGetProperty("content", out var cProp) ? cProp.GetString() ?? "" : "";
                var actionTypeStr = root.TryGetProperty("actionType", out var aProp) ? aProp.GetString() ?? "Speak" : "Speak";
                var isWhisper = root.TryGetProperty("isPrivateWhisper", out var wProp) && wProp.GetBoolean();
                var targetId = root.TryGetProperty("targetParticipantId", out var tpProp) ? tpProp.GetString() : null;
                var playerName = root.TryGetProperty("playerName", out var pnProp) ? pnProp.GetString() ?? "Mobile Player" : "Mobile Player";
                var participantId = root.TryGetProperty("participantId", out var pIdProp) ? pIdProp.GetString() ?? "" : "";

                if (!Enum.TryParse<RoleplayActionType>(actionTypeStr, true, out var actionType))
                {
                    actionType = RoleplayActionType.Speak;
                }

                // Ensure participant exists in session
                var p = _host.EnsureMobileParticipant(participantId, playerName);

                var payload = new PlayerActionPayload
                {
                    ActionType = actionType,
                    Content = content,
                    IsPrivateWhisper = isWhisper,
                    TargetParticipantId = targetId,
                    RequestAiResponse = true
                };

                await _host.ProcessMobileActionAsync(p.ParticipantId, payload, ct);

                var resp = JsonSerializer.Serialize(new { status = "submitted", participantId = p.ParticipantId });
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", resp);
                return;
            }

            if (method == "POST" && rawPath == "/api/sync-character")
            {
                string body = ExtractBody(requestHeader, headerBuffer, read, stream);
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var token = root.TryGetProperty("inviteToken", out var tProp) ? tProp.GetString() ?? "" : "";
                if (!string.Equals(token.Trim(), _host.InviteToken.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var err = JsonSerializer.Serialize(new { error = "Invalid invite token." });
                    await SendHttpResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", err);
                    return;
                }

                if (root.TryGetProperty("character", out var charElement))
                {
                    var syncedChar = JsonSerializer.Deserialize<Character>(charElement.GetRawText());
                    if (syncedChar != null)
                    {
                        _host.UpsertSyncedCharacterOnHost(syncedChar);
                        _ = _host.BroadcastToAllClientsAsync(new MultiplayerPacket
                        {
                            Type = MultiplayerPacketType.CharacterSync,
                            SessionId = _host.ActiveSession.SessionId,
                            SenderParticipantId = "mobile-client",
                            PayloadJson = JsonSerializer.Serialize(syncedChar)
                        }, ct);

                        var resp = JsonSerializer.Serialize(new { status = "synced", characterId = syncedChar.Id, characterName = syncedChar.Name });
                        await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", resp);
                        return;
                    }
                }

                var badReq = JsonSerializer.Serialize(new { error = "Missing character JSON payload." });
                await SendHttpResponseAsync(stream, 400, "Bad Request", "application/json; charset=utf-8", badReq);
                return;
            }

            if (method == "POST" && rawPath == "/api/lobby/identity")
            {
                string body = ExtractBody(requestHeader, headerBuffer, read, stream);
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var token = root.TryGetProperty("inviteToken", out var tProp) ? tProp.GetString() ?? "" : "";
                if (!string.Equals(token.Trim(), _host.InviteToken.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var err = JsonSerializer.Serialize(new { error = "Invalid invite token." });
                    await SendHttpResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", err);
                    return;
                }

                var participantId = root.TryGetProperty("participantId", out var pidProp) ? pidProp.GetString() ?? "" : "";
                var personaName = root.TryGetProperty("personaName", out var pnProp) ? pnProp.GetString() ?? "Player" : "Player";
                var displayName = root.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? personaName : personaName;
                var characterId = root.TryGetProperty("characterId", out var cidProp) ? cidProp.GetString() : null;
                var isReady = root.TryGetProperty("isReady", out var rProp) && rProp.GetBoolean();

                var p = _host.EnsureMobileParticipant(participantId, personaName);
                await _host.UpdateParticipantIdentityAsync(
                    p.ParticipantId,
                    personaName,
                    displayName,
                    characterId: characterId,
                    isReady: isReady,
                    ct: ct);

                var resp = JsonSerializer.Serialize(new { status = "updated", participantId = p.ParticipantId, personaName });
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", resp);
                return;
            }

            if (method == "POST" && rawPath == "/api/lobby/ready")
            {
                string body = ExtractBody(requestHeader, headerBuffer, read, stream);
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var token = root.TryGetProperty("inviteToken", out var tProp) ? tProp.GetString() ?? "" : "";
                if (!string.Equals(token.Trim(), _host.InviteToken.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var err = JsonSerializer.Serialize(new { error = "Invalid invite token." });
                    await SendHttpResponseAsync(stream, 401, "Unauthorized", "application/json; charset=utf-8", err);
                    return;
                }

                var participantId = root.TryGetProperty("participantId", out var pidProp) ? pidProp.GetString() ?? "" : "";
                var isReady = root.TryGetProperty("isReady", out var rProp) && rProp.GetBoolean();
                var p = _host.ActiveSession.Participants.FirstOrDefault(x => x.ParticipantId == participantId);
                if (p != null)
                {
                    await _host.UpdateParticipantIdentityAsync(p.ParticipantId, p.PersonaName, p.DisplayName, isReady: isReady, ct: ct);
                }

                var resp = JsonSerializer.Serialize(new { status = "updated", participantId, isReady });
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", resp);
                return;
            }

            if (method == "POST" && rawPath == "/api/round/resolve")
            {
                await _host.ResolveRoundAndNarrateConsequencesAsync(ct);
                var resp = JsonSerializer.Serialize(new { status = "resolving", round = _host.ActiveSession.TurnNumber });
                await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", resp);
                return;
            }

            // Fallback 404
            await SendHttpResponseAsync(stream, 404, "Not Found", "text/plain", "Endpoint not found.");
        }
        catch
        {
            try { tcpClient.Close(); } catch { }
        }
    }

    private static string ExtractBody(string requestHeader, byte[] headerBuffer, int readTotal, NetworkStream stream)
    {
        int headerEnd = requestHeader.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0) return string.Empty;

        int bodyStart = headerEnd + 4;
        var existingBody = requestHeader[bodyStart..];

        // Read Content-Length if more bytes are pending
        int contentLength = 0;
        foreach (var line in requestHeader.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                break;
            }
        }

        if (contentLength > 0 && Encoding.UTF8.GetByteCount(existingBody) < contentLength)
        {
            var remaining = contentLength - Encoding.UTF8.GetByteCount(existingBody);
            var buf = new byte[remaining];
            int read = stream.Read(buf, 0, remaining);
            if (read > 0)
            {
                existingBody += Encoding.UTF8.GetString(buf, 0, read);
            }
        }
        return existingBody;
    }

    private static async Task SendHttpResponseAsync(NetworkStream stream, int statusCode, string statusReason, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var headers = $"HTTP/1.1 {statusCode} {statusReason}\r\n" +
                      $"Content-Type: {contentType}\r\n" +
                      $"Content-Length: {bytes.Length}\r\n" +
                      "Access-Control-Allow-Origin: *\r\n" +
                      "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                      "Access-Control-Allow-Headers: Content-Type, Authorization\r\n" +
                      "Connection: close\r\n\r\n";

        var headerBytes = Encoding.UTF8.GetBytes(headers);
        await stream.WriteAsync(headerBytes);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes);
        }
        await stream.FlushAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
