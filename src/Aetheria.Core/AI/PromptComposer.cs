using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aetheria.Core.Domain;
using Aetheria.Core.Engine;

namespace Aetheria.Core.AI;

public sealed record ChatMessageTurn(string Role, string Content);

public static class PromptComposer
{
    public static List<ChatMessageTurn> BuildPrompt(
        RoleplaySession session,
        Character? activeCharacter,
        Persona activePersona,
        World? world,
        ModelPreset preset,
        bool isNarratorGeneration = false,
        bool isRoundResolution = false)
    {
        bool isNarrator = isNarratorGeneration || activeCharacter == null;
        var charName = isNarrator
            ? "Narrator"
            : (activeCharacter?.Name ?? "Companion");
        var userName = activePersona.Name;

        var sys = new StringBuilder();

        // 1. Core Role & Game Mode Directive
        if (isNarrator)
        {
            sys.AppendLine("You are the evocative, atmospheric Game Master and Narrator for this roleplay session.");
            sys.AppendLine("Your role is to collect the players' actions, dialogue, choices, and dice rolls, and craft a rich, immersive continuation.");
            if (session.GameMode == RoleplayGameMode.TwoPersonPrivate)
            {
                sys.AppendLine("This is an intimate Two-Person couple roleplay. Deepen their emotional connection, cozy bedroom atmosphere, sensory details, and romantic chemistry without speaking or acting on behalf of human players.");
            }
            else
            {
                sys.AppendLine("Describe environmental changes, sensory details, consequences of player actions, and world progression vividly without speaking on behalf of human players.");
            }
        }
        else if (activeCharacter != null)
        {
            sys.AppendLine($"You are roleplaying as {activeCharacter.Name}{(string.IsNullOrWhiteSpace(activeCharacter.Title) ? "" : $", {activeCharacter.Title}")}.");
            sys.AppendLine($"Stay strictly in character at all times. Never speak or act on behalf of {userName} or other human players.");
            sys.AppendLine("Enclose physical actions and expressions in *asterisks* and spoken dialogue in \"quotation marks\".");
        }

        // 2. Game Mode Specific Instructions
        sys.AppendLine();
        sys.AppendLine($"[Active Game Mode: {session.GameMode}]");
        switch (session.GameMode)
        {
            case RoleplayGameMode.TurnBased:
                sys.AppendLine($"This is a Turn-Based session (Current Turn #{session.TurnNumber}). React specifically to the latest turn actions and leave clear hooks for the next participant's turn.");
                break;
            case RoleplayGameMode.CooperativePvE:
                sys.AppendLine("This is a Cooperative PvE adventure. Honor world rules, environmental hazards, inventory usage, and active quest objectives.");
                break;
            case RoleplayGameMode.TwoPersonPrivate:
                sys.AppendLine("This is an intimate Two-Person Private roleplay session. Focus on interpersonal nuance and scene continuity without imposing artificial combat or quest mechanics.");
                break;
            case RoleplayGameMode.Sandbox:
                sys.AppendLine("This is an open-ended Sandbox roleplay. Follow player agency freely across the world.");
                break;
            case RoleplayGameMode.Custom:
                if (!string.IsNullOrWhiteSpace(session.CustomModeRules))
                    sys.AppendLine($"Custom Mode Rules: {session.CustomModeRules}");
                break;
        }

        // 3. Model Preset Custom System Instructions
        if (!string.IsNullOrWhiteSpace(preset.SystemInstructions))
        {
            sys.AppendLine();
            sys.AppendLine("[Model Directives]");
            sys.AppendLine(RoleplayEngine.ReplaceMacros(preset.SystemInstructions, userName, charName));
        }

        // 4. Character Profile
        if (activeCharacter != null && !isNarratorGeneration)
        {
            sys.AppendLine();
            sys.AppendLine($"[Character Profile: {activeCharacter.Name}]");
            AppendField(sys, "Description", activeCharacter.Description, userName, charName);
            AppendField(sys, "Appearance", activeCharacter.Appearance, userName, charName);
            AppendField(sys, "Personality", activeCharacter.Personality, userName, charName);
            AppendField(sys, "Background", activeCharacter.Background, userName, charName);
            AppendField(sys, "Speech Style", activeCharacter.SpeechStyle, userName, charName);
            AppendField(sys, "Behavioral Traits", activeCharacter.BehavioralTraits, userName, charName);
            AppendField(sys, "Motivations & Goals", $"{activeCharacter.Motivations} {activeCharacter.Goals}".Trim(), userName, charName);
            AppendField(sys, "Relationships", activeCharacter.Relationships, userName, charName);
            AppendField(sys, "Scenario Instructions", activeCharacter.ScenarioInstructions, userName, charName);
            AppendField(sys, "Boundaries", activeCharacter.Boundaries, userName, charName);
        }

        // 5. World Context, Lorebook & Structured World-State
        if (world != null)
        {
            sys.AppendLine();
            sys.AppendLine($"[World Setting: {world.Name} ({world.Genre} — {world.Tone})]");
            AppendField(sys, "Setting Overview", world.Setting, userName, charName);
            AppendField(sys, "World History & Canon", world.WorldHistory, userName, charName);
            AppendField(sys, "Universe & Narrative Rules", world.NarrativeRules, userName, charName);
            AppendField(sys, "Custom Mechanics", world.CustomRulesAndMechanics, userName, charName);
            AppendField(sys, "Scenario Conditions", world.ScenarioStartingConditions, userName, charName);

            var currentLoc = world.Locations.FirstOrDefault(l =>
                string.Equals(l.Name, session.CurrentLocation, StringComparison.OrdinalIgnoreCase) ||
                session.CurrentLocation.Contains(l.Name, StringComparison.OrdinalIgnoreCase) ||
                l.Name.Contains(session.CurrentLocation, StringComparison.OrdinalIgnoreCase));

            if (currentLoc != null)
            {
                var poi = currentLoc.PointsOfInterest.Count > 0
                    ? $" (Points of Interest: {string.Join(", ", currentLoc.PointsOfInterest)})"
                    : string.Empty;
                sys.AppendLine($"Current Active Location: {currentLoc.Name} ({currentLoc.Region}) — {currentLoc.Description}{poi}");
            }
            else if (!string.IsNullOrWhiteSpace(session.CurrentLocation))
            {
                sys.AppendLine($"Current Active Location: {session.CurrentLocation}");
            }

            if (world.Locations.Count > 0)
            {
                sys.AppendLine("Known World Locations:");
                foreach (var loc in world.Locations)
                {
                    sys.AppendLine($"- {loc.Name} ({loc.Region}): {loc.Description}");
                }
            }

            if (world.Factions.Count > 0)
            {
                sys.AppendLine("World Factions & Organizations:");
                foreach (var fac in world.Factions)
                {
                    sys.AppendLine($"- {fac.Name} [{fac.AlignmentOrDisposition}]: {fac.Description}");
                }
            }

            // Triggered & Constant Lore Entries
            var recentText = string.Join(" ", session.Messages.TakeLast(10).Select(m => m.Content)) + " " + session.CurrentLocation + " " + session.Title;
            var matchedLore = world.LoreEntries
                .Where(e => e.IsConstant || e.MatchesText(recentText))
                .OrderBy(e => e.PriorityOrder)
                .Take(16)
                .ToList();

            if (matchedLore.Count > 0)
            {
                sys.AppendLine();
                sys.AppendLine("[World Lorebook & Canon Facts]");
                foreach (var lore in matchedLore)
                {
                    sys.AppendLine($"- {lore.Title}: {RoleplayEngine.ReplaceMacros(lore.Content, userName, charName)}");
                }
            }
        }

        // 6. Structured Active World-State Variables & Quests
        if (session.ActiveWorldState.Count > 0)
        {
            sys.AppendLine();
            sys.AppendLine("[Structured World State]");
            foreach (var kv in session.ActiveWorldState)
            {
                sys.AppendLine($"- {kv.Key}: {kv.Value}");
            }
        }

        // 7. Human Participants / Personas
        sys.AppendLine();
        sys.AppendLine($"[Primary Player Persona: {activePersona.Name}]");
        AppendField(sys, "Description", activePersona.Description, userName, charName);
        AppendField(sys, "Appearance", activePersona.Appearance, userName, charName);
        AppendField(sys, "Personality", activePersona.Personality, userName, charName);
        AppendField(sys, "Background", activePersona.Background, userName, charName);

        var otherHumans = session.Participants.Where(p => !p.IsAiCharacter && p.PersonaName != activePersona.Name).ToList();
        if (otherHumans.Count > 0)
        {
            sys.AppendLine();
            sys.AppendLine("[Other Human Players in This Multiplayer Session]");
            foreach (var h in otherHumans)
            {
                sys.AppendLine($"- {h.PersonaName}: {h.PersonaSummary}");
            }
            sys.AppendLine("Acknowledge and interact naturally with all human players present in the scene.");
        }

        // 8. Running Summary (Continuity)
        if (!string.IsNullOrWhiteSpace(session.RunningSummary))
        {
            sys.AppendLine();
            sys.AppendLine("[Earlier Session Summary]");
            sys.AppendLine(session.RunningSummary);
        }

        // 9. Example Dialogue
        if (activeCharacter != null && !string.IsNullOrWhiteSpace(activeCharacter.ExampleDialogue) && !isNarratorGeneration)
        {
            sys.AppendLine();
            sys.AppendLine("[Character Voice & Dialogue Examples]");
            sys.AppendLine(RoleplayEngine.ReplaceMacros(activeCharacter.ExampleDialogue, userName, charName));
        }

        var result = new List<ChatMessageTurn>
        {
            new("system", sys.ToString().Trim())
        };

        // 10. Bounded History Window (Exclude private human-to-human whispers)
        var visibleHistory = session.Messages
            .Where(m => !m.IsPrivateWhisper)
            .ToList();

        int maxMsgs = Math.Max(8, session.MaxContextMessages);
        int maxCharBudget = Math.Max(6000, (preset.ContextLength - preset.MaxOutputTokens) * 4);

        var selectedHistory = new List<SessionMessage>();
        int currentChars = sys.Length;

        for (int i = visibleHistory.Count - 1; i >= 0 && selectedHistory.Count < maxMsgs; i--)
        {
            var m = visibleHistory[i];
            if (currentChars + m.Content.Length > maxCharBudget && selectedHistory.Count >= 4)
            {
                // Auto-update running summary with older messages if enabled
                if (session.EnableAutoSummarization && string.IsNullOrWhiteSpace(session.RunningSummary) && i > 0)
                {
                    var olderSnippet = visibleHistory.Take(i + 1).TakeLast(8)
                        .Select(o => $"{o.SenderName}: {Truncate(o.Content, 140)}");
                    session.RunningSummary = "Previously: " + string.Join(" | ", olderSnippet);
                }
                break;
            }
            selectedHistory.Insert(0, m);
            currentChars += m.Content.Length;
        }

        foreach (var msg in selectedHistory)
        {
            var role = msg.Role is "assistant" ? "assistant" : "user";
            var prefix = msg.Role == "user" && session.Participants.Count(p => !p.IsAiCharacter) > 1
                ? $"[{msg.SenderName}]: "
                : string.Empty;
            result.Add(new ChatMessageTurn(role, prefix + msg.Content));
        }

        // 11. Inject Author's Note at configured depth
        if (!string.IsNullOrWhiteSpace(session.AuthorsNote))
        {
            int insertIndex = Math.Max(1, result.Count - Math.Max(0, session.AuthorsNoteDepth));
            result.Insert(insertIndex, new ChatMessageTurn("system", $"[Author's Note / Narrative Direction: {session.AuthorsNote.Trim()}]"));
        }

        // 12. Post-History Canon & World Anchor + Ensure final message is always a 'user' turn
        var worldReminder = world != null ? $" in the world of '{world.Name}' (Current Location: {session.CurrentLocation})" : $" at {session.CurrentLocation}";
        var humanNames = string.Join(" and ", session.Participants.Where(p => !p.IsAiCharacter).Select(p => p.PersonaName).DefaultIfEmpty(userName).Distinct());

        // Extract any recent dice rolls from visible history to weave into consequences
        var recentDiceMessages = session.Messages.TakeLast(8)
            .Where(m => m.Content.Contains("🎲"))
            .Select(m => m.Content.Trim())
            .ToList();
        var diceNotice = recentDiceMessages.Count > 0
            ? $" Take into account recent tabletop dice rolls: {string.Join(" | ", recentDiceMessages)}."
            : string.Empty;

        if (isRoundResolution)
        {
            result.Add(new ChatMessageTurn(
                "user",
                $"[ROUND #{session.TurnNumber} RESOLUTION & CONSEQUENCES{worldReminder}]: Both players ({humanNames}) have confirmed ready and completed their actions for this round.{diceNotice} As the Game Master / Narrator, summarize what just unfolded from their choices, describe the resulting consequences and environmental shift, how they react to one another, and set up Round #{session.TurnNumber + 1}. Write 2-3 vivid paragraphs without speaking or acting on behalf of {humanNames}.]"));
        }
        else if (isNarrator)
        {
            var coupleStyle = session.GameMode == RoleplayGameMode.TwoPersonPrivate
                ? "This is an intimate couple roleplay. Evoke the sensory atmosphere, tenderness, emotional undercurrents, and subtle consequences of their closeness."
                : "Describe environmental shifts, sensory details, and immediate consequences of their actions vividly.";

            result.Add(new ChatMessageTurn(
                "user",
                $"[Narrator Continuation{worldReminder}]: Act as the atmospheric Game Master and Narrator for {humanNames}.{diceNotice} {coupleStyle} Describe the evolving scene and consequences in 2-3 engaging paragraphs. Never speak or act on behalf of {humanNames}.]"));
        }
        else if (result.Count == 0 || result[^1].Role != "user")
        {
            result.Add(new ChatMessageTurn(
                "user",
                $"*{humanNames} looks at {charName}{worldReminder}, waiting for {charName}'s response.* [Stay strictly in character as {charName} ({activeCharacter?.Title}). Honor the world lore, {charName}'s personality/speech style, and write a vivid, in-depth response without speaking or acting for {humanNames}.]"));
        }
        else
        {
            // Append subtle canon anchor to the latest user turn so the local model stays grounded in the world & character
            var lastIdx = result.Count - 1;
            var lastTurn = result[lastIdx];
            result[lastIdx] = new ChatMessageTurn(
                "user",
                $"{lastTurn.Content}\n\n[Scene Context: Location is '{session.CurrentLocation}'{(world != null ? $" in {world.Name}" : "")}. Respond strictly in-character as {charName}, staying faithful to the world lore, character personality, and ongoing story. Do not speak or act for {humanNames}.]");
        }

        return result;
    }

    private static void AppendField(StringBuilder sb, string label, string? value, string userName, string charName)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"{label}: {RoleplayEngine.ReplaceMacros(value.Trim(), userName, charName)}");
        }
    }

    private static string Truncate(string input, int maxLen) =>
        input.Length <= maxLen ? input : input[..maxLen] + "...";
}
