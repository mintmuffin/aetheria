using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aetheria.Core.Domain;

namespace Aetheria.Core.Formats;

public static class RoleplayFormatConstants
{
    public const string FormatIdentifier = "AETHERIA_RP_FORMAT";
    public const string CurrentSchemaVersion = "1.0";

    public const string ModelExtension = ".rpmodel";
    public const string CharacterExtension = ".rpchar";
    public const string PersonaExtension = ".rppersona";
    public const string WorldExtension = ".rpworld";
    public const string SessionExtension = ".rpsession";
    public const string BundleExtension = ".rpack";

    public const long MaxArchiveUncompressedBytes = 50L * 1024L * 1024L; // 50 MB limit
    public const int MaxArchiveEntries = 250;

    public static readonly HashSet<string> ProhibitedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".wsh", ".msi", ".scr", ".com", ".hta", ".py", ".sh"
    };
}

public sealed class FormatEnvelope<T>
{
    [JsonPropertyName("formatId")]
    public string FormatId { get; set; } = RoleplayFormatConstants.FormatIdentifier;

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = RoleplayFormatConstants.CurrentSchemaVersion;

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("payload")]
    public T? Payload { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionExportOptions
{
    public bool IncludeChatHistory { get; set; } = true;
    public bool IncludePrivateWhispers { get; set; } = false;
    public bool IncludeWorldState { get; set; } = true;
}

public sealed class ResourceBundleManifest
{
    public string BundleId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Aetheria Content Bundle";
    public string Author { get; set; } = "Creator";
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<ModelPreset> ModelPresets { get; set; } = new();
    public List<Character> Characters { get; set; } = new();
    public List<Persona> Personas { get; set; } = new();
    public List<World> Worlds { get; set; } = new();
    public Dictionary<string, string> BundledAssetsBase64 { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}

public sealed class FormatValidationResult<T>
{
    public bool IsValid { get; init; }
    public T? Data { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public string? MigratedFromVersion { get; init; }

    public static FormatValidationResult<T> Success(T data, List<string>? warnings = null, string? migratedFrom = null) =>
        new()
        {
            IsValid = true,
            Data = data,
            Warnings = warnings ?? new List<string>(),
            MigratedFromVersion = migratedFrom
        };

    public static FormatValidationResult<T> Failure(params string[] errors) =>
        new()
        {
            IsValid = false,
            Errors = errors.ToList()
        };
}

public static class RoleplayFormatSerializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ==================== 1. MODEL PRESET (.rpmodel) ====================
    public static string ExportModelPreset(ModelPreset preset)
    {
        // Sanitize copy so credentials or secrets can never be included
        var sanitized = new ModelPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            ProviderType = preset.ProviderType,
            EndpointUrl = preset.EndpointUrl,
            ModelId = preset.ModelId,
            Temperature = Math.Clamp(preset.Temperature, 0.0, 2.0),
            TopP = Math.Clamp(preset.TopP, 0.0, 1.0),
            TopK = Math.Max(0, preset.TopK),
            MinP = Math.Clamp(preset.MinP, 0.0, 1.0),
            RepetitionPenalty = Math.Max(0.1, preset.RepetitionPenalty),
            FrequencyPenalty = preset.FrequencyPenalty,
            PresencePenalty = preset.PresencePenalty,
            MaxOutputTokens = Math.Max(16, preset.MaxOutputTokens),
            ContextLength = Math.Max(512, preset.ContextLength),
            StreamResponses = preset.StreamResponses,
            SystemInstructions = preset.SystemInstructions,
            StopSequences = new List<string>(preset.StopSequences),
            ProviderSpecificOptions = preset.ProviderSpecificOptions
                .Where(kv => !IsSensitiveKeyName(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
        };

        var env = new FormatEnvelope<ModelPreset>
        {
            ResourceType = "rpmodel",
            Payload = sanitized
        };

        return JsonSerializer.Serialize(env, JsonOpts);
    }

    public static FormatValidationResult<ModelPreset> ImportModelPreset(string json)
    {
        var envResult = ParseAndValidateEnvelope<ModelPreset>(json, "rpmodel");
        if (!envResult.IsValid || envResult.Data == null)
            return envResult;

        var preset = envResult.Data;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(preset.Name))
            errors.Add("Model preset 'name' is required.");
        if (string.IsNullOrWhiteSpace(preset.ProviderType))
            errors.Add("Model preset 'providerType' is required.");
        if (preset.Temperature is < 0.0 or > 2.5)
            errors.Add("Model preset 'temperature' must be between 0.0 and 2.5.");

        if (errors.Count > 0)
            return FormatValidationResult<ModelPreset>.Failure(errors.ToArray());

        // Strip any accidentally embedded sensitive options on import
        foreach (var k in preset.ProviderSpecificOptions.Keys.Where(IsSensitiveKeyName).ToList())
        {
            preset.ProviderSpecificOptions.Remove(k);
        }

        return envResult;
    }

    // ==================== 2. CHARACTER (.rpchar) ====================
    public static string ExportCharacter(Character character)
    {
        var deps = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.PreferredModelPresetId))
            deps.Add($"rpmodel:{character.PreferredModelPresetId}");

        // Embed portrait file into PortraitBase64 if a local file exists and base64 is empty
        if (string.IsNullOrWhiteSpace(character.PortraitBase64) &&
            !string.IsNullOrWhiteSpace(character.PortraitPath) &&
            File.Exists(character.PortraitPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(character.PortraitPath);
                if (bytes.Length <= 5 * 1024 * 1024)
                {
                    character.PortraitBase64 = Convert.ToBase64String(bytes);
                }
            }
            catch
            {
            }
        }

        var env = new FormatEnvelope<Character>
        {
            ResourceType = "rpchar",
            Dependencies = deps,
            Payload = character
        };
        return JsonSerializer.Serialize(env, JsonOpts);
    }

    public static FormatValidationResult<Character> ImportCharacter(string json)
    {
        var envResult = ParseAndValidateEnvelope<Character>(json, "rpchar");
        if (!envResult.IsValid || envResult.Data == null)
            return envResult;

        var ch = envResult.Data;
        if (string.IsNullOrWhiteSpace(ch.Name))
            return FormatValidationResult<Character>.Failure("Character 'name' cannot be empty.");

        if (!string.IsNullOrWhiteSpace(ch.PortraitPath) && IsUnsafePath(ch.PortraitPath))
        {
            ch.PortraitPath = string.Empty;
            envResult.Warnings.Add("Cleared unsafe relative/rooted portraitPath during import.");
        }

        return envResult;
    }

    // ==================== 3. PERSONA (.rppersona) ====================
    public static string ExportPersona(Persona persona)
    {
        var env = new FormatEnvelope<Persona>
        {
            ResourceType = "rppersona",
            Payload = persona
        };
        return JsonSerializer.Serialize(env, JsonOpts);
    }

    public static FormatValidationResult<Persona> ImportPersona(string json)
    {
        var envResult = ParseAndValidateEnvelope<Persona>(json, "rppersona");
        if (!envResult.IsValid || envResult.Data == null)
            return envResult;

        if (string.IsNullOrWhiteSpace(envResult.Data.Name))
            return FormatValidationResult<Persona>.Failure("Persona 'name' is required.");

        return envResult;
    }

    // ==================== 4. WORLD (.rpworld) ====================
    public static string ExportWorld(World world)
    {
        var deps = world.CharacterRosterIds.Select(id => $"rpchar:{id}").ToList();
        var env = new FormatEnvelope<World>
        {
            ResourceType = "rpworld",
            Dependencies = deps,
            Payload = world
        };
        return JsonSerializer.Serialize(env, JsonOpts);
    }

    public static FormatValidationResult<World> ImportWorld(string json)
    {
        var envResult = ParseAndValidateEnvelope<World>(json, "rpworld");
        if (!envResult.IsValid || envResult.Data == null)
            return envResult;

        if (string.IsNullOrWhiteSpace(envResult.Data.Name))
            return FormatValidationResult<World>.Failure("World 'name' is required.");

        return envResult;
    }

    // ==================== 5. SESSION (.rpsession) ====================
    public static string ExportSession(RoleplaySession session, SessionExportOptions? options = null)
    {
        options ??= new SessionExportOptions();
        var copy = JsonSerializer.Deserialize<RoleplaySession>(JsonSerializer.Serialize(session, JsonOpts), JsonOpts)!;

        if (!options.IncludeChatHistory)
        {
            copy.Messages.Clear();
        }
        else if (!options.IncludePrivateWhispers)
        {
            copy.Messages = copy.Messages.Where(m => !m.IsPrivateWhisper).ToList();
        }

        if (!options.IncludeWorldState)
        {
            copy.ActiveWorldState.Clear();
        }

        var deps = new List<string>();
        if (!string.IsNullOrWhiteSpace(copy.WorldId)) deps.Add($"rpworld:{copy.WorldId}");
        deps.AddRange(copy.ActiveCharacterIds.Select(c => $"rpchar:{c}"));

        var env = new FormatEnvelope<RoleplaySession>
        {
            ResourceType = "rpsession",
            Dependencies = deps,
            Payload = copy
        };
        return JsonSerializer.Serialize(env, JsonOpts);
    }

    public static FormatValidationResult<RoleplaySession> ImportSession(string json)
    {
        var envResult = ParseAndValidateEnvelope<RoleplaySession>(json, "rpsession");
        if (!envResult.IsValid || envResult.Data == null)
            return envResult;

        if (string.IsNullOrWhiteSpace(envResult.Data.Title))
            return FormatValidationResult<RoleplaySession>.Failure("Session 'title' is required.");

        return envResult;
    }

    // ==================== 6. BUNDLE (.rpack) ====================
    public static void ExportBundleToFile(string outputPath, ResourceBundleManifest bundle)
    {
        // Sanitize all model presets in the bundle
        foreach (var preset in bundle.ModelPresets)
        {
            foreach (var k in preset.ProviderSpecificOptions.Keys.Where(IsSensitiveKeyName).ToList())
                preset.ProviderSpecificOptions.Remove(k);
        }

        var env = new FormatEnvelope<ResourceBundleManifest>
        {
            ResourceType = "rpack",
            Payload = bundle
        };

        var manifestJson = JsonSerializer.Serialize(env, JsonOpts);

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry("manifest.rpack.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
        {
            writer.Write(manifestJson);
        }

        // Write individual characters and worlds inside archive for easy inspection
        foreach (var ch in bundle.Characters)
        {
            var entry = archive.CreateEntry($"characters/{SanitizeFileName(ch.Id)}.rpchar", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(ExportCharacter(ch));
        }

        foreach (var w in bundle.Worlds)
        {
            var entry = archive.CreateEntry($"worlds/{SanitizeFileName(w.Id)}.rpworld", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(ExportWorld(w));
        }
    }

    public static FormatValidationResult<ResourceBundleManifest> ImportBundleFromFile(string archivePath)
    {
        if (!File.Exists(archivePath))
            return FormatValidationResult<ResourceBundleManifest>.Failure($"Bundle file not found: {archivePath}");

        try
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

            if (archive.Entries.Count > RoleplayFormatConstants.MaxArchiveEntries)
            {
                return FormatValidationResult<ResourceBundleManifest>.Failure(
                    $"Archive exceeds maximum allowed entry count ({RoleplayFormatConstants.MaxArchiveEntries}).");
            }

            long totalUncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                if (IsUnsafePath(entry.FullName))
                {
                    return FormatValidationResult<ResourceBundleManifest>.Failure(
                        $"Security Violation: Archive entry '{entry.FullName}' contains path traversal or rooted path.");
                }

                var ext = Path.GetExtension(entry.Name);
                if (RoleplayFormatConstants.ProhibitedExtensions.Contains(ext))
                {
                    return FormatValidationResult<ResourceBundleManifest>.Failure(
                        $"Security Violation: Archive contains prohibited executable/script file '{entry.FullName}'.");
                }

                totalUncompressed += entry.Length;
                if (totalUncompressed > RoleplayFormatConstants.MaxArchiveUncompressedBytes)
                {
                    return FormatValidationResult<ResourceBundleManifest>.Failure(
                        $"Security Violation: Archive uncompressed size exceeds 50 MB safety limit.");
                }
            }

            var manifestEntry = archive.GetEntry("manifest.rpack.json");
            if (manifestEntry == null)
            {
                return FormatValidationResult<ResourceBundleManifest>.Failure(
                    "Invalid .rpack archive: Missing required 'manifest.rpack.json' root entry.");
            }

            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
            var json = reader.ReadToEnd();
            var result = ParseAndValidateEnvelope<ResourceBundleManifest>(json, "rpack");
            if (!result.IsValid || result.Data == null)
                return result;

            if (string.IsNullOrWhiteSpace(result.Data.Title))
                return FormatValidationResult<ResourceBundleManifest>.Failure("Bundle 'title' is required.");

            return result;
        }
        catch (InvalidDataException)
        {
            return FormatValidationResult<ResourceBundleManifest>.Failure("Corrupt or invalid .rpack ZIP archive.");
        }
        catch (Exception ex)
        {
            return FormatValidationResult<ResourceBundleManifest>.Failure($"Failed to read .rpack bundle: {ex.Message}");
        }
    }

    // ==================== ENVELOPE VALIDATION & MIGRATION ====================
    private static FormatValidationResult<T> ParseAndValidateEnvelope<T>(string json, string expectedResourceType) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return FormatValidationResult<T>.Failure("File content is empty.");

        FormatEnvelope<T>? env;
        try
        {
            env = JsonSerializer.Deserialize<FormatEnvelope<T>>(json, JsonOpts);
        }
        catch (JsonException jex)
        {
            return FormatValidationResult<T>.Failure($"Malformed JSON structure: {jex.Message}");
        }

        if (env == null)
            return FormatValidationResult<T>.Failure("Could not deserialize format envelope.");

        if (!string.Equals(env.FormatId, RoleplayFormatConstants.FormatIdentifier, StringComparison.Ordinal))
        {
            return FormatValidationResult<T>.Failure(
                $"Invalid formatId '{env.FormatId}'. Expected '{RoleplayFormatConstants.FormatIdentifier}'.");
        }

        if (!string.Equals(env.ResourceType, expectedResourceType, StringComparison.OrdinalIgnoreCase))
        {
            return FormatValidationResult<T>.Failure(
                $"Resource type mismatch: File is '{env.ResourceType}', but importer expected '{expectedResourceType}'.");
        }

        var warnings = new List<string>();
        string? migratedFrom = null;

        // Schema Version Check & Migration
        if (env.SchemaVersion is "0.9" or "0.8" or "1.0.0")
        {
            migratedFrom = env.SchemaVersion;
            env.SchemaVersion = RoleplayFormatConstants.CurrentSchemaVersion;
            warnings.Add($"Automatically migrated schema from version {migratedFrom} to {RoleplayFormatConstants.CurrentSchemaVersion}.");
        }
        else if (env.SchemaVersion != RoleplayFormatConstants.CurrentSchemaVersion)
        {
            if (Version.TryParse(env.SchemaVersion, out var fileVer) &&
                Version.TryParse(RoleplayFormatConstants.CurrentSchemaVersion, out var curVer) &&
                fileVer.Major == curVer.Major)
            {
                warnings.Add($"Loaded forward-compatible minor schema version {env.SchemaVersion}.");
            }
            else
            {
                return FormatValidationResult<T>.Failure(
                    $"Unsupported major schema version '{env.SchemaVersion}'. Supported version is '{RoleplayFormatConstants.CurrentSchemaVersion}'.");
            }
        }

        if (env.Payload == null)
        {
            return FormatValidationResult<T>.Failure("Resource envelope 'payload' is null or missing.");
        }

        return FormatValidationResult<T>.Success(env.Payload, warnings, migratedFrom);
    }

    public static bool IsUnsafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains("..", StringComparison.Ordinal)) return true;
        if (Path.IsPathRooted(path)) return true;
        if (path.Contains(':', StringComparison.Ordinal)) return true;
        if (path.StartsWith('/') || path.StartsWith('\\')) return true;
        return false;
    }

    private static bool IsSensitiveKeyName(string key) =>
        key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("auth", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
