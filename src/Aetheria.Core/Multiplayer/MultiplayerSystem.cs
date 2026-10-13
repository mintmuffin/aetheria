using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aetheria.Core.AI;
using Aetheria.Core.Domain;
using Aetheria.Core.Engine;

namespace Aetheria.Core.Multiplayer;

public enum MultiplayerPacketType
{
    HandshakeRequest,
    HandshakeAccepted,
    HandshakeRejected,
    SessionSnapshot,
    PlayerActionSubmit,
    MessageBroadcast,
    TurnStateUpdate,
    WorldStateUpdate,
    AiStreamStart,
    AiStreamChunk,
    AiStreamEnd,
    ParticipantListUpdate,
    CharacterSync,
    SystemNotice,
    Ping,
    Pong,
    ParticipantUpdate,
    LobbyStateUpdate
}

public sealed class ParticipantUpdatePayload
{
    public string ParticipantId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PersonaId { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    public string PersonaSummary { get; set; } = string.Empty;
    public string? CharacterId { get; set; }
    public bool IsReady { get; set; }
    public string? RoleTag { get; set; }
}

public sealed class LobbyStatePayload
{
    public string SessionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string GameMode { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public bool GameStarted { get; set; }
    public List<SessionParticipant> Participants { get; set; } = new();
}

public sealed class MultiplayerPacket
{
    public string PacketId { get; set; } = Guid.NewGuid().ToString("N");
    public MultiplayerPacketType Type { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string SenderParticipantId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HandshakeRequestPayload
{
    public string InviteToken { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Player";
    public string PersonaId { get; set; } = string.Empty;
    public string PersonaName { get; set; } = "Traveler";
    public string PersonaSummary { get; set; } = string.Empty;
    public Persona? FullPersona { get; set; }
    public Character? CustomCharacter { get; set; }
    public string? ReconnectParticipantId { get; set; }
}

public sealed class HandshakeResponsePayload
{
    public bool Accepted { get; set; }
    public string AssignedParticipantId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public RoleplaySession? InitialSessionSnapshot { get; set; }
    public List<Character> HostCharacters { get; set; } = new();
    public World? HostWorld { get; set; }
}

public sealed class PlayerActionPayload
{
    public string ActionId { get; set; } = Guid.NewGuid().ToString("N");
    public RoleplayActionType ActionType { get; set; } = RoleplayActionType.Speak;
    public string Content { get; set; } = string.Empty;
    public bool IsPrivateWhisper { get; set; }
    public string? TargetParticipantId { get; set; }
    public bool RequestAiResponse { get; set; } = true;
}

public sealed class AiStreamPayload
{
    public string StreamId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string DeltaOrFullContent { get; set; } = string.Empty;
    public bool IsNarrator { get; set; }
}

public sealed record DiscoveredLanSession(
    string SessionId,
    string Title,
    string HostName,
    string GameMode,
    string HostAddress,
    int Port,
    int CurrentPlayers,
    int MaxPlayers);

public sealed record NetworkAdapterDiagnostic(
    string AdapterName,
    string IpAddress,
    string AdapterCategory, // LAN, Hamachi/VPN, Loopback
    bool IsUp);

public static class NetworkDiagnosticsHelper
{
    public static List<NetworkAdapterDiagnostic> GetLocalNetworkInterfaces()
    {
        var list = new List<NetworkAdapterDiagnostic>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    var category = "LAN / Wi-Fi";
                    if (IPAddress.IsLoopback(ua.Address))
                        category = "Local Loopback (This PC)";
                    else if (ip.StartsWith("25.", StringComparison.Ordinal) || ni.Name.Contains("Hamachi", StringComparison.OrdinalIgnoreCase))
                        category = "LogMeIn Hamachi Virtual LAN";
                    else if (ip.StartsWith("26.", StringComparison.Ordinal) || ni.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase))
                        category = "Radmin VPN Virtual LAN";
                    else if (ip.StartsWith("100.", StringComparison.Ordinal) || ni.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase))
                        category = "Tailscale / Carrier VPN";
                    else if (ni.Description.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase))
                        category = "ZeroTier Virtual LAN";

                    list.Add(new NetworkAdapterDiagnostic(ni.Name, ip, category, true));
                }
            }
        }
        catch
        {
            list.Add(new NetworkAdapterDiagnostic("Loopback", "127.0.0.1", "Local Loopback", true));
        }
        return list;
    }

    public static async Task<(bool Reachable, string DiagnosticMessage)> TestTcpEndpointAsync(string host, int port, int timeoutMs = 3000)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return (true, $"Successfully established TCP connection to {host}:{port}.");
        }
        catch (OperationCanceledException)
        {
            return (false, $"Connection to {host}:{port} timed out after {timeoutMs}ms. Check Windows Firewall, port forwarding, or Virtual LAN (Hamachi) status.");
        }
        catch (Exception ex)
        {
            return (false, $"Cannot connect to {host}:{port}: {ex.Message}");
        }
    }
}

internal static class PacketFrameIO
{
    public const int MaxFrameBytes = 2 * 1024 * 1024; // 2 MB max packet limit

    public static async Task WritePacketAsync(Stream stream, MultiplayerPacket packet, SemaphoreSlim writeLock, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(packet);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        if (payloadBytes.Length > MaxFrameBytes)
            throw new InvalidOperationException("Packet exceeds 2 MB maximum size limit.");

        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payloadBytes.Length);

        await writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(lengthPrefix, ct);
            await stream.WriteAsync(payloadBytes, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public static async Task<MultiplayerPacket?> ReadPacketAsync(Stream stream, CancellationToken ct)
    {
        var lengthPrefix = new byte[4];
        int read = await ReadExactAsync(stream, lengthPrefix, 4, ct);
        if (read == 0) return null; // Clean disconnect
        if (read < 4) throw new EndOfStreamException("Incomplete frame header.");

        int length = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
        if (length <= 0 || length > MaxFrameBytes)
            throw new InvalidDataException($"Invalid frame length: {length} bytes.");

        var payloadBytes = new byte[length];
        int payloadRead = await ReadExactAsync(stream, payloadBytes, length, ct);
        if (payloadRead < length)
            throw new EndOfStreamException("Incomplete frame body.");

        var json = Encoding.UTF8.GetString(payloadBytes);
        return JsonSerializer.Deserialize<MultiplayerPacket>(json);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (n == 0) return offset;
            offset += n;
        }
        return offset;
    }
}

public sealed class MultiplayerHostServer : IAsyncDisposable
{
    public const int DefaultDiscoveryUdpPort = 47891;

    private readonly RoleplayEngine _engine;
    private readonly AiOrchestrator _aiOrchestrator;
    private readonly ConcurrentDictionary<string, ConnectedClientContext> _clients = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedActionIds = new();
    private readonly SemaphoreSlim _aiGenerationLock = new(1, 1);

    private TcpListener? _tcpListener;
    private UdpClient? _udpDiscoveryListener;
    private MobileCompanionServer? _mobileCompanionServer;
    private CancellationTokenSource? _cts;

    public RoleplaySession ActiveSession { get; private set; }
    public World? ActiveWorld { get; set; }
    public List<Character> ActiveCharacters { get; set; } = new();
    public Persona HostPersona { get; set; }
    public ModelPreset ActiveModelPreset { get; set; }
    public int ListeningPort { get; private set; }
    public int MobileCompanionPort => ListeningPort + 1;
    public string InviteToken { get; private set; }
    public bool IsRunning { get; private set; }

    public event Action<string>? OnLogMessage;
    public event Action<RoleplaySession>? OnSessionStateChanged;
    public event Action<SessionMessage>? OnMessageAdded;
    public event Action<string, string>? OnAiStreamDelta; // (streamId, delta)

    private sealed class ConnectedClientContext
    {
        public required string ParticipantId { get; init; }
        public required TcpClient TcpClient { get; init; }
        public required NetworkStream Stream { get; init; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public Queue<DateTimeOffset> RecentMessageTimestamps { get; } = new();
    }

    public MultiplayerHostServer(
        RoleplayEngine engine,
        AiOrchestrator aiOrchestrator,
        RoleplaySession session,
        Persona hostPersona,
        World? world,
        List<Character> characters,
        ModelPreset modelPreset)
    {
        _engine = engine;
        _aiOrchestrator = aiOrchestrator;
        ActiveSession = session;
        HostPersona = hostPersona;
        ActiveWorld = world;
        ActiveCharacters = characters;
        ActiveModelPreset = modelPreset;
        InviteToken = GenerateSecureInviteToken();

        // Ensure host server always starts with a clean roster: only the Host and scenario AI characters.
        ActiveSession.Participants.RemoveAll(p => !p.IsHost && p.ParticipantId != "host-player" && !p.IsAiCharacter);
        var hostPart = ActiveSession.Participants.FirstOrDefault(p => p.IsHost || p.ParticipantId == "host-player");
        if (hostPart != null)
        {
            hostPart.IsConnected = true;
            hostPart.IsReady = true;
        }
    }

    public void Start(int port = 7777, bool enableLanDiscovery = true, string? customInviteToken = null)
    {
        if (IsRunning) return;
        ListeningPort = port;
        if (!string.IsNullOrWhiteSpace(customInviteToken))
        {
            InviteToken = customInviteToken.Trim();
        }

        _cts = new CancellationTokenSource();
        _tcpListener = new TcpListener(IPAddress.Any, port);
        _tcpListener.Start();
        IsRunning = true;

        Log($"Host server started on TCP port {port}. Invite Token: {InviteToken}");
        _ = AcceptClientsLoopAsync(_cts.Token);

        _mobileCompanionServer = new MobileCompanionServer(this);
        _mobileCompanionServer.Start(port + 1, _cts.Token);
        Log($"Mobile companion server active on HTTP port {port + 1} (Android App & Web Companion).");

        if (enableLanDiscovery)
        {
            StartLanDiscoveryResponder(_cts.Token);
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        try { _tcpListener?.Stop(); } catch { }
        try { _udpDiscoveryListener?.Close(); } catch { }

        if (_mobileCompanionServer != null)
        {
            try { await _mobileCompanionServer.StopAsync(); } catch { }
            _mobileCompanionServer = null;
        }

        foreach (var kv in _clients)
        {
            try { kv.Value.TcpClient.Close(); } catch { }
        }
        _clients.Clear();
        Log("Host server stopped.");
        await Task.CompletedTask;
    }

    public async Task SubmitHostActionAsync(
        RoleplayActionType actionType,
        string content,
        bool isPrivateWhisper = false,
        string? targetParticipantId = null)
    {
        await ProcessParticipantActionInternalAsync(
            "host-player",
            new PlayerActionPayload
            {
                ActionType = actionType,
                Content = content,
                IsPrivateWhisper = isPrivateWhisper,
                TargetParticipantId = targetParticipantId,
                RequestAiResponse = true
            },
            _cts?.Token ?? CancellationToken.None);
    }

    private async Task AcceptClientsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _tcpListener != null)
        {
            try
            {
                var tcpClient = await _tcpListener.AcceptTcpClientAsync(ct);
                tcpClient.NoDelay = true;
                _ = HandleClientConnectionAsync(tcpClient, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (IsRunning) Log($"Accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientConnectionAsync(TcpClient tcpClient, CancellationToken ct)
    {
        string? assignedParticipantId = null;
        var stream = tcpClient.GetStream();
        var writeLock = new SemaphoreSlim(1, 1);

        try
        {
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(10));

            var firstPacket = await PacketFrameIO.ReadPacketAsync(stream, handshakeCts.Token);
            if (firstPacket == null || firstPacket.Type != MultiplayerPacketType.HandshakeRequest)
            {
                tcpClient.Close();
                return;
            }

            var req = JsonSerializer.Deserialize<HandshakeRequestPayload>(firstPacket.PayloadJson);
            if (req == null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(req.InviteToken.Trim()),
                    Encoding.UTF8.GetBytes(InviteToken)))
            {
                var reject = new HandshakeResponsePayload
                {
                    Accepted = false,
                    Reason = "Invalid Invite Token. Authorization denied."
                };
                await PacketFrameIO.WritePacketAsync(stream, new MultiplayerPacket
                {
                    Type = MultiplayerPacketType.HandshakeRejected,
                    SessionId = ActiveSession.SessionId,
                    PayloadJson = JsonSerializer.Serialize(reject)
                }, writeLock, ct);
                Log("Rejected client connection due to invalid Invite Token.");
                tcpClient.Close();
                return;
            }

            // Check player capacity
            int humanCount = ActiveSession.Participants.Count(p => !p.IsAiCharacter && p.IsConnected);
            bool isReconnect = !string.IsNullOrWhiteSpace(req.ReconnectParticipantId) &&
                               ActiveSession.Participants.Any(p => p.ParticipantId == req.ReconnectParticipantId);

            if (!isReconnect && humanCount >= ActiveSession.MaxPlayers)
            {
                var reject = new HandshakeResponsePayload
                {
                    Accepted = false,
                    Reason = $"Session is full ({ActiveSession.MaxPlayers}/{ActiveSession.MaxPlayers} human participants)."
                };
                await PacketFrameIO.WritePacketAsync(stream, new MultiplayerPacket
                {
                    Type = MultiplayerPacketType.HandshakeRejected,
                    SessionId = ActiveSession.SessionId,
                    PayloadJson = JsonSerializer.Serialize(reject)
                }, writeLock, ct);
                tcpClient.Close();
                return;
            }

            // Register or reconnect participant
            var fullPersonaSummary = req.PersonaSummary;
            if (req.FullPersona != null)
            {
                fullPersonaSummary = $"{req.FullPersona.Description} Appearance: {req.FullPersona.Appearance} Personality: {req.FullPersona.Personality} Background: {req.FullPersona.Background}".Trim();
                Services.PlatformContext.Current?.Repository.SavePersona(req.FullPersona);
            }
            if (req.CustomCharacter != null)
            {
                if (string.IsNullOrWhiteSpace(fullPersonaSummary))
                {
                    fullPersonaSummary = $"{req.CustomCharacter.Description} Appearance: {req.CustomCharacter.Appearance} Personality: {req.CustomCharacter.Personality} Background: {req.CustomCharacter.Background}".Trim();
                }
                UpsertSyncedCharacterOnHost(req.CustomCharacter);
            }

            SessionParticipant participant;
            if (isReconnect)
            {
                participant = ActiveSession.Participants.First(p => p.ParticipantId == req.ReconnectParticipantId);
                participant.IsConnected = true;
                participant.DisplayName = req.DisplayName;
                participant.PersonaName = req.PersonaName;
                participant.PersonaSummary = fullPersonaSummary;
                assignedParticipantId = participant.ParticipantId;
                Log($"Participant '{participant.PersonaName}' reconnected.");
            }
            else
            {
                assignedParticipantId = $"player-{Guid.NewGuid().ToString("N")[..8]}";
                participant = new SessionParticipant
                {
                    ParticipantId = assignedParticipantId,
                    DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? "Guest" : req.DisplayName.Trim(),
                    PersonaId = req.PersonaId,
                    PersonaName = string.IsNullOrWhiteSpace(req.PersonaName) ? req.DisplayName : req.PersonaName.Trim(),
                    PersonaSummary = fullPersonaSummary,
                    IsHost = false,
                    IsAiCharacter = false,
                    IsConnected = true,
                    CanTriggerAi = true
                };
                ActiveSession.Participants.Add(participant);
                if (!ActiveSession.TurnOrderParticipantIds.Contains(assignedParticipantId))
                {
                    ActiveSession.TurnOrderParticipantIds.Add(assignedParticipantId);
                }
                Log($"Participant '{participant.PersonaName}' ({assignedParticipantId}) joined the session (Character/Persona synced).");
            }

            var ctx = new ConnectedClientContext
            {
                ParticipantId = assignedParticipantId,
                TcpClient = tcpClient,
                Stream = stream
            };
            _clients[assignedParticipantId] = ctx;

            // Create sanitized snapshot for client (exclude private whispers not involving this participant)
            var snapshotForClient = CreateParticipantScopedSnapshot(assignedParticipantId);
            var acceptPayload = new HandshakeResponsePayload
            {
                Accepted = true,
                AssignedParticipantId = assignedParticipantId,
                Reason = "Connected to authoritative host (Characters & World synced).",
                InitialSessionSnapshot = snapshotForClient,
                HostCharacters = ActiveCharacters,
                HostWorld = ActiveWorld
            };

            await PacketFrameIO.WritePacketAsync(stream, new MultiplayerPacket
            {
                Type = MultiplayerPacketType.HandshakeAccepted,
                SessionId = ActiveSession.SessionId,
                SenderParticipantId = "host-player",
                PayloadJson = JsonSerializer.Serialize(acceptPayload)
            }, ctx.WriteLock, ct);

            // If client brought a custom character, broadcast it to any other connected clients
            if (req.CustomCharacter != null)
            {
                await BroadcastToAllClientsAsync(new MultiplayerPacket
                {
                    Type = MultiplayerPacketType.CharacterSync,
                    SessionId = ActiveSession.SessionId,
                    SenderParticipantId = assignedParticipantId,
                    PayloadJson = JsonSerializer.Serialize(req.CustomCharacter)
                }, ct);
            }

            // Broadcast updated participant list to everyone
            await BroadcastParticipantListAsync(ct);
            OnSessionStateChanged?.Invoke(ActiveSession);

            // Read client loop
            while (!ct.IsCancellationRequested && tcpClient.Connected)
            {
                var packet = await PacketFrameIO.ReadPacketAsync(stream, ct);
                if (packet == null) break;

                if (!CheckRateLimit(ctx))
                {
                    await SendPacketToClientAsync(ctx, new MultiplayerPacket
                    {
                        Type = MultiplayerPacketType.SystemNotice,
                        SessionId = ActiveSession.SessionId,
                        PayloadJson = "Rate limit exceeded. Please slow down."
                    }, ct);
                    continue;
                }

                await HandleClientPacketAsync(ctx, packet, ct);
            }
        }
        catch (Exception ex)
        {
            if (IsRunning && assignedParticipantId != null)
            {
                Log($"Client {assignedParticipantId} connection ended: {ex.Message}");
            }
        }
        finally
        {
            if (assignedParticipantId != null)
            {
                _clients.TryRemove(assignedParticipantId, out _);
                var p = ActiveSession.Participants.FirstOrDefault(x => x.ParticipantId == assignedParticipantId);
                if (p != null) p.IsConnected = false;
                _ = BroadcastParticipantListAsync(CancellationToken.None);
                OnSessionStateChanged?.Invoke(ActiveSession);
            }
            try { tcpClient.Close(); } catch { }
        }
    }

    public void SubmitSystemLog(string msg) => Log(msg);

    public SessionParticipant EnsureMobileParticipant(string participantId, string displayName)
    {
        var existing = ActiveSession.Participants.FirstOrDefault(p => p.ParticipantId == participantId);
        if (existing != null)
        {
            existing.IsConnected = true;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                existing.DisplayName = displayName.Trim();
                existing.PersonaName = displayName.Trim();
            }
            return existing;
        }

        var newId = string.IsNullOrWhiteSpace(participantId) ? $"mobile-{Guid.NewGuid().ToString("N")[..8]}" : participantId;
        var pName = string.IsNullOrWhiteSpace(displayName) ? "Mobile Player" : displayName.Trim();
        var newP = new SessionParticipant
        {
            ParticipantId = newId,
            DisplayName = pName,
            PersonaName = pName,
            PersonaSummary = "Mobile companion player connected via Android app / Web companion.",
            IsHost = false,
            IsAiCharacter = false,
            IsConnected = true,
            CanTriggerAi = true
        };
        ActiveSession.Participants.Add(newP);
        if (!ActiveSession.TurnOrderParticipantIds.Contains(newId))
        {
            ActiveSession.TurnOrderParticipantIds.Add(newId);
        }
        _ = BroadcastParticipantListAsync(CancellationToken.None);
        OnSessionStateChanged?.Invoke(ActiveSession);
        _mobileCompanionServer?.BroadcastSessionState(ActiveSession);
        return newP;
    }

    public async Task ProcessMobileActionAsync(string participantId, PlayerActionPayload payload, CancellationToken ct)
    {
        await ProcessParticipantActionInternalAsync(participantId, payload, ct);
    }

    public void UpsertSyncedCharacterOnHost(Character syncedChar)
    {
        var existingIdx = ActiveCharacters.FindIndex(c => c.Id == syncedChar.Id);
        if (existingIdx >= 0)
            ActiveCharacters[existingIdx] = syncedChar;
        else
            ActiveCharacters.Add(syncedChar);

        if (!ActiveSession.ActiveCharacterIds.Contains(syncedChar.Id))
            ActiveSession.ActiveCharacterIds.Add(syncedChar.Id);

        Services.PlatformContext.Current?.Repository.SaveCharacter(syncedChar);
        Log($"Synced custom character '{syncedChar.Name}' into session & library.");
    }

    public async Task SyncCharacterFromHostAsync(Character character, CancellationToken ct = default)
    {
        UpsertSyncedCharacterOnHost(character);
        await BroadcastToAllClientsAsync(new MultiplayerPacket
        {
            Type = MultiplayerPacketType.CharacterSync,
            SessionId = ActiveSession.SessionId,
            SenderParticipantId = "host-player",
            PayloadJson = JsonSerializer.Serialize(character)
        }, ct);
        await BroadcastParticipantListAsync(ct);
        OnSessionStateChanged?.Invoke(ActiveSession);
    }

    private async Task HandleClientPacketAsync(ConnectedClientContext ctx, MultiplayerPacket packet, CancellationToken ct)
    {
        switch (packet.Type)
        {
            case MultiplayerPacketType.Ping:
                await SendPacketToClientAsync(ctx, new MultiplayerPacket
                {
                    Type = MultiplayerPacketType.Pong,
                    SessionId = ActiveSession.SessionId
                }, ct);
                break;

            case MultiplayerPacketType.CharacterSync:
                var syncedChar = JsonSerializer.Deserialize<Character>(packet.PayloadJson);
                if (syncedChar != null)
                {
                    UpsertSyncedCharacterOnHost(syncedChar);
                    await BroadcastToAllClientsAsync(new MultiplayerPacket
                    {
                        Type = MultiplayerPacketType.CharacterSync,
                        SessionId = ActiveSession.SessionId,
                        SenderParticipantId = ctx.ParticipantId,
                        PayloadJson = JsonSerializer.Serialize(syncedChar)
                    }, ct);
                    OnSessionStateChanged?.Invoke(ActiveSession);
                }
                break;

            case MultiplayerPacketType.ParticipantUpdate:
                var updatePayload = JsonSerializer.Deserialize<ParticipantUpdatePayload>(packet.PayloadJson);
                if (updatePayload != null)
                {
                    await UpdateParticipantIdentityAsync(
                        ctx.ParticipantId,
                        updatePayload.PersonaName,
                        updatePayload.DisplayName,
                        updatePayload.PersonaId,
                        updatePayload.PersonaSummary,
                        updatePayload.CharacterId,
                        updatePayload.IsReady,
                        updatePayload.RoleTag,
                        ct);
                }
                break;

            case MultiplayerPacketType.PlayerActionSubmit:
                var actionPayload = JsonSerializer.Deserialize<PlayerActionPayload>(packet.PayloadJson);
                if (actionPayload == null) return;

                // Deduplicate message/action IDs
                if (!_processedActionIds.TryAdd(actionPayload.ActionId, DateTimeOffset.UtcNow))
                    return;

                await ProcessParticipantActionInternalAsync(ctx.ParticipantId, actionPayload, ct);
                break;
        }
    }

    public async Task UpdateParticipantIdentityAsync(
        string participantId,
        string personaName,
        string? displayName = null,
        string? personaId = null,
        string? personaSummary = null,
        string? characterId = null,
        bool? isReady = null,
        string? roleTag = null,
        CancellationToken ct = default)
    {
        var p = ActiveSession.Participants.FirstOrDefault(x => x.ParticipantId == participantId);
        if (p == null) return;

        if (!string.IsNullOrWhiteSpace(personaName))
        {
            p.PersonaName = personaName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            p.DisplayName = displayName.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(personaName))
        {
            p.DisplayName = personaName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(personaId))
        {
            p.PersonaId = personaId;
        }
        if (!string.IsNullOrWhiteSpace(personaSummary))
        {
            p.PersonaSummary = personaSummary;
        }
        if (!string.IsNullOrWhiteSpace(characterId))
        {
            p.CharacterId = characterId;
            if (!ActiveSession.ActiveCharacterIds.Contains(characterId))
            {
                ActiveSession.ActiveCharacterIds.Add(characterId);
            }
        }
        if (isReady.HasValue)
        {
            p.IsReady = isReady.Value;
        }
        if (roleTag != null)
        {
            p.RoleTag = roleTag;
        }

        Log($"Updated participant '{p.PersonaName}' ({p.ParticipantId}) [Ready: {p.IsReady}].");
        await BroadcastParticipantListAsync(ct);
        await BroadcastLobbyStateAsync(gameStarted: false, ct);
        OnSessionStateChanged?.Invoke(ActiveSession);
        _mobileCompanionServer?.BroadcastSessionState(ActiveSession);
    }

    public async Task BroadcastLobbyStateAsync(bool gameStarted = false, CancellationToken ct = default)
    {
        var worldName = ActiveWorld?.Name ?? "Default World";
        var lobby = new LobbyStatePayload
        {
            SessionId = ActiveSession.SessionId,
            Title = ActiveSession.Title,
            GameMode = ActiveSession.GameMode.ToString(),
            WorldName = worldName,
            GameStarted = gameStarted,
            Participants = ActiveSession.Participants
        };
        await BroadcastToAllClientsAsync(new MultiplayerPacket
        {
            Type = MultiplayerPacketType.LobbyStateUpdate,
            SessionId = ActiveSession.SessionId,
            SenderParticipantId = "host-player",
            PayloadJson = JsonSerializer.Serialize(lobby)
        }, ct);
    }

    public async Task LaunchGameSessionAsync(CancellationToken ct = default)
    {
        Log("Host launched the roleplay session for all party members!");
        await BroadcastLobbyStateAsync(gameStarted: true, ct);
    }

    private async Task ProcessParticipantActionInternalAsync(
        string participantId,
        PlayerActionPayload payload,
        CancellationToken ct)
    {
        var validation = _engine.ValidateAndApplyPlayerAction(
            ActiveSession,
            participantId,
            payload.ActionType,
            payload.Content,
            out var createdMsg,
            payload.IsPrivateWhisper,
            payload.TargetParticipantId);

        if (!validation.IsAllowed || createdMsg == null)
        {
            if (participantId != "host-player" && _clients.TryGetValue(participantId, out var senderCtx))
            {
                await SendPacketToClientAsync(senderCtx, new MultiplayerPacket
                {
                    Type = MultiplayerPacketType.SystemNotice,
                    SessionId = ActiveSession.SessionId,
                    PayloadJson = validation.DenialReason ?? "Action not permitted."
                }, ct);
            }
            else
            {
                Log($"Action denied: {validation.DenialReason}");
            }
            return;
        }

        OnMessageAdded?.Invoke(createdMsg);
        _mobileCompanionServer?.BroadcastMessage(createdMsg);
        OnSessionStateChanged?.Invoke(ActiveSession);
        _mobileCompanionServer?.BroadcastSessionState(ActiveSession);

        // Broadcast message (respecting private whispers!)
        var msgPacket = new MultiplayerPacket
        {
            Type = MultiplayerPacketType.MessageBroadcast,
            SessionId = ActiveSession.SessionId,
            SenderParticipantId = participantId,
            PayloadJson = JsonSerializer.Serialize(createdMsg)
        };

        if (createdMsg.IsPrivateWhisper && !string.IsNullOrWhiteSpace(createdMsg.TargetParticipantId))
        {
            // Send only to sender and target
            if (_clients.TryGetValue(participantId, out var sCtx))
                await SendPacketToClientAsync(sCtx, msgPacket, ct);
            if (_clients.TryGetValue(createdMsg.TargetParticipantId, out var tCtx))
                await SendPacketToClientAsync(tCtx, msgPacket, ct);
        }
        else
        {
            await BroadcastToAllClientsAsync(msgPacket, ct);
        }

        // Broadcast updated Turn & World State
        await BroadcastTurnAndWorldStateAsync(ct);

        // Coordinate authoritative AI generation on the Host
        if (payload.RequestAiResponse && (validation.ShouldTriggerAiCharacter || validation.ShouldTriggerNarrator))
        {
            await TriggerAuthoritativeAiGenerationAsync(validation.ShouldTriggerNarrator, ct);
        }
    }

    public async Task TriggerAuthoritativeAiGenerationAsync(bool isNarrator, CancellationToken ct = default)
    {
        if (!await _aiGenerationLock.WaitAsync(0, ct))
        {
            // Another AI response is already streaming; prevent conflicting narrative generation
            return;
        }

        try
        {
            var primaryChar = ActiveCharacters.FirstOrDefault(c =>
                !ActiveSession.Participants.Any(p => !p.IsAiCharacter && (
                    string.Equals(p.PersonaName, c.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.CharacterId, c.Id, StringComparison.OrdinalIgnoreCase))))
                ?? ActiveCharacters.FirstOrDefault();
            if (!isNarrator && primaryChar == null) return;

            var senderName = isNarrator ? "Narrator" : primaryChar!.Name;
            var streamId = Guid.NewGuid().ToString("N");

            // Notify clients AI stream has begun
            await BroadcastToAllClientsAsync(new MultiplayerPacket
            {
                Type = MultiplayerPacketType.AiStreamStart,
                SessionId = ActiveSession.SessionId,
                PayloadJson = JsonSerializer.Serialize(new AiStreamPayload
                {
                    StreamId = streamId,
                    SenderName = senderName,
                    IsNarrator = isNarrator
                })
            }, ct);

            var fullText = new StringBuilder();
            await foreach (var delta in _aiOrchestrator.StreamSessionResponseAsync(
                ActiveSession,
                primaryChar,
                HostPersona,
                ActiveWorld,
                ActiveModelPreset,
                isNarrator,
                ct))
            {
                fullText.Append(delta);
                OnAiStreamDelta?.Invoke(streamId, delta);
                _mobileCompanionServer?.BroadcastAiDelta(streamId, delta);

                await BroadcastToAllClientsAsync(new MultiplayerPacket
                {
                    Type = MultiplayerPacketType.AiStreamChunk,
                    SessionId = ActiveSession.SessionId,
                    PayloadJson = JsonSerializer.Serialize(new AiStreamPayload
                    {
                        StreamId = streamId,
                        SenderName = senderName,
                        DeltaOrFullContent = delta,
                        IsNarrator = isNarrator
                    })
                }, ct);
            }

            var finalContent = fullText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(finalContent))
            {
                finalContent = $"*{senderName} pauses thoughtfully.*";
            }

            var aiMsg = _engine.AppendAiMessage(
                ActiveSession,
                primaryChar?.Id ?? "narrator",
                senderName,
                finalContent,
                isNarrator);

            OnMessageAdded?.Invoke(aiMsg);
            _mobileCompanionServer?.BroadcastMessage(aiMsg);
            OnSessionStateChanged?.Invoke(ActiveSession);
            _mobileCompanionServer?.BroadcastSessionState(ActiveSession);

            await BroadcastToAllClientsAsync(new MultiplayerPacket
            {
                Type = MultiplayerPacketType.AiStreamEnd,
                SessionId = ActiveSession.SessionId,
                PayloadJson = JsonSerializer.Serialize(aiMsg)
            }, ct);

            await BroadcastTurnAndWorldStateAsync(ct);
        }
        finally
        {
            _aiGenerationLock.Release();
        }
    }

    private void StartLanDiscoveryResponder(CancellationToken ct)
    {
        try
        {
            _udpDiscoveryListener = new UdpClient(DefaultDiscoveryUdpPort);
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && _udpDiscoveryListener != null)
                {
                    try
                    {
                        var recv = await _udpDiscoveryListener.ReceiveAsync(ct);
                        var msg = Encoding.UTF8.GetString(recv.Buffer);
                        if (msg == "AETHERIA_DISCOVER_V1")
                        {
                            var info = new DiscoveredLanSession(
                                ActiveSession.SessionId,
                                ActiveSession.Title,
                                HostPersona.Name,
                                ActiveSession.GameMode.ToString(),
                                string.Empty,
                                ListeningPort,
                                ActiveSession.Participants.Count(p => !p.IsAiCharacter && p.IsConnected),
                                ActiveSession.MaxPlayers);

                            var respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info));
                            await _udpDiscoveryListener.SendAsync(respBytes, recv.RemoteEndPoint, ct);
                        }
                    }
                    catch
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            Log($"UDP LAN discovery responder note: {ex.Message}");
        }
    }

    private RoleplaySession CreateParticipantScopedSnapshot(string participantId)
    {
        var json = JsonSerializer.Serialize(ActiveSession);
        var copy = JsonSerializer.Deserialize<RoleplaySession>(json)!;
        copy.Messages = copy.Messages
            .Where(m => !m.IsPrivateWhisper || m.SenderId == participantId || m.TargetParticipantId == participantId)
            .ToList();
        return copy;
    }

    public async Task BroadcastParticipantListAsync(CancellationToken ct = default)
    {
        var pkt = new MultiplayerPacket
        {
            Type = MultiplayerPacketType.ParticipantListUpdate,
            SessionId = ActiveSession.SessionId,
            PayloadJson = JsonSerializer.Serialize(ActiveSession.Participants)
        };
        await BroadcastToAllClientsAsync(pkt, ct);
    }

    private async Task BroadcastTurnAndWorldStateAsync(CancellationToken ct)
    {
        var stateObj = new
        {
            ActiveSession.CurrentTurnIndex,
            ActiveSession.CurrentTurnPhase,
            ActiveSession.TurnNumber,
            ActiveSession.CurrentLocation,
            ActiveSession.ActiveWorldState
        };
        var pkt = new MultiplayerPacket
        {
            Type = MultiplayerPacketType.WorldStateUpdate,
            SessionId = ActiveSession.SessionId,
            PayloadJson = JsonSerializer.Serialize(stateObj)
        };
        await BroadcastToAllClientsAsync(pkt, ct);
    }

    public async Task BroadcastToAllClientsAsync(MultiplayerPacket packet, CancellationToken ct)
    {
        foreach (var kv in _clients)
        {
            await SendPacketToClientAsync(kv.Value, packet, ct);
        }
    }

    private static async Task SendPacketToClientAsync(ConnectedClientContext ctx, MultiplayerPacket packet, CancellationToken ct)
    {
        try
        {
            await PacketFrameIO.WritePacketAsync(ctx.Stream, packet, ctx.WriteLock, ct);
        }
        catch
        {
            // Disconnected client handled in read loop
        }
    }

    private static bool CheckRateLimit(ConnectedClientContext ctx)
    {
        var now = DateTimeOffset.UtcNow;
        lock (ctx.RecentMessageTimestamps)
        {
            while (ctx.RecentMessageTimestamps.Count > 0 && (now - ctx.RecentMessageTimestamps.Peek()).TotalSeconds > 10)
            {
                ctx.RecentMessageTimestamps.Dequeue();
            }
            if (ctx.RecentMessageTimestamps.Count >= 25) return false;
            ctx.RecentMessageTimestamps.Enqueue(now);
            return true;
        }
    }

    public static string GenerateSecureInviteToken()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes); // 12-character hex token
    }

    private void Log(string msg) => OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public async ValueTask DisposeAsync() => await StopAsync();
}

public sealed class MultiplayerClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;

    public string? AssignedParticipantId { get; private set; }
    public RoleplaySession? SyncedSession { get; private set; }
    public bool IsConnected => _tcpClient?.Connected == true;

    public event Action<string>? OnLogMessage;
    public event Action<RoleplaySession>? OnSessionSynced;
    public event Action<SessionMessage>? OnMessageReceived;
    public event Action<AiStreamPayload>? OnAiStreamChunkReceived;
    public event Action<Character>? OnCharacterSynced;
    public event Action<string>? OnSystemNoticeReceived;
    public event Action<LobbyStatePayload>? OnLobbyStateReceived;
    public event Action? OnGameLaunchReceived;
    public event Action? OnDisconnected;

    public async Task<(bool Success, string Message)> ConnectAsync(
        string hostAddress,
        int port,
        string inviteToken,
        Persona playerPersona,
        string? reconnectParticipantId = null,
        CancellationToken ct = default,
        Character? customCharacter = null)
    {
        await DisconnectAsync();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            _tcpClient = new TcpClient { NoDelay = true };
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, connectTimeout.Token);

            await _tcpClient.ConnectAsync(hostAddress.Trim(), port, linked.Token);
            _stream = _tcpClient.GetStream();

            var reqPayload = new HandshakeRequestPayload
            {
                InviteToken = inviteToken.Trim(),
                DisplayName = playerPersona.Name,
                PersonaId = playerPersona.Id,
                PersonaName = playerPersona.Name,
                PersonaSummary = $"{playerPersona.Description} Appearance: {playerPersona.Appearance} Personality: {playerPersona.Personality}".Trim(),
                FullPersona = playerPersona,
                CustomCharacter = customCharacter,
                ReconnectParticipantId = reconnectParticipantId
            };

            await PacketFrameIO.WritePacketAsync(_stream, new MultiplayerPacket
            {
                Type = MultiplayerPacketType.HandshakeRequest,
                PayloadJson = JsonSerializer.Serialize(reqPayload)
            }, _writeLock, linked.Token);

            var respPacket = await PacketFrameIO.ReadPacketAsync(_stream, linked.Token);
            if (respPacket == null)
                return (false, "Host closed connection during handshake.");

            var resp = JsonSerializer.Deserialize<HandshakeResponsePayload>(respPacket.PayloadJson);
            if (respPacket.Type == MultiplayerPacketType.HandshakeRejected || resp == null || !resp.Accepted)
            {
                var reason = resp?.Reason ?? "Handshake rejected by host.";
                await DisconnectAsync();
                return (false, reason);
            }

            AssignedParticipantId = resp.AssignedParticipantId;
            SyncedSession = resp.InitialSessionSnapshot;

            if (resp.HostWorld != null)
            {
                Services.PlatformContext.Current?.Repository.SaveWorld(resp.HostWorld);
            }
            if (resp.HostCharacters != null)
            {
                foreach (var hc in resp.HostCharacters)
                {
                    Services.PlatformContext.Current?.Repository.SaveCharacter(hc);
                }
            }

            if (SyncedSession != null)
            {
                OnSessionSynced?.Invoke(SyncedSession);
            }

            Log($"Connected to host {hostAddress}:{port} as {playerPersona.Name} ({AssignedParticipantId}). Synced {resp.HostCharacters?.Count ?? 0} character(s).");
            _ = ReceiveLoopAsync(_cts.Token);
            return (true, $"Connected to '{SyncedSession?.Title ?? "Session"}' (Characters & World synced)!");
        }
        catch (Exception ex)
        {
            await DisconnectAsync();
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task SyncCharacterToHostAsync(Character character, CancellationToken ct = default)
    {
        if (_stream == null || !IsConnected)
            throw new InvalidOperationException("Not connected to a multiplayer host.");

        await PacketFrameIO.WritePacketAsync(_stream, new MultiplayerPacket
        {
            Type = MultiplayerPacketType.CharacterSync,
            SessionId = SyncedSession?.SessionId ?? string.Empty,
            SenderParticipantId = AssignedParticipantId ?? string.Empty,
            PayloadJson = JsonSerializer.Serialize(character)
        }, _writeLock, ct);
        Log($"Synced custom character '{character.Name}' to host and session.");
    }

    public async Task UpdateIdentityAsync(
        string personaName,
        string? personaId = null,
        string? personaSummary = null,
        string? characterId = null,
        bool? isReady = null,
        string? roleTag = null,
        CancellationToken ct = default)
    {
        if (_stream == null || !IsConnected) return;

        var payload = new ParticipantUpdatePayload
        {
            ParticipantId = AssignedParticipantId ?? string.Empty,
            DisplayName = personaName,
            PersonaName = personaName,
            PersonaId = personaId ?? string.Empty,
            PersonaSummary = personaSummary ?? string.Empty,
            CharacterId = characterId,
            IsReady = isReady ?? false,
            RoleTag = roleTag
        };

        await PacketFrameIO.WritePacketAsync(_stream, new MultiplayerPacket
        {
            Type = MultiplayerPacketType.ParticipantUpdate,
            SessionId = SyncedSession?.SessionId ?? string.Empty,
            SenderParticipantId = AssignedParticipantId ?? string.Empty,
            PayloadJson = JsonSerializer.Serialize(payload)
        }, _writeLock, ct);
        Log($"Sent identity update as '{personaName}' [Ready: {isReady}].");
    }

    public async Task SendActionAsync(
        RoleplayActionType actionType,
        string content,
        bool isPrivateWhisper = false,
        string? targetParticipantId = null,
        CancellationToken ct = default)
    {
        if (_stream == null || !IsConnected)
            throw new InvalidOperationException("Not connected to a multiplayer host.");

        var payload = new PlayerActionPayload
        {
            ActionType = actionType,
            Content = content,
            IsPrivateWhisper = isPrivateWhisper,
            TargetParticipantId = targetParticipantId,
            RequestAiResponse = true
        };

        await PacketFrameIO.WritePacketAsync(_stream, new MultiplayerPacket
        {
            Type = MultiplayerPacketType.PlayerActionSubmit,
            SessionId = SyncedSession?.SessionId ?? string.Empty,
            SenderParticipantId = AssignedParticipantId ?? string.Empty,
            PayloadJson = JsonSerializer.Serialize(payload)
        }, _writeLock, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var packet = await PacketFrameIO.ReadPacketAsync(_stream, ct);
                if (packet == null) break;

                switch (packet.Type)
                {
                    case MultiplayerPacketType.MessageBroadcast:
                    case MultiplayerPacketType.AiStreamEnd:
                        var msg = JsonSerializer.Deserialize<SessionMessage>(packet.PayloadJson);
                        if (msg != null && SyncedSession != null)
                        {
                            if (SyncedSession.Messages.All(m => m.MessageId != msg.MessageId))
                            {
                                SyncedSession.Messages.Add(msg);
                                OnMessageReceived?.Invoke(msg);
                            }
                        }
                        break;

                    case MultiplayerPacketType.AiStreamChunk:
                        var chunk = JsonSerializer.Deserialize<AiStreamPayload>(packet.PayloadJson);
                        if (chunk != null) OnAiStreamChunkReceived?.Invoke(chunk);
                        break;

                    case MultiplayerPacketType.CharacterSync:
                        var syncedChar = JsonSerializer.Deserialize<Character>(packet.PayloadJson);
                        if (syncedChar != null)
                        {
                            Services.PlatformContext.Current?.Repository.SaveCharacter(syncedChar);
                            if (SyncedSession != null && !SyncedSession.ActiveCharacterIds.Contains(syncedChar.Id))
                            {
                                SyncedSession.ActiveCharacterIds.Add(syncedChar.Id);
                            }
                            Log($"Received synced character '{syncedChar.Name}' from session.");
                            OnCharacterSynced?.Invoke(syncedChar);
                        }
                        break;

                    case MultiplayerPacketType.ParticipantListUpdate:
                        var parts = JsonSerializer.Deserialize<List<SessionParticipant>>(packet.PayloadJson);
                        if (parts != null && SyncedSession != null)
                        {
                            SyncedSession.Participants = parts;
                            OnSessionSynced?.Invoke(SyncedSession);
                        }
                        break;

                    case MultiplayerPacketType.WorldStateUpdate:
                        if (SyncedSession != null)
                        {
                            using var doc = JsonDocument.Parse(packet.PayloadJson);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("CurrentTurnIndex", out var cti))
                                SyncedSession.CurrentTurnIndex = cti.GetInt32();
                            if (root.TryGetProperty("TurnNumber", out var tn))
                                SyncedSession.TurnNumber = tn.GetInt32();
                            if (root.TryGetProperty("CurrentLocation", out var loc))
                                SyncedSession.CurrentLocation = loc.GetString() ?? SyncedSession.CurrentLocation;
                            OnSessionSynced?.Invoke(SyncedSession);
                        }
                        break;

                    case MultiplayerPacketType.LobbyStateUpdate:
                        var lobby = JsonSerializer.Deserialize<LobbyStatePayload>(packet.PayloadJson);
                        if (lobby != null)
                        {
                            if (SyncedSession != null)
                            {
                                SyncedSession.Participants = lobby.Participants;
                                SyncedSession.Title = lobby.Title;
                            }
                            OnLobbyStateReceived?.Invoke(lobby);
                            if (lobby.GameStarted)
                            {
                                OnGameLaunchReceived?.Invoke();
                            }
                        }
                        break;

                    case MultiplayerPacketType.SystemNotice:
                        OnSystemNoticeReceived?.Invoke(packet.PayloadJson);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Log($"Disconnected from host: {ex.Message}");
        }
        finally
        {
            OnDisconnected?.Invoke();
        }
    }

    public static async Task<List<DiscoveredLanSession>> DiscoverLanSessionsAsync(int timeoutMs = 1200)
    {
        var results = new List<DiscoveredLanSession>();
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            var reqBytes = Encoding.UTF8.GetBytes("AETHERIA_DISCOVER_V1");
            await udp.SendAsync(reqBytes, new IPEndPoint(IPAddress.Broadcast, MultiplayerHostServer.DefaultDiscoveryUdpPort));

            using var cts = new CancellationTokenSource(timeoutMs);
            while (!cts.IsCancellationRequested)
            {
                var recv = await udp.ReceiveAsync(cts.Token);
                var json = Encoding.UTF8.GetString(recv.Buffer);
                var info = JsonSerializer.Deserialize<DiscoveredLanSession>(json);
                if (info != null)
                {
                    var withIp = info with { HostAddress = recv.RemoteEndPoint.Address.ToString() };
                    if (results.All(r => r.SessionId != withIp.SessionId))
                        results.Add(withIp);
                }
            }
        }
        catch
        {
            // Discovery window elapsed
        }
        return results;
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        try { _tcpClient?.Close(); } catch { }
        _tcpClient = null;
        _stream = null;
        await Task.CompletedTask;
    }

    private void Log(string msg) => OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
