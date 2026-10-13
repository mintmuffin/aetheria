using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Aetheria.Core.Domain;

namespace Aetheria.Core.Engine;

public sealed class ActionValidationResult
{
    public bool IsAllowed { get; init; }
    public string? DenialReason { get; init; }
    public bool ShouldTriggerAiCharacter { get; init; }
    public bool ShouldTriggerNarrator { get; init; }

    public static ActionValidationResult Allow(bool triggerAi = true, bool triggerNarrator = false) =>
        new() { IsAllowed = true, ShouldTriggerAiCharacter = triggerAi, ShouldTriggerNarrator = triggerNarrator };

    public static ActionValidationResult Deny(string reason) =>
        new() { IsAllowed = false, DenialReason = reason };
}

public sealed class RoleplayEngine
{
    private readonly object _stateLock = new();
    private long _sequenceCounter;

    public RoleplaySession InitializeSession(
        string title,
        RoleplayGameMode mode,
        Persona hostPersona,
        World? world,
        IEnumerable<Character> characters,
        ModelPreset? modelPreset = null)
    {
        var charList = characters.ToList();
        var session = new RoleplaySession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Roleplay" : title.Trim(),
            GameMode = mode,
            WorldId = world?.Id,
            ActivePersonaId = hostPersona.Id,
            ActiveModelPresetId = modelPreset?.Id,
            ActiveCharacterIds = charList.Select(c => c.Id).ToList(),
            CurrentLocation = world?.Locations.FirstOrDefault()?.Name
                              ?? charList.FirstOrDefault()?.StartingLocation
                              ?? "Starting Scene",
            MaxPlayers = mode == RoleplayGameMode.TwoPersonPrivate ? 2 : 8,
            AllowAiInTwoPersonMode = false,
            EnableNarratorPhase = mode is RoleplayGameMode.TurnBased or RoleplayGameMode.CooperativePvE
        };

        // Copy initial world state variables if present
        if (world != null)
        {
            foreach (var kv in world.WorldStateVariables)
            {
                session.ActiveWorldState[kv.Key] = kv.Value;
            }
            foreach (var item in world.ItemsAndResources)
            {
                session.SharedInventory.Add(new InventoryItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    Category = item.Category
                });
            }
        }

        if (mode == RoleplayGameMode.CooperativePvE)
        {
            session.ActiveWorldState.TryAdd("PartyStatus", "Healthy");
            session.ActiveWorldState.TryAdd("ThreatLevel", "Low");
            if (world?.QuestsAndObjectives.FirstOrDefault() is { } firstQuest)
            {
                session.ActiveWorldState.TryAdd("ActiveQuest", firstQuest.Title);
            }
        }

        // Add Host participant
        var hostParticipant = new SessionParticipant
        {
            ParticipantId = "host-player",
            DisplayName = hostPersona.Name,
            PersonaId = hostPersona.Id,
            PersonaName = hostPersona.Name,
            PersonaSummary = hostPersona.Description,
            IsHost = true,
            IsAiCharacter = false,
            IsConnected = true,
            CanTriggerAi = true
        };
        session.Participants.Add(hostParticipant);
        session.TurnOrderParticipantIds.Add(hostParticipant.ParticipantId);

        // Add AI characters to participants (unless TwoPersonPrivate with AI disabled)
        if (mode != RoleplayGameMode.TwoPersonPrivate || session.AllowAiInTwoPersonMode)
        {
            foreach (var ch in charList)
            {
                var aiPart = new SessionParticipant
                {
                    ParticipantId = $"ai-{ch.Id}",
                    DisplayName = ch.Name,
                    PersonaName = ch.Name,
                    PersonaSummary = ch.Description,
                    IsHost = false,
                    IsAiCharacter = true,
                    CharacterId = ch.Id,
                    IsConnected = true
                };
                session.Participants.Add(aiPart);
                session.TurnOrderParticipantIds.Add(aiPart.ParticipantId);
            }
        }

        // Seed opening greeting if single-player or AI enabled
        if (mode != RoleplayGameMode.TwoPersonPrivate || session.AllowAiInTwoPersonMode)
        {
            var primaryChar = charList.FirstOrDefault();
            if (primaryChar != null && !string.IsNullOrWhiteSpace(primaryChar.FirstMessage))
            {
                var greeting = ReplaceMacros(primaryChar.FirstMessage, hostPersona.Name, primaryChar.Name);
                var msg = new SessionMessage
                {
                    SequenceNumber = Interlocked.Increment(ref _sequenceCounter),
                    SenderId = $"ai-{primaryChar.Id}",
                    SenderName = primaryChar.Name,
                    Role = "assistant",
                    ActionType = RoleplayActionType.Speak,
                    Content = greeting,
                    Alternatives = new List<string> { greeting },
                    ActiveAlternativeIndex = 0
                };
                session.Messages.Add(msg);
            }
        }

        return session;
    }

    public ActionValidationResult ValidateAndApplyPlayerAction(
        RoleplaySession session,
        string participantId,
        RoleplayActionType actionType,
        string content,
        out SessionMessage? createdMessage,
        bool isPrivateWhisper = false,
        string? targetParticipantId = null)
    {
        createdMessage = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return ActionValidationResult.Deny("Action or message content cannot be empty.");
        }

        lock (_stateLock)
        {
            var participant = session.Participants.FirstOrDefault(p => p.ParticipantId == participantId);
            if (participant == null)
            {
                return ActionValidationResult.Deny("Participant is not registered in this session.");
            }

            // Enforce Turn-Based queue rules (unless private whisper)
            if (session.GameMode == RoleplayGameMode.TurnBased && !isPrivateWhisper)
            {
                var currentTurnHolderId = GetCurrentTurnParticipantId(session);
                if (currentTurnHolderId != null && currentTurnHolderId != participantId && !participant.IsHost)
                {
                    var holderName = session.Participants.FirstOrDefault(p => p.ParticipantId == currentTurnHolderId)?.DisplayName ?? "Another participant";
                    return ActionValidationResult.Deny($"Turn-Based Rule: It is currently {holderName}'s turn (Turn #{session.TurnNumber}).");
                }
            }

            // Apply structured world-state mutations based on actionType
            var formattedContent = FormatActionContent(actionType, content, participant.PersonaName);
            ApplyActionSideEffects(session, actionType, content, participant.PersonaName);

            var seq = session.Messages.Count > 0
                ? Math.Max(session.Messages.Max(m => m.SequenceNumber) + 1, Interlocked.Increment(ref _sequenceCounter))
                : Interlocked.Increment(ref _sequenceCounter);

            createdMessage = new SessionMessage
            {
                SequenceNumber = seq,
                SenderId = participantId,
                SenderName = participant.PersonaName,
                Role = actionType == RoleplayActionType.Narrate ? "narrator" : "user",
                ActionType = actionType,
                Content = formattedContent,
                Alternatives = new List<string> { formattedContent },
                ActiveAlternativeIndex = 0,
                IsPrivateWhisper = isPrivateWhisper,
                TargetParticipantId = targetParticipantId,
                Timestamp = DateTimeOffset.UtcNow
            };

            session.Messages.Add(createdMessage);
            session.UpdatedAt = DateTimeOffset.UtcNow;

            // Advance turn in TurnBased mode
            if (session.GameMode == RoleplayGameMode.TurnBased && !isPrivateWhisper)
            {
                AdvanceTurnInternal(session);
            }

            // Determine whether AI should respond
            bool triggerAi = session.GameMode switch
            {
                // In TwoPersonPrivate (couple / co-op), individual messages do NOT trigger AI.
                // AI generation is only triggered when both participants confirm Ready for scene continuation!
                RoleplayGameMode.TwoPersonPrivate => false,
                RoleplayGameMode.TurnBased => !isPrivateWhisper && IsCurrentTurnAi(session),
                _ => !isPrivateWhisper && session.ActiveCharacterIds.Count > 0 && participant.CanTriggerAi
            };

            bool triggerNarrator = session.GameMode != RoleplayGameMode.TwoPersonPrivate &&
                                  session.EnableNarratorPhase &&
                                  actionType is RoleplayActionType.Move or RoleplayActionType.Inspect or RoleplayActionType.Interact;

            return ActionValidationResult.Allow(triggerAi, triggerNarrator);
        }
    }

    public SessionMessage AppendAiMessage(
        RoleplaySession session,
        string characterId,
        string characterName,
        string content,
        bool isNarrator = false)
    {
        lock (_stateLock)
        {
            var seq = session.Messages.Count > 0
                ? session.Messages.Max(m => m.SequenceNumber) + 1
                : 1;

            var msg = new SessionMessage
            {
                SequenceNumber = seq,
                SenderId = isNarrator ? "narrator" : $"ai-{characterId}",
                SenderName = isNarrator ? "Narrator" : characterName,
                Role = isNarrator ? "narrator" : "assistant",
                ActionType = isNarrator ? RoleplayActionType.Narrate : RoleplayActionType.Speak,
                Content = content,
                Alternatives = new List<string> { content },
                ActiveAlternativeIndex = 0,
                Timestamp = DateTimeOffset.UtcNow
            };

            session.Messages.Add(msg);
            session.UpdatedAt = DateTimeOffset.UtcNow;

            if (session.GameMode == RoleplayGameMode.TurnBased && !isNarrator)
            {
                AdvanceTurnInternal(session);
            }

            return msg;
        }
    }

    public void AdvanceTurn(RoleplaySession session)
    {
        lock (_stateLock)
        {
            AdvanceTurnInternal(session);
        }
    }

    private void AdvanceTurnInternal(RoleplaySession session)
    {
        if (session.TurnOrderParticipantIds.Count == 0) return;

        session.CurrentTurnIndex = (session.CurrentTurnIndex + 1) % session.TurnOrderParticipantIds.Count;
        if (session.CurrentTurnIndex == 0)
        {
            session.TurnNumber++;
        }

        var nextId = session.TurnOrderParticipantIds[session.CurrentTurnIndex];
        var nextParticipant = session.Participants.FirstOrDefault(p => p.ParticipantId == nextId);
        session.CurrentTurnPhase = nextParticipant?.IsAiCharacter == true
            ? TurnPhase.AiResponse
            : TurnPhase.PlayerAction;
    }

    public string? GetCurrentTurnParticipantId(RoleplaySession session)
    {
        if (session.TurnOrderParticipantIds.Count == 0) return null;
        var idx = Math.Clamp(session.CurrentTurnIndex, 0, session.TurnOrderParticipantIds.Count - 1);
        return session.TurnOrderParticipantIds[idx];
    }

    public bool IsCurrentTurnAi(RoleplaySession session)
    {
        var id = GetCurrentTurnParticipantId(session);
        if (id == null) return false;
        return session.Participants.FirstOrDefault(p => p.ParticipantId == id)?.IsAiCharacter == true;
    }

    public void UpdateWorldStateVariable(RoleplaySession session, World? world, string key, string newValue, string reason = "Roleplay progression")
    {
        lock (_stateLock)
        {
            session.ActiveWorldState.TryGetValue(key, out var oldVal);
            session.ActiveWorldState[key] = newValue;
            if (string.Equals(key, "Location", StringComparison.OrdinalIgnoreCase))
            {
                session.CurrentLocation = newValue;
            }

            world?.PersistentStateChanges.Add(new WorldStateChange
            {
                Timestamp = DateTimeOffset.UtcNow,
                Key = key,
                OldValue = oldVal ?? string.Empty,
                NewValue = newValue,
                Reason = reason
            });
        }
    }

    private static string FormatActionContent(RoleplayActionType actionType, string raw, string actorName)
    {
        var trimmed = raw.Trim();
        return actionType switch
        {
            RoleplayActionType.Move => $"*{actorName} moves toward {trimmed}.*",
            RoleplayActionType.Inspect => $"*{actorName} closely inspects {trimmed}.*",
            RoleplayActionType.Interact => $"*{actorName} interacts with {trimmed}.*",
            RoleplayActionType.UseItem => $"*{actorName} uses {trimmed}.*",
            RoleplayActionType.Narrate => $"[Scene Narration] {trimmed}",
            _ => trimmed
        };
    }

    private void ApplyActionSideEffects(RoleplaySession session, RoleplayActionType actionType, string raw, string actorName)
    {
        switch (actionType)
        {
            case RoleplayActionType.Move:
                session.CurrentLocation = raw.Trim();
                session.ActiveWorldState["CurrentLocation"] = session.CurrentLocation;
                break;
            case RoleplayActionType.UseItem:
                var item = session.SharedInventory.FirstOrDefault(i =>
                    raw.Contains(i.Name, StringComparison.OrdinalIgnoreCase));
                if (item != null && item.Quantity > 0)
                {
                    item.Quantity--;
                }
                break;
        }
        session.ActiveWorldState["LastActionBy"] = actorName;
        session.ActiveWorldState["LastActionType"] = actionType.ToString();
    }

    public static string ReplaceMacros(string text, string userName, string charName)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("{{user}}", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{char}}", charName, StringComparison.OrdinalIgnoreCase);
    }
}
