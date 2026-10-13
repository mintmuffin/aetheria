using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Aetheria.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Aetheria.Core.Persistence;

public sealed class LibraryItemRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ResourceType { get; set; } = "rpchar"; // rpchar, rpworld, rppersona, rpmodel, rpsession, rpack
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string SchemaVersion { get; set; } = "1.0";
    public string SourceFilePath { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecentConnectionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionTitle { get; set; } = string.Empty;
    public string HostAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7777;
    public string LastParticipantId { get; set; } = string.Empty;
    public DateTimeOffset LastConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SqliteRepository : IDisposable
{
    private readonly string _connectionString;
    public string DatabasePath { get; }
    public string DataDirectory { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public SqliteRepository(string? customDataDirectory = null)
    {
        DataDirectory = customDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aetheria");
        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "aetheria_platform.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeSchema();
        SeedDefaultDataIfEmpty();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Characters (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Category TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Personas (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsDefault INTEGER NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Worlds (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Genre TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ModelPresets (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ProviderType TEXT NOT NULL,
                IsDefault INTEGER NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Sessions (
                SessionId TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                GameMode TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS LibraryItems (
                Id TEXT PRIMARY KEY,
                ResourceType TEXT NOT NULL,
                Title TEXT NOT NULL,
                ImportedAt TEXT NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS RecentConnections (
                Id TEXT PRIMARY KEY,
                HostAddress TEXT NOT NULL,
                Port INTEGER NOT NULL,
                LastConnectedAt TEXT NOT NULL,
                JsonData TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ==================== SETTINGS ====================
    public void SetSetting(string key, string value)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES ($k, $v) ON CONFLICT(Key) DO UPDATE SET Value = $v;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string ?? defaultValue;
    }

    // ==================== CHARACTERS ====================
    public void SaveCharacter(Character character)
    {
        character.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(character, JsonOpts);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Characters (Id, Name, Category, UpdatedAt, JsonData)
            VALUES ($id, $name, $cat, $upd, $json)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name, Category = $cat, UpdatedAt = $upd, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", character.Id);
        cmd.Parameters.AddWithValue("$name", character.Name);
        cmd.Parameters.AddWithValue("$cat", character.Category);
        cmd.Parameters.AddWithValue("$upd", character.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<Character> GetAllCharacters()
    {
        var list = new List<Character>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM Characters ORDER BY UpdatedAt DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var c = JsonSerializer.Deserialize<Character>(reader.GetString(0), JsonOpts);
                if (c != null) list.Add(c);
            }
            catch { }
        }
        return list;
    }

    public void DeleteCharacter(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Characters WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== PERSONAS ====================
    public void SavePersona(Persona persona)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        if (persona.IsDefault)
        {
            foreach (var existing in GetAllPersonas())
            {
                if (existing.Id != persona.Id && existing.IsDefault)
                {
                    existing.IsDefault = false;
                    SavePersonaInternal(conn, tx, existing);
                }
            }
        }
        SavePersonaInternal(conn, tx, persona);
        tx.Commit();
    }

    private static void SavePersonaInternal(SqliteConnection conn, SqliteTransaction tx, Persona persona)
    {
        var json = JsonSerializer.Serialize(persona, JsonOpts);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO Personas (Id, Name, IsDefault, JsonData)
            VALUES ($id, $name, $def, $json)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name, IsDefault = $def, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", persona.Id);
        cmd.Parameters.AddWithValue("$name", persona.Name);
        cmd.Parameters.AddWithValue("$def", persona.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<Persona> GetAllPersonas()
    {
        var list = new List<Persona>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM Personas ORDER BY IsDefault DESC, Name ASC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var p = JsonSerializer.Deserialize<Persona>(reader.GetString(0), JsonOpts);
                if (p != null) list.Add(p);
            }
            catch { }
        }
        return list;
    }

    public void DeletePersona(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Personas WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== WORLDS ====================
    public void SaveWorld(World world)
    {
        world.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(world, JsonOpts);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Worlds (Id, Name, Genre, UpdatedAt, JsonData)
            VALUES ($id, $name, $genre, $upd, $json)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name, Genre = $genre, UpdatedAt = $upd, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", world.Id);
        cmd.Parameters.AddWithValue("$name", world.Name);
        cmd.Parameters.AddWithValue("$genre", world.Genre);
        cmd.Parameters.AddWithValue("$upd", world.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<World> GetAllWorlds()
    {
        var list = new List<World>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM Worlds ORDER BY UpdatedAt DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var w = JsonSerializer.Deserialize<World>(reader.GetString(0), JsonOpts);
                if (w != null) list.Add(w);
            }
            catch { }
        }
        return list;
    }

    public void DeleteWorld(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Worlds WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== MODEL PRESETS ====================
    public void SaveModelPreset(ModelPreset preset)
    {
        var json = JsonSerializer.Serialize(preset, JsonOpts);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ModelPresets (Id, Name, ProviderType, IsDefault, JsonData)
            VALUES ($id, $name, $prov, $def, $json)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name, ProviderType = $prov, IsDefault = $def, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", preset.Id);
        cmd.Parameters.AddWithValue("$name", preset.Name);
        cmd.Parameters.AddWithValue("$prov", preset.ProviderType);
        cmd.Parameters.AddWithValue("$def", preset.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<ModelPreset> GetAllModelPresets()
    {
        var list = new List<ModelPreset>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM ModelPresets ORDER BY IsDefault DESC, Name ASC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var m = JsonSerializer.Deserialize<ModelPreset>(reader.GetString(0), JsonOpts);
                if (m != null) list.Add(m);
            }
            catch { }
        }
        return list;
    }

    public void DeleteModelPreset(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ModelPresets WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== SESSIONS ====================
    public void SaveSession(RoleplaySession session)
    {
        session.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(session, JsonOpts);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Sessions (SessionId, Title, GameMode, UpdatedAt, JsonData)
            VALUES ($id, $title, $mode, $upd, $json)
            ON CONFLICT(SessionId) DO UPDATE SET
                Title = $title, GameMode = $mode, UpdatedAt = $upd, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", session.SessionId);
        cmd.Parameters.AddWithValue("$title", session.Title);
        cmd.Parameters.AddWithValue("$mode", session.GameMode.ToString());
        cmd.Parameters.AddWithValue("$upd", session.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<RoleplaySession> GetAllSessions()
    {
        var list = new List<RoleplaySession>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM Sessions ORDER BY UpdatedAt DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var s = JsonSerializer.Deserialize<RoleplaySession>(reader.GetString(0), JsonOpts);
                if (s != null)
                {
                    foreach (var p in s.Participants)
                    {
                        if (!p.IsHost && p.ParticipantId != "host-player")
                        {
                            p.IsConnected = false;
                        }
                    }
                    list.Add(s);
                }
            }
            catch { }
        }
        return list;
    }

    public void DeleteSession(string sessionId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Sessions WHERE SessionId = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    // ==================== LIBRARY & RECENT CONNECTIONS ====================
    public void SaveLibraryItem(LibraryItemRecord record)
    {
        var json = JsonSerializer.Serialize(record, JsonOpts);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LibraryItems (Id, ResourceType, Title, ImportedAt, JsonData)
            VALUES ($id, $type, $title, $imp, $json)
            ON CONFLICT(Id) DO UPDATE SET
                ResourceType = $type, Title = $title, ImportedAt = $imp, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$type", record.ResourceType);
        cmd.Parameters.AddWithValue("$title", record.Title);
        cmd.Parameters.AddWithValue("$imp", record.ImportedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<LibraryItemRecord> GetAllLibraryItems()
    {
        var list = new List<LibraryItemRecord>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM LibraryItems ORDER BY ImportedAt DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var r = JsonSerializer.Deserialize<LibraryItemRecord>(reader.GetString(0), JsonOpts);
                if (r != null) list.Add(r);
            }
            catch { }
        }
        return list;
    }

    public void SaveRecentConnection(RecentConnectionRecord rec)
    {
        var json = JsonSerializer.Serialize(rec, JsonOpts);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RecentConnections (Id, HostAddress, Port, LastConnectedAt, JsonData)
            VALUES ($id, $host, $port, $ts, $json)
            ON CONFLICT(Id) DO UPDATE SET
                HostAddress = $host, Port = $port, LastConnectedAt = $ts, JsonData = $json;
            """;
        cmd.Parameters.AddWithValue("$id", rec.Id);
        cmd.Parameters.AddWithValue("$host", rec.HostAddress);
        cmd.Parameters.AddWithValue("$port", rec.Port);
        cmd.Parameters.AddWithValue("$ts", rec.LastConnectedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    public List<RecentConnectionRecord> GetRecentConnections()
    {
        var list = new List<RecentConnectionRecord>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JsonData FROM RecentConnections ORDER BY LastConnectedAt DESC LIMIT 12;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var r = JsonSerializer.Deserialize<RecentConnectionRecord>(reader.GetString(0), JsonOpts);
                if (r != null) list.Add(r);
            }
            catch { }
        }
        return list;
    }

    // ==================== INITIAL SEED DATA ====================
    private void SeedDefaultDataIfEmpty()
    {
        if (GetAllModelPresets().Count == 0)
        {
            SeedModelPresets();
        }
        if (GetAllPersonas().Count == 0)
        {
            SeedPersonas();
        }
        if (GetAllCharacters().Count == 0)
        {
            SeedCharactersAndWorlds();
        }
    }

    private void SeedModelPresets()
    {
        var presets = new[]
        {
            new ModelPreset
            {
                Id = "preset-kobold-vulkan",
                Name = "KoboldCpp Local Vulkan — Llama 3.1 8B (8192 Context)",
                ProviderType = "KoboldCpp",
                EndpointUrl = "http://127.0.0.1:5001/v1",
                ModelId = "koboldcpp/Llama-3.1-8B-Instruct-Q4_K_M",
                Temperature = 0.80,
                TopP = 0.92,
                TopK = 40,
                MinP = 0.06,
                RepetitionPenalty = 1.12,
                MaxOutputTokens = 700,
                ContextLength = 8192,
                IsDefault = true,
                SystemInstructions = "Write rich, canon-accurate, multi-paragraph roleplay responses with vivid sensory detail, authentic character psychology, and natural dialogue. Stay strictly faithful to the established world lore."
            },
            new ModelPreset
            {
                Id = "preset-ollama-local",
                Name = "Ollama Local — Llama 3.1 8B",
                ProviderType = "Ollama",
                EndpointUrl = "http://127.0.0.1:11434",
                ModelId = "llama3.1:8b",
                Temperature = 0.82,
                TopP = 0.92,
                MaxOutputTokens = 600,
                ContextLength = 4096,
                IsDefault = false,
                SystemInstructions = "Write atmospheric, natural character responses with vivid sensory detail."
            },
            new ModelPreset
            {
                Id = "preset-openai-gpt4o",
                Name = "OpenAI ChatGPT — GPT-4o Mini",
                ProviderType = "OpenAI",
                EndpointUrl = "https://api.openai.com/v1",
                ModelId = "gpt-4o-mini",
                Temperature = 0.85,
                TopP = 0.95,
                MaxOutputTokens = 800,
                ContextLength = 16384
            },
            new ModelPreset
            {
                Id = "preset-google-gemini",
                Name = "Google Gemini — Gemini 2.5 Flash",
                ProviderType = "Gemini",
                EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                ModelId = "gemini-2.5-flash",
                Temperature = 0.85,
                TopP = 0.95,
                MaxOutputTokens = 1024,
                ContextLength = 32768
            },
            new ModelPreset
            {
                Id = "preset-anthropic-claude",
                Name = "Anthropic Claude — Claude 3.7 Sonnet",
                ProviderType = "Anthropic",
                EndpointUrl = "https://api.anthropic.com/v1",
                ModelId = "claude-3-7-sonnet-latest",
                Temperature = 0.85,
                MaxOutputTokens = 1024,
                ContextLength = 32768
            },
            new ModelPreset
            {
                Id = "preset-openrouter-rp",
                Name = "OpenRouter — Stheno / Magnum / DeepSeek",
                ProviderType = "OpenRouter",
                EndpointUrl = "https://openrouter.ai/api/v1",
                ModelId = "sao10k/l3-8b-stheno-v3.2",
                Temperature = 0.88,
                TopP = 0.92,
                MinP = 0.05,
                RepetitionPenalty = 1.10,
                MaxOutputTokens = 800,
                ContextLength = 8192
            },
            new ModelPreset
            {
                Id = "preset-deepseek-v3",
                Name = "DeepSeek — DeepSeek Chat V3",
                ProviderType = "DeepSeek",
                EndpointUrl = "https://api.deepseek.com/v1",
                ModelId = "deepseek-chat",
                Temperature = 0.85,
                MaxOutputTokens = 800,
                ContextLength = 16384
            },
            new ModelPreset
            {
                Id = "preset-groq-fast",
                Name = "Groq Cloud — Llama 3.3 70B Versatile",
                ProviderType = "Groq",
                EndpointUrl = "https://api.groq.com/openai/v1",
                ModelId = "llama-3.3-70b-versatile",
                Temperature = 0.80,
                MaxOutputTokens = 800,
                ContextLength = 8192
            }
        };

        foreach (var p in presets) SaveModelPreset(p);
    }

    private void SeedPersonas()
    {
        // Chainsaw Man Playable Personas
        SavePersona(new Persona
        {
            Id = "persona-denji",
            Name = "Denji",
            Description = "Chainsaw Man. A rough, earnest teenage Devil Hunter who merged with his Chainsaw Devil pet Pochita to survive.",
            Appearance = "Messy blonde hair, sharp canines, Public Safety uniform or casual hoodie. Ripcord starter handle visible in chest when transformed.",
            Personality = "Blunt, impulsive, surprisingly kind beneath the crude exterior. Dreams of simple pleasures — good food, a girlfriend, a normal life. Fiercely loyal to those who show him genuine kindness.",
            Background = "Grew up in extreme poverty paying off his dead father's debts to the yakuza. Merged with the Chainsaw Devil Pochita and was recruited into Public Safety Division 4 by Makima.",
            IsDefault = true
        });
        SavePersona(new Persona
        {
            Id = "persona-makima",
            Name = "Makima",
            Description = "The Control Devil. Public Safety's enigmatic and terrifyingly powerful leader of Division 4.",
            Appearance = "Long auburn hair in braids, ringed golden-amber eyes, always composed in a crisp white shirt and black tie.",
            Personality = "Calm, manipulative, outwardly warm but calculating. Views relationships as ownership. Speaks softly with absolute authority.",
            Background = "The Control Devil incarnate, she has lived many lives and manipulated governments and Devil Hunters alike to achieve her dream of creating a world without fear — by controlling everything.",
            IsDefault = false
        });
        SavePersona(new Persona
        {
            Id = "persona-reze",
            Name = "Reze",
            Description = "The Bomb Devil hybrid. A seemingly sweet café worker who is actually a deadly Soviet assassin.",
            Appearance = "Short dark hair with a choker around her neck (grenade pin). Petite, disarmingly cute appearance that belies lethal combat ability.",
            Personality = "Playful, flirtatious, genuinely tender with Denji. Beneath the sweetness lies a trained killer torn between her mission and her real feelings.",
            Background = "A war orphan raised and weaponized by the Soviet Union's anti-devil program. Sent to Japan to capture Denji's heart — literally. She fell in love with him for real.",
            IsDefault = false
        });
        SavePersona(new Persona
        {
            Id = "persona-power",
            Name = "Power",
            Description = "The Blood Fiend. A chaotic, self-centered devil inhabiting a human corpse, and Denji's partner in Division 4.",
            Appearance = "Long strawberry-blonde hair with red horns poking through. Wild red eyes, fanged grin, Public Safety uniform perpetually disheveled.",
            Personality = "Loud, narcissistic, cowardly when outmatched, surprisingly loyal to Denji and Aki. Loves her cat Meowy above all. Claims everything is her achievement.",
            Background = "The Blood Devil who took over a human corpse. Captured by Makima and forced into Division 4. Became genuine friends with Denji despite constant bickering.",
            IsDefault = false
        });
        SavePersona(new Persona
        {
            Id = "persona-asa-mitaka",
            Name = "Asa Mitaka",
            Description = "A socially awkward high school girl sharing her body with Yoru, the War Devil.",
            Appearance = "Dark hair in twin tails with a scar across her face. School uniform. When Yoru takes over, her eyes gain cross-shaped pupils and her demeanor shifts to predatory confidence.",
            Personality = "Anxious, overthinking, deeply empathetic but terrible at expressing it. Yoru is aggressive, prideful, and obsessed with reclaiming power. Together they are chaotically unpredictable.",
            Background = "Asa was killed by the Class President (a devil) and resurrected by Yoru (War Devil) who now shares her body. Yoru wants to defeat Chainsaw Man; Asa just wants to make friends and maybe date Denji.",
            IsDefault = false
        });

        // Death Note Playable Personas
        SavePersona(new Persona
        {
            Id = "persona-light-yagami",
            Name = "Light Yagami",
            Description = "Kira. A genius honor student who found the Death Note and appointed himself judge, jury, and executioner of the criminal world.",
            Appearance = "Immaculately groomed brown hair, sharp intelligent brown eyes, always dressed impeccably — school uniform or designer casual wear.",
            Personality = "Brilliant, charismatic, narcissistic, utterly ruthless beneath a perfect-son facade. Believes he is justice itself. Master manipulator and strategist.",
            Background = "Son of NPA Chief Soichiro Yagami. Top student in Japan. Found the Death Note dropped by the Shinigami Ryuk and began killing criminals, attracting the attention of the world's greatest detective, L.",
            IsDefault = false
        });
        SavePersona(new Persona
        {
            Id = "persona-misa-amane",
            Name = "Misa Amane",
            Description = "The Second Kira. A famous model and actress devoted to Light Yagami with her own Death Note and Shinigami Eyes.",
            Appearance = "Petite, gothic-lolita fashion sense, blonde pigtails, bright blue eyes. Always stylish and attention-grabbing.",
            Personality = "Bubbly, energetic, obsessively devoted to Light. Surprisingly cunning when motivated. Willing to sacrifice anything — including her lifespan — for love.",
            Background = "Her parents were murdered by a criminal who was then killed by Kira. Devoted herself to finding and serving Kira. Made the Shinigami Eye deal twice, halving her lifespan each time.",
            IsDefault = false
        });
        SavePersona(new Persona
        {
            Id = "persona-l-lawliet",
            Name = "L Lawliet",
            Description = "The world's greatest detective. An eccentric genius hunting Kira.",
            Appearance = "Wild black hair, perpetual dark circles under wide dark eyes, white long-sleeve shirt, baggy jeans, bare feet. Always hunched in a crouching sit.",
            Personality = "Eccentric, socially detached, sweet-tooth obsessed, brutally logical. Playful and childlike on the surface but relentless and willing to bend ethics to catch criminals.",
            Background = "Raised in Wammy's House orphanage for gifted children. Became the world's top three detectives simultaneously (L, Eraldo Coil, Deneuve). Took on the Kira case as his greatest challenge.",
            IsDefault = false
        });
    }

    private void SeedCharactersAndWorlds()
    {
        SeedDataHelper.SeedCsmAndDeathNote(this);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
