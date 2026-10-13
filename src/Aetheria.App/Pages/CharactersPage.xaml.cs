using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aetheria.App.Helpers;
using Aetheria.Core.Domain;
using Aetheria.Core.Formats;
using Aetheria.Core.Persistence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aetheria.App.Pages;

public sealed partial class CharactersPage : Page
{
    private List<Character> _allCharacters = new();
    private Character? _selectedCharacter;

    public CharactersPage()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) => LoadRoster();
    }

    private void LoadRoster(string? selectId = null)
    {
        var ctx = App.Platform;
        _allCharacters = ctx.Repository.GetAllCharacters();

        PreferredModelCombo.Items.Clear();
        PreferredModelCombo.Items.Add(new ComboBoxItem { Content = "(Inherit Session Model)", Tag = "" });
        foreach (var preset in ctx.Repository.GetAllModelPresets())
        {
            PreferredModelCombo.Items.Add(new ComboBoxItem { Content = $"{preset.Name} [{preset.ProviderType}]", Tag = preset.Id });
        }

        ApplyFilterAndBindList(selectId);
    }

    private void ApplyFilterAndBindList(string? selectId = null)
    {
        var q = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allCharacters
            : _allCharacters.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();

        CharactersList.Items.Clear();
        int targetIndex = 0;
        for (int i = 0; i < filtered.Count; i++)
        {
            var c = filtered[i];
            CharactersList.Items.Add(new ListViewItem
            {
                Content = $"{c.Name} ({c.Category})",
                Tag = c.Id
            });
            if (selectId != null && c.Id == selectId)
                targetIndex = i;
        }

        if (CharactersList.Items.Count > 0)
            CharactersList.SelectedIndex = targetIndex;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilterAndBindList(_selectedCharacter?.Id);

    private void CharactersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharactersList.SelectedItem is ListViewItem item && item.Tag is string id)
        {
            var found = _allCharacters.FirstOrDefault(c => c.Id == id);
            if (found != null) BindCharacterToForm(found);
        }
    }

    private void BindCharacterToForm(Character c)
    {
        _selectedCharacter = c;
        EditorHeaderTitle.Text = c.Name;
        EditorVersionSubtitle.Text = $"ID: {c.Id} • Version {c.Version}";

        NameBox.Text = c.Name;
        TitleBox.Text = c.Title;
        CategoryBox.Text = c.Category;
        VersionBox.Text = c.Version;
        TagsBox.Text = string.Join(", ", c.Tags);
        PortraitPathBox.Text = c.PortraitPath;
        DescriptionBox.Text = c.Description;
        AppearanceBox.Text = c.Appearance;
        PersonalityBox.Text = c.Personality;
        BackgroundBox.Text = c.Background;
        SpeechStyleBox.Text = c.SpeechStyle;
        BehavioralTraitsBox.Text = c.BehavioralTraits;
        MotivationsBox.Text = c.Motivations;
        GoalsBox.Text = c.Goals;
        RelationshipsBox.Text = c.Relationships;
        FirstMessageBox.Text = c.FirstMessage;
        ExampleDialogueBox.Text = c.ExampleDialogue;
        AlternateGreetingsBox.Text = string.Join("\n---\n", c.AlternateGreetings);
        ScenarioInstructionsBox.Text = c.ScenarioInstructions;
        BoundariesBox.Text = c.Boundaries;
        StartingLocationBox.Text = c.StartingLocation;
        MemoryContinuityBox.Text = c.MemoryContinuitySettings;
        InventoryRawBox.Text = string.Join(", ", c.Inventory.Select(i => i.Name));

        PreferredModelCombo.SelectedIndex = 0;
        for (int i = 0; i < PreferredModelCombo.Items.Count; i++)
        {
            if (PreferredModelCombo.Items[i] is ComboBoxItem mItem &&
                string.Equals(mItem.Tag as string, c.PreferredModelPresetId ?? "", StringComparison.OrdinalIgnoreCase))
            {
                PreferredModelCombo.SelectedIndex = i;
                break;
            }
        }
    }

    private bool PopulateCharacterFromForm(Character target, out string? validationError)
    {
        validationError = null;
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            validationError = "Character Name is required.";
            return false;
        }

        target.Name = name;
        target.Title = TitleBox.Text.Trim();
        target.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "General" : CategoryBox.Text.Trim();
        target.Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? "1.0.0" : VersionBox.Text.Trim();
        target.Tags = TagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        target.PortraitPath = PortraitPathBox.Text.Trim();
        target.Description = DescriptionBox.Text.Trim();
        target.Appearance = AppearanceBox.Text.Trim();
        target.Personality = PersonalityBox.Text.Trim();
        target.Background = BackgroundBox.Text.Trim();
        target.SpeechStyle = SpeechStyleBox.Text.Trim();
        target.BehavioralTraits = BehavioralTraitsBox.Text.Trim();
        target.Motivations = MotivationsBox.Text.Trim();
        target.Goals = GoalsBox.Text.Trim();
        target.Relationships = RelationshipsBox.Text.Trim();
        target.FirstMessage = FirstMessageBox.Text.Trim();
        target.ExampleDialogue = ExampleDialogueBox.Text.Trim();
        target.AlternateGreetings = AlternateGreetingsBox.Text
            .Split("---", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        target.ScenarioInstructions = ScenarioInstructionsBox.Text.Trim();
        target.Boundaries = BoundariesBox.Text.Trim();
        target.StartingLocation = StartingLocationBox.Text.Trim();
        target.MemoryContinuitySettings = string.IsNullOrWhiteSpace(MemoryContinuityBox.Text) ? "Standard" : MemoryContinuityBox.Text.Trim();
        target.PreferredModelPresetId = (PreferredModelCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(target.PreferredModelPresetId))
            target.PreferredModelPresetId = null;

        var invNames = InventoryRawBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        target.Inventory = invNames.Select(n => new InventoryItem { Name = n, Quantity = 1 }).ToList();

        return true;
    }

    private void SaveCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) _selectedCharacter = new Character();
        if (!PopulateCharacterFromForm(_selectedCharacter, out var err))
        {
            App.MainWindowInstance.ShowNotification("Validation Error", err ?? "Invalid character fields.", InfoBarSeverity.Error);
            return;
        }

        App.Platform.Repository.SaveCharacter(_selectedCharacter);
        LoadRoster(_selectedCharacter.Id);
        App.MainWindowInstance.ShowNotification("Character Saved", $"Saved '{_selectedCharacter.Name}' (v{_selectedCharacter.Version}).", InfoBarSeverity.Success);
    }

    private void NewCharacter_Click(object sender, RoutedEventArgs e)
    {
        var ch = new Character
        {
            Name = "New Character",
            Category = "Fantasy",
            FirstMessage = "*Looks toward you as you enter.* \"Greetings, {{user}}.\""
        };
        App.Platform.Repository.SaveCharacter(ch);
        LoadRoster(ch.Id);
    }

    private void DuplicateCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) return;
        var copy = _selectedCharacter.CloneAsDuplicate();
        App.Platform.Repository.SaveCharacter(copy);
        LoadRoster(copy.Id);
        App.MainWindowInstance.ShowNotification("Duplicated", $"Created copy '{copy.Name}'.", InfoBarSeverity.Informational);
    }

    private void PlayAsCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) _selectedCharacter = new Character();
        if (!PopulateCharacterFromForm(_selectedCharacter, out var err))
        {
            App.MainWindowInstance.ShowNotification("Validation Error", err ?? "Invalid character fields.", InfoBarSeverity.Error);
            return;
        }
        App.Platform.Repository.SaveCharacter(_selectedCharacter);

        foreach (var existing in App.Platform.Repository.GetAllPersonas())
        {
            existing.IsDefault = false;
            App.Platform.Repository.SavePersona(existing);
        }

        var persona = new Persona
        {
            Id = $"persona-playable-{_selectedCharacter.Id}",
            Name = _selectedCharacter.Name,
            Description = $"{_selectedCharacter.Title}. {_selectedCharacter.Description}".Trim(),
            Appearance = _selectedCharacter.Appearance,
            Personality = $"{_selectedCharacter.Personality} Speech style: {_selectedCharacter.SpeechStyle}".Trim(),
            Background = _selectedCharacter.Background,
            IsDefault = true
        };
        App.Platform.Repository.SavePersona(persona);

        if (App.Platform.ActiveSession != null)
        {
            App.Platform.ActiveSession.ActivePersonaId = persona.Id;
            var hostPart = App.Platform.ActiveSession.Participants.FirstOrDefault(p => p.IsHost || p.ParticipantId == "host-player");
            if (hostPart != null)
            {
                hostPart.DisplayName = persona.Name;
                hostPart.PersonaId = persona.Id;
                hostPart.PersonaName = persona.Name;
                hostPart.PersonaSummary = persona.Description;
            }
            App.Platform.Repository.SaveSession(App.Platform.ActiveSession);
        }

        App.MainWindowInstance.ShowNotification(
            "Now Playing as Character!",
            $"'{_selectedCharacter.Name}' is now set as your active Player Persona for Single-Player and Multiplayer sessions.",
            InfoBarSeverity.Success);
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) return;
        if (_allCharacters.Count <= 1)
        {
            App.MainWindowInstance.ShowNotification("Cannot Delete", "At least one character must remain in the roster.", InfoBarSeverity.Warning);
            return;
        }
        App.Platform.Repository.DeleteCharacter(_selectedCharacter.Id);
        LoadRoster();
    }

    private async void PickPortrait_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickOpenFileAsync(App.MainWindowInstance, ".png", ".jpg", ".jpeg", ".webp");
        if (!string.IsNullOrWhiteSpace(path))
        {
            PortraitPathBox.Text = path;
        }
    }

    private async void ImportCharacter_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickOpenFileAsync(App.MainWindowInstance, RoleplayFormatConstants.CharacterExtension, ".json");
        if (string.IsNullOrWhiteSpace(path)) return;

        var raw = await File.ReadAllTextAsync(path);
        var result = RoleplayFormatSerializer.ImportCharacter(raw);
        if (!result.IsValid || result.Data == null)
        {
            App.MainWindowInstance.ShowNotification("Import Validation Failed", string.Join(" ", result.Errors), InfoBarSeverity.Error);
            return;
        }

        App.Platform.Repository.SaveCharacter(result.Data);
        App.Platform.Repository.SaveLibraryItem(new LibraryItemRecord
        {
            ResourceType = "rpchar",
            Title = result.Data.Name,
            Summary = result.Data.Description,
            Tags = result.Data.Tags,
            SourceFilePath = path
        });

        LoadRoster(result.Data.Id);
        App.MainWindowInstance.ShowNotification("Character Imported", $"Imported '{result.Data.Name}' successfully.", InfoBarSeverity.Success);
    }

    private async void ExportCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) return;
        if (!PopulateCharacterFromForm(_selectedCharacter, out var err))
        {
            App.MainWindowInstance.ShowNotification("Validation Error", err ?? "Invalid fields.", InfoBarSeverity.Error);
            return;
        }

        var path = await NativePickerHelper.PickSaveFileAsync(
            App.MainWindowInstance,
            _selectedCharacter.Name.Replace(' ', '_'),
            "Aetheria Character File",
            RoleplayFormatConstants.CharacterExtension);

        if (!string.IsNullOrWhiteSpace(path))
        {
            var json = RoleplayFormatSerializer.ExportCharacter(_selectedCharacter);
            await File.WriteAllTextAsync(path, json);
            App.MainWindowInstance.ShowNotification("Character Exported", $"Saved to {path}", InfoBarSeverity.Success);
        }
    }

    private void StartWithCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) return;
        PopulateCharacterFromForm(_selectedCharacter, out _);
        App.Platform.Repository.SaveCharacter(_selectedCharacter);

        var ctx = App.Platform;
        var persona = ctx.GetDefaultPersona();
        var world = ctx.Repository.GetAllWorlds().FirstOrDefault();
        var session = ctx.Engine.InitializeSession(
            $"{_selectedCharacter.Name} — Roleplay",
            RoleplayGameMode.Freeform,
            persona,
            world,
            new[] { _selectedCharacter },
            ctx.GetDefaultModelPreset());

        ctx.Repository.SaveSession(session);
        ctx.ActiveSession = session;
        App.MainWindowInstance.NavigateToSection("roleplay");
    }

    private async void TestCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter == null) return;
        if (!PopulateCharacterFromForm(_selectedCharacter, out var err))
        {
            App.MainWindowInstance.ShowNotification("Validation Error", err ?? "Fix errors before testing.", InfoBarSeverity.Error);
            return;
        }

        var ctx = App.Platform;
        var persona = ctx.GetDefaultPersona();
        var preset = ctx.GetDefaultModelPreset();
        var tempSession = ctx.Engine.InitializeSession("Character Test Sandbox", RoleplayGameMode.Freeform, persona, null, new[] { _selectedCharacter }, preset);

        var promptInput = new TextBox
        {
            Text = "\"Hello there. Tell me about yourself and what you are working on.\"",
            Header = "Test Prompt as " + persona.Name
        };

        var outputBlock = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 200,
            Header = "Character Opening & AI Response Preview",
            Text = $"[Opening Greeting]:\n{tempSession.Messages.FirstOrDefault()?.Content ?? "(None)"}\n\nClick 'Run Test Turn' to query {preset.ProviderType} ({preset.ModelId})."
        };

        var runBtn = new Button { Content = "Run Test Turn with AI" };
        runBtn.Click += async (_, _) =>
        {
            runBtn.IsEnabled = false;
            outputBlock.Text = "Streaming test response...";
            ctx.Engine.ValidateAndApplyPlayerAction(tempSession, "host-player", RoleplayActionType.Speak, promptInput.Text, out _);
            var sb = new StringBuilder();
            await foreach (var delta in ctx.AiOrchestrator.StreamSessionResponseAsync(tempSession, _selectedCharacter, persona, null, preset))
            {
                sb.Append(delta);
                outputBlock.Text = sb.ToString();
            }
            runBtn.IsEnabled = true;
        };

        var dlg = new ContentDialog
        {
            Title = $"Test Character — {_selectedCharacter.Name}",
            Content = new StackPanel
            {
                Spacing = 12,
                Width = 500,
                Children = { promptInput, runBtn, outputBlock }
            },
            CloseButtonText = "Close Preview",
            XamlRoot = this.XamlRoot
        };

        await dlg.ShowAsync();
    }
}
