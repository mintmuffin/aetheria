using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Aetheria.Core.Domain;

public enum RoleplayGameMode
{
    Freeform,
    TurnBased,
    RealTime,
    CooperativePvE,
    TwoPersonPrivate,
    Sandbox,
    Custom
}

public enum TurnPhase
{
    PlayerAction,
    AiResponse,
    NarratorPhase
}

public enum RoleplayActionType
{
    Speak,
    Move,
    Interact,
    Inspect,
    UseItem,
    Narrate,
    Custom
}

public sealed class InventoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string Category { get; set; } = "General";
}

public sealed class Character
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int SchemaRevision { get; set; } = 1;
    public string Version { get; set; } = "1.0.0";
    public string Name { get; set; } = "Unnamed Character";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Appearance { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string SpeechStyle { get; set; } = string.Empty;
    public string BehavioralTraits { get; set; } = string.Empty;
    public string Motivations { get; set; } = string.Empty;
    public string Goals { get; set; } = string.Empty;
    public string Relationships { get; set; } = string.Empty;
    public string ScenarioInstructions { get; set; } = string.Empty;
    public string Boundaries { get; set; } = string.Empty;
    public string FirstMessage { get; set; } = "Hello, {{user}}.";
    public List<string> AlternateGreetings { get; set; } = new();
    public string ExampleDialogue { get; set; } = string.Empty;
    public string PortraitPath { get; set; } = string.Empty;
    public string PortraitBase64 { get; set; } = string.Empty;
    public List<string> GalleryPaths { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Category { get; set; } = "General";
    public string MemoryContinuitySettings { get; set; } = "Standard";
    public string StartingLocation { get; set; } = string.Empty;
    public List<InventoryItem> Inventory { get; set; } = new();
    public List<string> StatusEffects { get; set; } = new();
    public Dictionary<string, string> WorldStateAttributes { get; set; } = new();
    public string? PreferredModelPresetId { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Character CloneAsDuplicate()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Character>(json)!;
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = $"{this.Name} (Copy)";
        copy.CreatedAt = DateTimeOffset.UtcNow;
        copy.UpdatedAt = DateTimeOffset.UtcNow;
        return copy;
    }
}

public sealed class Persona
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Traveler";
    public string Description { get; set; } = string.Empty;
    public string Appearance { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public Dictionary<string, string> ScenarioAttributes { get; set; } = new();
    public string AvatarPath { get; set; } = string.Empty;
    public string AvatarBase64 { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LoreEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public List<string> Keys { get; set; } = new();
    public string Content { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsConstant { get; set; }
    public int PriorityOrder { get; set; } = 100;

    public bool MatchesText(string text)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(Content)) return false;
        if (IsConstant) return true;
        if (string.IsNullOrWhiteSpace(text) || Keys.Count == 0) return false;

        foreach (var k in Keys)
        {
            var trimmed = k.Trim();
            if (!string.IsNullOrEmpty(trimmed) && text.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class WorldLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> ConnectedLocationNames { get; set; } = new();
    public List<string> PointsOfInterest { get; set; } = new();
}

public sealed class WorldFaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string AlignmentOrDisposition { get; set; } = "Neutral";
    public string Description { get; set; } = string.Empty;
    public string Relationships { get; set; } = string.Empty;
}

public sealed class WorldQuest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // Active, Completed, Failed
    public List<string> Objectives { get; set; } = new();
}

public sealed class WorldStateChange
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Key { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class World
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Version { get; set; } = "1.0.0";
    public string Name { get; set; } = "Untitled World";
    public string Setting { get; set; } = string.Empty;
    public string Genre { get; set; } = "Fantasy";
    public string Tone { get; set; } = "Immersive & Atmospheric";
    public string NarrativeRules { get; set; } = string.Empty;
    public string WorldHistory { get; set; } = string.Empty;
    public List<LoreEntry> LoreEntries { get; set; } = new();
    public List<WorldLocation> Locations { get; set; } = new();
    public List<WorldFaction> Factions { get; set; } = new();
    public List<string> CharacterRosterIds { get; set; } = new();
    public List<InventoryItem> ItemsAndResources { get; set; } = new();
    public List<WorldQuest> QuestsAndObjectives { get; set; } = new();
    public Dictionary<string, string> WorldStateVariables { get; set; } = new();
    public string CustomRulesAndMechanics { get; set; } = string.Empty;
    public string ScenarioStartingConditions { get; set; } = string.Empty;
    public List<WorldStateChange> PersistentStateChanges { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ModelPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Model Preset";
    public string ProviderType { get; set; } = "Ollama"; // Ollama, OpenAI, OpenRouter, Gemini, Anthropic, DeepSeek, Groq, KoboldCpp, LMStudio, Custom
    public string EndpointUrl { get; set; } = "http://127.0.0.1:11434";
    public string ModelId { get; set; } = "llama3.1:8b";
    public double Temperature { get; set; } = 0.82;
    public double TopP { get; set; } = 0.92;
    public int TopK { get; set; } = 40;
    public double MinP { get; set; } = 0.05;
    public double RepetitionPenalty { get; set; } = 1.10;
    public double FrequencyPenalty { get; set; } = 0.05;
    public double PresencePenalty { get; set; } = 0.05;
    public int MaxOutputTokens { get; set; } = 700;
    public int ContextLength { get; set; } = 8192;
    public bool StreamResponses { get; set; } = true;
    public string SystemInstructions { get; set; } = string.Empty;
    public List<string> StopSequences { get; set; } = new();
    public Dictionary<string, string> ProviderSpecificOptions { get; set; } = new();
    public bool IsDefault { get; set; }
}

public sealed class SessionParticipant
{
    public string ParticipantId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Player";
    public string PersonaId { get; set; } = string.Empty;
    public string PersonaName { get; set; } = "Traveler";
    public string PersonaSummary { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsAiCharacter { get; set; }
    public string? CharacterId { get; set; }
    public bool IsConnected { get; set; } = true;
    public bool CanTriggerAi { get; set; } = true;
    public bool IsReady { get; set; } = false;
    public string? RoleTag { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SessionMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    public long SequenceNumber { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Role { get; set; } = "user"; // user, assistant, narrator, system
    public RoleplayActionType ActionType { get; set; } = RoleplayActionType.Speak;
    public string Content { get; set; } = string.Empty;
    public List<string> Alternatives { get; set; } = new();
    public int ActiveAlternativeIndex { get; set; }
    public bool IsPrivateWhisper { get; set; }
    public string? TargetParticipantId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RoleplaySession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Roleplay Session";
    public RoleplayGameMode GameMode { get; set; } = RoleplayGameMode.Freeform;
    public string? WorldId { get; set; }
    public List<string> ActiveCharacterIds { get; set; } = new();
    public string ActivePersonaId { get; set; } = string.Empty;
    public string? ActiveModelPresetId { get; set; }
    public List<SessionParticipant> Participants { get; set; } = new();
    public List<string> TurnOrderParticipantIds { get; set; } = new();
    public int CurrentTurnIndex { get; set; }
    public TurnPhase CurrentTurnPhase { get; set; } = TurnPhase.PlayerAction;
    public int TurnNumber { get; set; } = 1;
    public List<SessionMessage> Messages { get; set; } = new();
    public Dictionary<string, string> ActiveWorldState { get; set; } = new();
    public List<InventoryItem> SharedInventory { get; set; } = new();
    public string CurrentLocation { get; set; } = "Starting Area";
    public bool AllowAiInTwoPersonMode { get; set; }
    public bool EnableNarratorPhase { get; set; }
    public int MaxPlayers { get; set; } = 6;
    public bool IsPrivateSession { get; set; } = true;
    public int MaxContextMessages { get; set; } = 50;
    public bool EnableAutoSummarization { get; set; } = true;
    public string RunningSummary { get; set; } = string.Empty;
    public string AuthorsNote { get; set; } = string.Empty;
    public int AuthorsNoteDepth { get; set; } = 2;
    public string CustomModeRules { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
