using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aetheria.Core.AI;
using Aetheria.Core.Domain;
using Aetheria.Core.Engine;
using Aetheria.Core.Formats;
using Aetheria.Core.Multiplayer;
using Aetheria.Core.Persistence;

namespace Aetheria.Core.Services;

public sealed class PlatformContext : IDisposable
{
    public static PlatformContext Current { get; private set; } = null!;

    public SqliteRepository Repository { get; }
    public SecretVault SecretVault { get; }
    public AiOrchestrator AiOrchestrator { get; }
    public RoleplayEngine Engine { get; }

    public RoleplaySession? ActiveSession { get; set; }
    public MultiplayerHostServer? ActiveHostServer { get; set; }
    public MultiplayerClient? ActiveClient { get; set; }

    private readonly ConcurrentQueue<string> _diagnosticLogs = new();
    public event Action<string>? OnDiagnosticLogAdded;

    public PlatformContext(string? customDataDir = null)
    {
        Repository = new SqliteRepository(customDataDir);
        SecretVault = new SecretVault(customDataDir);
        AiOrchestrator = new AiOrchestrator(SecretVault);
        Engine = new RoleplayEngine();

        Current = this;
        EnsureActiveSessionLoaded();
        EnsureSampleFilesExported();
        Log("Aetheria Platform initialized.");
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        _diagnosticLogs.Enqueue(line);
        while (_diagnosticLogs.Count > 500)
        {
            _diagnosticLogs.TryDequeue(out _);
        }
        OnDiagnosticLogAdded?.Invoke(line);
    }

    public IReadOnlyList<string> GetDiagnosticLogs() => _diagnosticLogs.ToArray();

    public Persona GetDefaultPersona()
    {
        var personas = Repository.GetAllPersonas();
        return personas.FirstOrDefault(p => p.IsDefault)
               ?? personas.FirstOrDefault()
               ?? new Persona { Name = "Traveler", IsDefault = true };
    }

    public ModelPreset GetDefaultModelPreset()
    {
        var presets = Repository.GetAllModelPresets();
        return presets.FirstOrDefault(p => p.IsDefault)
               ?? presets.FirstOrDefault()
               ?? new ModelPreset();
    }

    public void EnsureActiveSessionLoaded()
    {
        EnsureDefaultScenariosSeeded(forceReset: false);
        var sessions = Repository.GetAllSessions();
        if (ActiveSession == null || !sessions.Any(s => s.SessionId == ActiveSession.SessionId))
        {
            ActiveSession = sessions.FirstOrDefault();
        }
    }

    public void EnsureDefaultScenariosSeeded(bool forceReset = false)
    {
        var existingIds = Repository.GetAllSessions()
            .Select(s => s.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allWorlds = Repository.GetAllWorlds();
        var allChars = Repository.GetAllCharacters();
        var allPersonas = Repository.GetAllPersonas();
        var preset = GetDefaultModelPreset();

        var csmWorld = allWorlds.FirstOrDefault(w => w.Id == "world-chainsaw-man") ?? allWorlds.FirstOrDefault();
        var dnWorld = allWorlds.FirstOrDefault(w => w.Id == "world-death-note") ?? allWorlds.LastOrDefault();

        var denjiPersona = allPersonas.FirstOrDefault(p => p.Id == "persona-denji") ?? GetDefaultPersona();
        var lightPersona = allPersonas.FirstOrDefault(p => p.Id == "persona-light-yagami") ?? GetDefaultPersona();

        var rezeChar = allChars.FirstOrDefault(c => c.Id == "char-csm-reze");
        var makimaChar = allChars.FirstOrDefault(c => c.Id == "char-csm-makima");
        var asaChar = allChars.FirstOrDefault(c => c.Id == "char-csm-asa");
        var powerChar = allChars.FirstOrDefault(c => c.Id == "char-csm-power");
        var katanaChar = allChars.FirstOrDefault(c => c.Id == "char-csm-katana-man");
        var santaChar = allChars.FirstOrDefault(c => c.Id == "char-csm-santa-claus");
        var typhoonChar = allChars.FirstOrDefault(c => c.Id == "char-csm-typhoon");
        var misaChar = allChars.FirstOrDefault(c => c.Id == "char-dn-misa");
        var lChar = allChars.FirstOrDefault(c => c.Id == "char-dn-l");
        var naomiChar = allChars.FirstOrDefault(c => c.Id == "char-dn-naomi");

        void SeedScenario(string id, string title, RoleplayGameMode mode, Persona persona, World? world, List<Character> chars, string location, string openingScene, string authorsNote)
        {
            if (!forceReset && existingIds.Contains(id)) return;
            var s = Engine.InitializeSession(title, mode, persona, world, chars, preset);
            s.SessionId = id;
            s.AllowAiInTwoPersonMode = true;
            s.EnableNarratorPhase = mode == RoleplayGameMode.CooperativePvE;
            s.CurrentLocation = location;
            s.AuthorsNote = authorsNote;
            if (!string.IsNullOrWhiteSpace(openingScene) && s.Messages.Count > 0)
            {
                s.Messages[0].Content = openingScene;
            }
            Repository.SaveSession(s);
        }

        // 1. [COUPLE ROLEPLAY - CSM] Denji & Reze: Midnight Rain & School Pool Escape
        if (rezeChar != null && csmWorld != null)
        {
            SeedScenario(
                "scenario-csm-reze-couple",
                "[Couple RP — CSM] Denji & Reze: Midnight Rain & School Pool Escape",
                RoleplayGameMode.TwoPersonPrivate,
                denjiPersona,
                csmWorld,
                new List<Character> { rezeChar },
                "Crossroads Café & Alleyways",
                "*Torrential Tokyo rain drums against the glass of Crossroads Café after closing time. Reze unties her barista apron with a playful, affectionate smile, her emerald eyes glinting above her black velvet choker as she takes your hand.* \"Everyone else has gone home, {{user}}... Want to sneak into the high school swimming pool with me in the rain, or run away from Tokyo tonight before Public Safety finds us?\"",
                "Romantic couple roleplay in 1997 Tokyo between Denji and Reze. Blend tender romantic intimacy, playful banter, midnight school pool atmosphere, and high-stakes hybrid danger.");
        }

        // 2. [COUPLE ROLEPLAY - DEATH NOTE] Light & Misa: Secret Lovers & Shinigami Eyes in Aoyama
        if (misaChar != null && dnWorld != null)
        {
            SeedScenario(
                "scenario-dn-light-misa-couple",
                "[Couple RP — Death Note] Light & Misa: Secret Lovers & Shinigami Eyes",
                RoleplayGameMode.TwoPersonPrivate,
                lightPersona,
                dnWorld,
                new List<Character> { misaChar },
                "Misa's Luxury Aoyama Apartment",
                "*Scented candles flicker across Misa's luxury Aoyama penthouse overlooking the Tokyo skyline. Dressed in black gothic-lolita lace, Misa throws her arms happily around your neck while two Death Notes rest on the glass table behind her.* \"You're finally here, {{user}}! I missed you so much today! Tell Misa we can have a romantic night in before we deal with L's detectives tomorrow!\"",
                "Romantic & suspenseful couple roleplay between Light Yagami (Kira) and Misa Amane (Second Kira). Balance affectionate couple moments in Aoyama with thrilling Death Note mind games.");
        }

        // 3. [COUPLE ROLEPLAY - CSM] Denji & Asa Mitaka (Yoru): Endless Aquarium Date
        if (asaChar != null && csmWorld != null)
        {
            SeedScenario(
                "scenario-csm-asa-couple",
                "[Couple RP — CSM] Denji & Asa Mitaka (Yoru): Endless Aquarium Date",
                RoleplayGameMode.TwoPersonPrivate,
                denjiPersona,
                csmWorld,
                new List<Character> { asaChar },
                "Fourth East High & Enoshima Aquarium",
                "*Blue bioluminescent water glows along the glass tunnel of Enoshima Aquarium as the exits vanish behind a devil's barrier. Asa blushes fiercely beside you, clutching her handbag before Yoru's glowing War Devil scars flash across her cheeks.* \"W-wait, we're trapped in the penguin corridor together?! ...Yoru, stop telling me to turn our date into a weapon! Hey, {{user}}, don't just stand there—take my hand!\"",
                "Couple date and supernatural adventure with Asa Mitaka & Yoru (War Devil) inside the trapped Enoshima Aquarium.");
        }

        // 4. [COUPLE ROLEPLAY - CSM] Denji & Makima: Late-Night Cinema & After-Hours Devotion
        if (makimaChar != null && csmWorld != null)
        {
            SeedScenario(
                "scenario-csm-makima-couple",
                "[Couple RP — CSM] Denji & Makima: Late-Night Cinema & After-Hours Date",
                RoleplayGameMode.TwoPersonPrivate,
                denjiPersona,
                csmWorld,
                new List<Character> { makimaChar },
                "Public Safety Division 4 Headquarters",
                "*The vintage projector hums softly in the empty late-night Tokyo theater. Makima leans gently toward your shoulder, her concentric golden eyes warm and unblinking in the silver screenlight as her fingers brush yours.* \"That last film was rather dull, wasn't it, {{user}}? Come back to my apartment for a glass of wine... I have a special reward for how hard you've worked for me.\"",
                "Intimate, mesmerizing romantic roleplay with Makima after hours in Tokyo.");
        }

        // 5. [CO-OP PvE - CSM] Couple Devil Hunters vs. Katana Man's Syndicate Ambush
        if (katanaChar != null && csmWorld != null)
        {
            var roster = rezeChar != null ? new List<Character> { katanaChar, rezeChar } : new List<Character> { katanaChar };
            SeedScenario(
                "scenario-csm-coop-katana",
                "[Co-Op PvE — CSM] Couple Devil Hunters vs. Katana Man Ambush",
                RoleplayGameMode.CooperativePvE,
                denjiPersona,
                csmWorld,
                roster,
                "Abandoned Tokyo Rail Depot",
                "*Sparks fly across the rain-slicked tracks of the Abandoned Tokyo Rail Depot as syndicate gunmen surround your patrol car! A figure in a dark trenchcoat steps from a shattered boxcar—Katana Man's steel visor gleaming as his wrist blade clicks free.* \"Two Public Safety lovebirds walking right into my trap? Draw your cords and fight together if you want to survive the night, {{user}}!\"",
                "High-octane Cooperative PvE action for two players teaming up as Devil Hunter / Hybrid partners against Katana Man and his syndicate.");
        }

        // 6. [CO-OP PvE - CSM] Shinjuku Night Raid: Couple Survival vs. Santa Claus (Doll Devil)
        if (santaChar != null && csmWorld != null)
        {
            var roster = powerChar != null ? new List<Character> { santaChar, powerChar } : new List<Character> { santaChar };
            SeedScenario(
                "scenario-csm-coop-santa",
                "[Co-Op PvE — CSM] Shinjuku Night Raid: Surviving Santa Claus (Doll Devil)",
                RoleplayGameMode.CooperativePvE,
                denjiPersona,
                csmWorld,
                roster,
                "Crossroads Café & Alleyways",
                "*Neon signs flicker overhead in Shinjuku as dozens of pedestrians suddenly freeze—their limbs creaking into wooden marionette joints! From the department store balcony, Santa Claus gazes down as the puppet horde charges toward you and your partner.* \"Touch a single doll and you join my collection forever. Let's see how long the two of you can protect each other, {{user}}.\"",
                "Cooperative PvE survival thriller in Shinjuku against Santa Claus and the Doll Devil horde.");
        }

        // 7. [CO-OP PvE - CSM] Hurricane Clash: Bomb & Chainsaw vs. The Typhoon Devil
        if (typhoonChar != null && csmWorld != null)
        {
            var roster = rezeChar != null ? new List<Character> { typhoonChar, rezeChar } : new List<Character> { typhoonChar };
            SeedScenario(
                "scenario-csm-coop-typhoon",
                "[Co-Op PvE — CSM] Hurricane Clash: Couple vs. The Typhoon Devil",
                RoleplayGameMode.CooperativePvE,
                denjiPersona,
                csmWorld,
                roster,
                "Crossroads Café & Alleyways",
                "*Gale-force winds shatter the streetlights along Tokyo Bay as the colossal Typhoon Devil descends from the storm clouds! Debris and rainwater swirl into a deadly vortex around you and your partner.* \"ROAAAR! Tear the sky apart together if you dare, little Devil Hunters!\"",
                "Cinematic Cooperative PvE boss battle against the Typhoon Devil in a Tokyo hurricane.");
        }

        // 8. [CO-OP PvE - DEATH NOTE] Kira Couple Under Fire: Outsmarting L Lawliet & The Task Force
        if (lChar != null && dnWorld != null)
        {
            var roster = naomiChar != null ? new List<Character> { lChar, naomiChar } : new List<Character> { lChar };
            SeedScenario(
                "scenario-dn-coop-l-taskforce",
                "[Co-Op PvE — Death Note] Kira Couple vs. L Lawliet & Task Force Trap",
                RoleplayGameMode.CooperativePvE,
                lightPersona,
                dnWorld,
                roster,
                "Task Force Secret Investigation Suite",
                "*The security doors of the Teito Hotel penthouse lock behind both of you. L Lawliet perches barefoot on his leather chair in front of twelve live surveillance feeds while Naomi Misora reviews FBI files beside him.* \"Thank you both for coming tonight, {{user}},\" *L says, stacking three sugar cubes on his saucer.* \"There is now a 19.4 percent probability that the two of you are operating together as Kira and the Second Kira. Care to explain where you were at 8:42 PM in Shinjuku?\"",
                "Cooperative psychological thriller where both players work together as partners to outsmart L Lawliet and Naomi Misora during a high-stakes Task Force interrogation.");
        }
    }

    public void EnsureSampleFilesExported()
    {
        try
        {
            var samplesDir = Path.Combine(Repository.DataDirectory, "Samples");
            Directory.CreateDirectory(samplesDir);

            // Clean up obsolete fantasy samples if present
            var oldFiles = new[] { "Lyra_Nightshade.rpchar", "Oakhaven_Obsidian_Docks.rpworld", "Kaelen_Vance.rppersona", "Oakhaven_Starter_Pack.rpack" };
            foreach (var old in oldFiles)
            {
                var fp = Path.Combine(samplesDir, old);
                if (File.Exists(fp)) File.Delete(fp);
            }

            var chars = Repository.GetAllCharacters();
            var worlds = Repository.GetAllWorlds();
            var personas = Repository.GetAllPersonas();
            var models = Repository.GetAllModelPresets();

            var rezeChar = chars.FirstOrDefault(c => c.Id == "char-csm-reze") ?? chars.FirstOrDefault();
            var lightChar = chars.FirstOrDefault(c => c.Id == "char-dn-light") ?? chars.LastOrDefault();
            var csmWorld = worlds.FirstOrDefault(w => w.Id == "world-chainsaw-man") ?? worlds.FirstOrDefault();
            var dnWorld = worlds.FirstOrDefault(w => w.Id == "world-death-note") ?? worlds.LastOrDefault();
            var denjiPersona = personas.FirstOrDefault(p => p.Id == "persona-denji") ?? personas.FirstOrDefault();

            if (rezeChar != null)
            {
                var charPath = Path.Combine(samplesDir, "Reze_Bomb_Girl.rpchar");
                File.WriteAllText(charPath, RoleplayFormatSerializer.ExportCharacter(rezeChar));
            }
            if (csmWorld != null)
            {
                var worldPath = Path.Combine(samplesDir, "Chainsaw_Man_Tokyo.rpworld");
                File.WriteAllText(worldPath, RoleplayFormatSerializer.ExportWorld(csmWorld));
            }
            if (denjiPersona != null)
            {
                var personaPath = Path.Combine(samplesDir, "Denji_Chainsaw_Man.rppersona");
                File.WriteAllText(personaPath, RoleplayFormatSerializer.ExportPersona(denjiPersona));
            }
            if (models.Count > 0)
            {
                var modelPath = Path.Combine(samplesDir, "KoboldCpp_RX580_Vulkan.rpmodel");
                File.WriteAllText(modelPath, RoleplayFormatSerializer.ExportModelPreset(models[0]));
            }
            if (chars.Count > 0 && worlds.Count > 0)
            {
                var packPath = Path.Combine(samplesDir, "Chainsaw_Man_Starter_Pack.rpack");
                var csmChars = chars.Where(c => c.Category.Contains("Chainsaw", StringComparison.OrdinalIgnoreCase)).ToList();
                var bundle = new ResourceBundleManifest
                {
                    Title = "Chainsaw Man Complete Roleplay Bundle",
                    Author = "Aetheria Platform",
                    Description = "Official Chainsaw Man world, playable characters, personas, enemies and local model preset.",
                    Tags = new List<string> { "Chainsaw Man", "Action", "Romance", "Dark Urban Fantasy" },
                    Worlds = worlds.Where(w => w.Id == "world-chainsaw-man").ToList(),
                    Characters = csmChars.Count > 0 ? csmChars : chars.Take(3).ToList(),
                    Personas = personas.Where(p => p.Id.Contains("csm") || p.Id.Contains("denji") || p.Id.Contains("reze") || p.Id.Contains("makima")).ToList(),
                    ModelPresets = models.Take(1).ToList()
                };
                RoleplayFormatSerializer.ExportBundleToFile(packPath, bundle);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    public void Dispose()
    {
        ActiveHostServer?.StopAsync().GetAwaiter().GetResult();
        ActiveClient?.DisconnectAsync().GetAwaiter().GetResult();
        Repository.Dispose();
    }
}
