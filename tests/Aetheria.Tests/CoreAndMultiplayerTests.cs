using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Aetheria.Core.AI;
using Aetheria.Core.Domain;
using Aetheria.Core.Engine;
using Aetheria.Core.Formats;
using Aetheria.Core.Multiplayer;
using Aetheria.Core.Persistence;
using Xunit;

namespace Aetheria.Tests;

public sealed class CoreAndMultiplayerTests : IDisposable
{
    private readonly string _tempDir;

    public CoreAndMultiplayerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AetheriaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void CustomFormats_ModelPreset_StripsSecretsAndRoundTrips()
    {
        var preset = new ModelPreset
        {
            Name = "Test Preset",
            ProviderType = "OpenAI",
            EndpointUrl = "https://api.openai.com/v1",
            ModelId = "gpt-4o",
            Temperature = 0.75,
            ProviderSpecificOptions = new Dictionary<string, string>
            {
                ["safeOption"] = "enabled",
                ["apiKey"] = "sk-SUPER-SECRET-DO-NOT-EXPORT",
                ["authToken"] = "bearer-secret"
            }
        };

        var exportedJson = RoleplayFormatSerializer.ExportModelPreset(preset);
        Assert.DoesNotContain("sk-SUPER-SECRET-DO-NOT-EXPORT", exportedJson);
        Assert.DoesNotContain("bearer-secret", exportedJson);
        Assert.Contains("AETHERIA_RP_FORMAT", exportedJson);
        Assert.Contains("\"resourceType\": \"rpmodel\"", exportedJson);

        var imported = RoleplayFormatSerializer.ImportModelPreset(exportedJson);
        Assert.True(imported.IsValid);
        Assert.NotNull(imported.Data);
        Assert.Equal("Test Preset", imported.Data!.Name);
        Assert.Equal("gpt-4o", imported.Data.ModelId);
        Assert.True(imported.Data.ProviderSpecificOptions.ContainsKey("safeOption"));
        Assert.False(imported.Data.ProviderSpecificOptions.ContainsKey("apiKey"));
    }

    [Fact]
    public void CustomFormats_CharacterPersonaWorldSession_RoundTripAndValidate()
    {
        var ch = new Character
        {
            Name = "Seraphina",
            Title = "Starlight Warden",
            Description = "Guardian of the Astral Spire.",
            FirstMessage = "Welcome, {{user}}."
        };
        var charJson = RoleplayFormatSerializer.ExportCharacter(ch);
        var charRes = RoleplayFormatSerializer.ImportCharacter(charJson);
        Assert.True(charRes.IsValid);
        Assert.Equal("Seraphina", charRes.Data!.Name);

        // Wrong resource type validation
        var wrongType = RoleplayFormatSerializer.ImportWorld(charJson);
        Assert.False(wrongType.IsValid);
        Assert.Contains(wrongType.Errors, e => e.Contains("Resource type mismatch"));

        // Schema migration from 0.9 -> 1.0
        var v09Json = charJson.Replace("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"0.9\"");
        var migrated = RoleplayFormatSerializer.ImportCharacter(v09Json);
        Assert.True(migrated.IsValid);
        Assert.Equal("0.9", migrated.MigratedFromVersion);
    }

    [Fact]
    public void CustomFormats_RpackBundle_BlocksPathTraversalAndExecutableScripts()
    {
        var validBundlePath = Path.Combine(_tempDir, "valid.rpack");
        var bundle = new ResourceBundleManifest
        {
            Title = "Test Adventure Pack",
            Characters = new List<Character> { new() { Name = "Lyra" } },
            Worlds = new List<World> { new() { Name = "Oakhaven" } }
        };

        RoleplayFormatSerializer.ExportBundleToFile(validBundlePath, bundle);
        var imported = RoleplayFormatSerializer.ImportBundleFromFile(validBundlePath);
        Assert.True(imported.IsValid);
        Assert.Single(imported.Data!.Characters);
        Assert.Single(imported.Data.Worlds);

        // Create malicious archive with path traversal
        var maliciousTraversalPath = Path.Combine(_tempDir, "traversal.rpack");
        using (var fs = new FileStream(maliciousTraversalPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../../evil.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("malicious");
        }

        var traversalResult = RoleplayFormatSerializer.ImportBundleFromFile(maliciousTraversalPath);
        Assert.False(traversalResult.IsValid);
        Assert.Contains(traversalResult.Errors, e => e.Contains("Security Violation"));

        // Create malicious archive with executable script
        var maliciousScriptPath = Path.Combine(_tempDir, "script.rpack");
        using (var fs = new FileStream(maliciousScriptPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("payload.ps1");
            using var w = new StreamWriter(entry.Open());
            w.Write("Start-Process calc.exe");
        }

        var scriptResult = RoleplayFormatSerializer.ImportBundleFromFile(maliciousScriptPath);
        Assert.False(scriptResult.IsValid);
        Assert.Contains(scriptResult.Errors, e => e.Contains("prohibited executable/script"));
    }

    [Fact]
    public void RoleplayEngine_TurnBasedAndTwoPersonModes_EnforceRulesDistinctly()
    {
        var engine = new RoleplayEngine();
        var hostPersona = new Persona { Id = "p1", Name = "Alice" };
        var world = new World { Name = "TestWorld" };
        var ch = new Character { Id = "c1", Name = "Orion", FirstMessage = "Greetings, {{user}}." };

        // 1. Two-Person Private Mode without AI
        var privateSession = engine.InitializeSession("Private Duet", RoleplayGameMode.TwoPersonPrivate, hostPersona, world, new[] { ch });
        Assert.Equal(2, privateSession.MaxPlayers);
        Assert.Empty(privateSession.Messages); // No unsolicited AI greeting when AllowAiInTwoPersonMode == false

        // 2. Turn-Based Mode
        var turnSession = engine.InitializeSession("Tactical Quest", RoleplayGameMode.TurnBased, hostPersona, world, new[] { ch });
        turnSession.Participants.Add(new SessionParticipant
        {
            ParticipantId = "player-2",
            DisplayName = "Bob",
            PersonaName = "Bob",
            IsHost = false,
            IsAiCharacter = false
        });
        turnSession.TurnOrderParticipantIds = new List<string> { "host-player", "player-2" };
        turnSession.CurrentTurnIndex = 0;

        // Player 2 tries to act on Host's turn -> Denied
        var p2Attempt = engine.ValidateAndApplyPlayerAction(turnSession, "player-2", RoleplayActionType.Speak, "Acting out of turn!", out _);
        Assert.False(p2Attempt.IsAllowed);
        Assert.Contains("Turn-Based Rule", p2Attempt.DenialReason);

        // Host acts on their turn -> Allowed & advances turn to Player 2
        var hostAttempt = engine.ValidateAndApplyPlayerAction(turnSession, "host-player", RoleplayActionType.Move, "Ancient Courtyard", out var moveMsg);
        Assert.True(hostAttempt.IsAllowed);
        Assert.NotNull(moveMsg);
        Assert.Equal("Ancient Courtyard", turnSession.CurrentLocation);
        Assert.Equal("player-2", engine.GetCurrentTurnParticipantId(turnSession));

        // Now Player 2 can act!
        var p2ValidAttempt = engine.ValidateAndApplyPlayerAction(turnSession, "player-2", RoleplayActionType.Speak, "\"I follow closely behind.\"", out _);
        Assert.True(p2ValidAttempt.IsAllowed);
    }

    [Fact]
    public async Task Multiplayer_HostAndClient_HandshakeAuthAndSynchronizeState()
    {
        var engine = new RoleplayEngine();
        var vault = new SecretVault(_tempDir);
        var ai = new AiOrchestrator(vault);
        var hostPersona = new Persona { Id = "p-host", Name = "HostCommander" };
        var guestPersona = new Persona { Id = "p-guest", Name = "GuestEnvoy" };
        var session = engine.InitializeSession("Coop Session", RoleplayGameMode.TwoPersonPrivate, hostPersona, null, Array.Empty<Character>());

        int port = GetAvailableTcpPort();
        await using var hostServer = new MultiplayerHostServer(
            engine, ai, session, hostPersona, null, new List<Character>(), new ModelPreset());

        hostServer.Start(port, enableLanDiscovery: false, customInviteToken: "SECRET-TOKEN-123");

        // 1. Client with wrong token should be rejected
        await using var badClient = new MultiplayerClient();
        var badConnect = await badClient.ConnectAsync("127.0.0.1", port, "WRONG-TOKEN", guestPersona);
        Assert.False(badConnect.Success);
        Assert.Contains("Invalid Invite Token", badConnect.Message);

        // 2. Client with valid token should connect and receive initial snapshot
        await using var validClient = new MultiplayerClient();
        var msgTcs = new TaskCompletionSource<SessionMessage>();
        validClient.OnMessageReceived += m => msgTcs.TrySetResult(m);

        var okConnect = await validClient.ConnectAsync("127.0.0.1", port, "SECRET-TOKEN-123", guestPersona);
        Assert.True(okConnect.Success);
        Assert.NotNull(validClient.SyncedSession);
        Assert.Equal("Coop Session", validClient.SyncedSession!.Title);

        // 3. Client submits action -> Host validates and broadcasts back
        await validClient.SendActionAsync(RoleplayActionType.Speak, "\"Hello from GuestEnvoy!\"");
        var completed = await Task.WhenAny(msgTcs.Task, Task.Delay(3000));
        Assert.Same(msgTcs.Task, completed);
        var syncedMsg = await msgTcs.Task;
        Assert.Contains("Hello from GuestEnvoy!", syncedMsg.Content);
        Assert.Contains(hostServer.ActiveSession.Messages, m => m.Content.Contains("Hello from GuestEnvoy!"));
    }

    [Fact]
    public void SqliteRepository_SeedsAndPersistsEntitiesReliably()
    {
        using var repo = new SqliteRepository(_tempDir);
        var chars = repo.GetAllCharacters();
        var worlds = repo.GetAllWorlds();
        var presets = repo.GetAllModelPresets();
        var personas = repo.GetAllPersonas();

        Assert.NotEmpty(chars);
        Assert.NotEmpty(worlds);
        Assert.NotEmpty(presets);
        Assert.NotEmpty(personas);

        var customChar = new Character { Name = "Persistent Hero", Category = "Test" };
        repo.SaveCharacter(customChar);

        var reloaded = repo.GetAllCharacters().FirstOrDefault(c => c.Id == customChar.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Persistent Hero", reloaded!.Name);
    }

    [Fact]
    public async Task Multiplayer_MobileCompanionServer_ServesWebAppAndAcceptsActions()
    {
        var engine = new RoleplayEngine();
        var vault = new SecretVault(_tempDir);
        var ai = new AiOrchestrator(vault);
        var hostPersona = new Persona { Id = "p-host", Name = "HostDenji" };
        var session = engine.InitializeSession("Chainsaw Man Mobile Session", RoleplayGameMode.TwoPersonPrivate, hostPersona, null, Array.Empty<Character>());

        int port = GetAvailableTcpPort();
        await using var hostServer = new MultiplayerHostServer(
            engine, ai, session, hostPersona, null, new List<Character>(), new ModelPreset());

        hostServer.Start(port, enableLanDiscovery: false, customInviteToken: "MOBILE-TOKEN-777");

        int mobilePort = hostServer.MobileCompanionPort;
        using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{mobilePort}") };

        // 1. GET / serves the mobile web app HTML
        var indexResp = await http.GetAsync("/");
        Assert.True(indexResp.IsSuccessStatusCode);
        var html = await indexResp.Content.ReadAsStringAsync();
        Assert.Contains("Aetheria Mobile Companion", html);
        Assert.Contains("Chainsaw Man", session.Title);

        // 2. GET /manifest.json serves the Android PWA manifest
        var manifestResp = await http.GetAsync("/manifest.json");
        Assert.True(manifestResp.IsSuccessStatusCode);
        var manifest = await manifestResp.Content.ReadAsStringAsync();
        Assert.Contains("Aetheria AI Roleplay Mobile", manifest);

        // 3. GET /api/session returns session snapshot
        var sessionResp = await http.GetAsync("/api/session");
        Assert.True(sessionResp.IsSuccessStatusCode);
        var sessionJson = await sessionResp.Content.ReadAsStringAsync();
        Assert.Contains("Chainsaw Man Mobile Session", sessionJson);

        // 4. POST /api/action with invalid token should fail (401)
        var badActionJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            inviteToken = "WRONG-TOKEN",
            content = "Testing from phone",
            actionType = "Speak"
        });
        var badActionResp = await http.PostAsync("/api/action", new System.Net.Http.StringContent(badActionJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, badActionResp.StatusCode);

        // 5. POST /api/action with valid token succeeds and updates session
        var okActionJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            inviteToken = "MOBILE-TOKEN-777",
            content = "\"Hey Denji, I joined from my Android phone!\"",
            actionType = "Speak",
            playerName = "MobilePlayer",
            participantId = "mobile-test-user"
        });
        var okActionResp = await http.PostAsync("/api/action", new System.Net.Http.StringContent(okActionJson, Encoding.UTF8, "application/json"));
        Assert.True(okActionResp.IsSuccessStatusCode);
        Assert.Contains(hostServer.ActiveSession.Messages, m => m.Content.Contains("joined from my Android phone"));

        // 6. POST /api/sync-character syncs a custom mobile character
        var syncCharJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            inviteToken = "MOBILE-TOKEN-777",
            character = new Character
            {
                Id = "char-mobile-hero",
                Name = "Mobile Devil Hunter",
                Title = "Special Agent"
            }
        });
        var syncResp = await http.PostAsync("/api/sync-character", new System.Net.Http.StringContent(syncCharJson, Encoding.UTF8, "application/json"));
        Assert.True(syncResp.IsSuccessStatusCode);
        Assert.Contains(hostServer.ActiveCharacters, c => c.Name == "Mobile Devil Hunter");
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
