# Aetheria Roleplay Package Format Specification (`AETHERIA_RP_FORMAT`)

**Current Schema Version:** `1.0`  
**Backward Compatibility:** Supports automatic migration from legacy schema `0.9` to `1.0`.

---

## 1. Design Principles & Security Guarantees

1. **Explicit Versioned Envelope:** Every single-entity Aetheria file (`.rpmodel`, `.rpchar`, `.rppersona`, `.rpworld`, `.rpsession`) is wrapped in a canonical JSON envelope (`RoleplayPackageEnvelope<T>`) containing `formatHeader`, `packageType`, `schemaVersion`, `exportedAtUtc`, `applicationVersion`, `checksumSha256`, and `payload`.
2. **Zero Credential Leakage:** API keys, bearer tokens, and provider secrets are stored exclusively in the local Windows Data Protection API (`DPAPI`) encrypted vault (`%LOCALAPPDATA%\Aetheria\credentials.dpapi`). Model presets (`.rpmodel`) and bundles (`.rpack`) strip all credential fields during serialization and reject/warn on any unexpected secret fields during import.
3. **Hardened `.rpack` ZIP Bundle Unpacking:**
   - Rejects any archive entry containing relative path traversal (`..`), rooted paths (`/`, `\`), or drive specifiers (`:`).
   - Rejects dangerous executable or script extensions (`.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.msi`, `.scr`, `.com`).
   - Enforces a strict entry count limit (`MaxBundleEntries = 100`) and uncompressed payload size cap (`MaxUncompressedSizeBytes = 25 MB`) to prevent zip-bomb resource exhaustion.

---

## 2. File Extensions Overview

| Extension | `packageType` | Description |
| :--- | :--- | :--- |
| **`.rpmodel`** | `ModelPreset` | Portable AI provider, endpoint, sampling parameters, context budget, stop sequences, and system instruction preset (strictly secret-free). |
| **`.rpchar`** | `Character` | Complete AI character definition: appearance, personality, background, speech style, goals, relationships, scenario boundaries, example dialogues, inventory, stats, and optional model override. |
| **`.rppersona`** | `Persona` | Human player roleplay identity: name, role, appearance, personality, background, and custom attributes. |
| **`.rpworld`** | `World` | Structured worldbuilding package: setting, genre, tone, narrative rules, history, starting conditions, keyword-triggered Lorebook entries, locations, factions, quests, and initial world-state variables. |
| **`.rpsession`** | `RoleplaySession` | Full roleplay session snapshot: participants, turn queue, active world-state variables, audit trail, running summary, and message history. |
| **`.rpack`** | `ResourceBundle` | Standard ZIP archive containing `manifest.json` (`ResourceBundleManifest`), `worlds/*.rpworld`, `characters/*.rpchar`, `personas/*.rppersona`, and `models/*.rpmodel`. |

---

## 3. Canonical JSON Envelope Schema (`1.0`)

```json
{
  "formatHeader": "AETHERIA_RP_FORMAT",
  "packageType": "Character | World | Persona | ModelPreset | RoleplaySession | ResourceBundle",
  "schemaVersion": "1.0",
  "exportedAtUtc": "2026-10-13T01:30:00Z",
  "applicationVersion": "1.0.0",
  "checksumSha256": "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
  "payload": { ... }
}
```

### 3.1 `.rpmodel` Payload (`ModelPreset`)
```json
{
  "id": "rx580-koboldcpp-vulkan",
  "name": "RX 580 8GB • KoboldCpp Vulkan (Local)",
  "provider": "KoboldCpp",
  "endpointUrl": "http://localhost:5001/v1",
  "modelId": "koboldcpp-default",
  "temperature": 0.82,
  "topP": 0.92,
  "topK": 40,
  "minP": 0.05,
  "repetitionPenalty": 1.08,
  "maxOutputTokens": 600,
  "contextLength": 4096,
  "streamResponses": true,
  "stopSequences": ["\nUser:", "\nSystem:"],
  "systemInstructionPrefix": "Write immersive, grounded, character-driven roleplay responses.",
  "hardwareNotes": "Optimized for AMD Radeon RX 580 8GB via Vulkan + Intel Xeon E5-2690 v3."
}
```

### 3.2 `.rpchar` Payload (`Character`)
```json
{
  "id": "char-lyra-nightshade",
  "name": "Lyra Nightshade",
  "titleOrRole": "Shadowbroker of the Obsidian Docks",
  "description": "A sharp-witted information broker...",
  "appearance": "Tall, silver-streaked raven hair...",
  "personality": "Calculating, dryly humorous, fiercely independent...",
  "background": "Former cipher clerk for the Admiralty...",
  "speechStyle": "Measured, velvet-toned, laced with nautical metaphors...",
  "motivationsAndGoals": "Uncover who sabotaged the Aether-Lighthouse...",
  "scenarioInstructions": "Never break character. Keep the intrigue grounded.",
  "boundaries": "No omniscient knowledge.",
  "firstMessage": "*Rain drums against the leaded glass of the lantern-lit study...*",
  "exampleDialogues": [
    "{{user}}: \"Can I trust you?\"\n{{char}}: \"Trust is an expensive currency on the docks, darling.\""
  ],
  "tags": ["Dark Fantasy", "Mystery", "Intrigue"],
  "startingLocation": "The Obsidian Flagon — Private Parlor",
  "startingStatus": "Alert, nursing a spiced brandy",
  "startingInventory": [
    { "name": "Brass Cipher Cylinder", "description": "Encrypted ledger key", "quantity": 1, "category": "Key Item" }
  ],
  "attributes": { "Cunning": "9", "Resolve": "8" },
  "versionNumber": 1
}
```

### 3.3 `.rpworld` Payload (`World`)
```json
{
  "id": "world-oakhaven",
  "name": "Oakhaven: The Obsidian Docks",
  "genre": "Gaslamp Dark Fantasy & Maritime Intrigue",
  "tone": "Atmospheric, Mysterious, Grounded",
  "settingSummary": "A sprawling port metropolis powered by volatile Aetherium crystals...",
  "narrativeRules": "Actions have realistic consequences...",
  "worldHistory": "Founded three centuries ago atop sunken pre-cataclysm ruins...",
  "scenarioStartingConditions": "A dense crimson fog has rolled into the harbor...",
  "customMechanics": "Investigation clues unlock when inspecting locations or questioning NPCs.",
  "loreEntries": [
    {
      "id": "lore-1",
      "title": "Aetherium Crystals",
      "keywords": ["aetherium", "crystal", "lighthouse", "smuggling"],
      "content": "Refined Aetherium glows with a pale violet luminescence...",
      "priority": 10,
      "constant": true
    }
  ],
  "locations": [
    {
      "id": "loc-1",
      "name": "The Obsidian Flagon",
      "region": "Lower Docks",
      "description": "A three-story timber tavern overhanging the black canal waters."
    }
  ],
  "factions": [
    {
      "id": "fac-1",
      "name": "The Admiralty Board",
      "alignment": "Lawful Authoritarian",
      "description": "Controls harbor tariffs and naval blockades."
    }
  ],
  "worldStateVariables": {
    "HarborLockdown": "Active",
    "FogDensity": "Heavy",
    "AlertLevel": "Elevated"
  },
  "versionNumber": 1
}
```

---

## 4. Schema Migration (`0.9` -> `1.0`)
When `RoleplayFormatSerializer` encounters a package with `"schemaVersion": "0.9"`, it automatically:
1. Upgrades the envelope version to `"1.0"`.
2. Populates default collections (`LoreEntries`, `Locations`, `Factions`, `WorldStateVariables`, `StopSequences`) if omitted in `0.9`.
3. Logs a non-fatal migration notice in `FormatValidationResult.Warnings`.
