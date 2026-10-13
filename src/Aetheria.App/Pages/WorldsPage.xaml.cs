using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aetheria.App.Helpers;
using Aetheria.Core.Domain;
using Aetheria.Core.Formats;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aetheria.App.Pages;

public sealed partial class WorldsPage : Page
{
    private List<World> _allWorlds = new();
    private List<World> _filteredWorlds = new();
    private World? _selectedWorld;
    private LoreEntry? _selectedLoreEntry;

    public WorldsPage()
    {
        this.InitializeComponent();
        this.Loaded += WorldsPage_Loaded;
    }

    private void WorldsPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadWorlds();
    }

    private void LoadWorlds(string? selectId = null)
    {
        _allWorlds = App.Platform.Repository.GetAllWorlds().ToList();
        ApplyFilter(selectId);
    }

    private void ApplyFilter(string? selectId = null)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        _filteredWorlds = string.IsNullOrEmpty(query)
            ? _allWorlds.ToList()
            : _allWorlds.Where(w =>
                w.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Genre.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Tone.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        WorldsListView.Items.Clear();
        foreach (var w in _filteredWorlds)
        {
            var sp = new StackPanel { Spacing = 2, Padding = new Thickness(4) };
            sp.Children.Add(new TextBlock
            {
                Text = w.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{w.Genre} • {w.Locations.Count} locations, {w.LoreEntries.Count} lore",
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            WorldsListView.Items.Add(new ListViewItem { Content = sp, Tag = w.Id });
        }

        if (_filteredWorlds.Count > 0)
        {
            var idx = selectId != null ? _filteredWorlds.FindIndex(w => w.Id == selectId) : 0;
            if (idx < 0) idx = 0;
            WorldsListView.SelectedIndex = idx;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(_selectedWorld?.Id);
    }

    private void WorldsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorldsListView.SelectedItem is ListViewItem item && item.Tag is string id)
        {
            _selectedWorld = _filteredWorlds.FirstOrDefault(w => w.Id == id);
            if (_selectedWorld != null)
            {
                PopulateEditor(_selectedWorld);
            }
        }
    }

    private void PopulateEditor(World w)
    {
        EditorHeaderTitle.Text = $"Editing World: {w.Name}";
        VersionMetaText.Text = $"Version {w.Version} • Updated {w.UpdatedAt.ToLocalTime():g}";

        NameBox.Text = w.Name;
        GenreBox.Text = w.Genre;
        ToneBox.Text = w.Tone;
        SettingSummaryBox.Text = w.Setting;
        NarrativeRulesBox.Text = w.NarrativeRules;
        CustomMechanicsBox.Text = w.CustomRulesAndMechanics;
        WorldHistoryBox.Text = w.WorldHistory;
        StartingConditionsBox.Text = w.ScenarioStartingConditions;

        RefreshSubLists(w);
    }

    private void RefreshSubLists(World w)
    {
        LoreEntriesListView.Items.Clear();
        foreach (var lore in w.LoreEntries)
        {
            var badge = lore.IsConstant ? "[Constant]" : $"[Keys: {string.Join(", ", lore.Keys)}]";
            LoreEntriesListView.Items.Add(new ListViewItem
            {
                Content = $"{lore.Title} {badge} (P:{lore.PriorityOrder}) — {lore.Content}",
                Tag = lore.Id
            });
        }

        LocationsListView.Items.Clear();
        foreach (var loc in w.Locations)
        {
            LocationsListView.Items.Add(new ListViewItem
            {
                Content = $"{loc.Name} ({loc.Region}) — {loc.Description}",
                Tag = loc.Id
            });
        }

        FactionsListView.Items.Clear();
        foreach (var f in w.Factions)
        {
            FactionsListView.Items.Add(new ListViewItem
            {
                Content = $"{f.Name} [{f.AlignmentOrDisposition}] — {f.Description}",
                Tag = f.Id
            });
        }

        StateVarsListView.Items.Clear();
        foreach (var kv in w.WorldStateVariables)
        {
            StateVarsListView.Items.Add(new ListViewItem
            {
                Content = $"{kv.Key} = {kv.Value}",
                Tag = kv.Key
            });
        }
    }

    private void LoreEntriesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedWorld == null) return;
        if (LoreEntriesListView.SelectedItem is ListViewItem item && item.Tag is string loreId)
        {
            _selectedLoreEntry = _selectedWorld.LoreEntries.FirstOrDefault(l => l.Id == loreId);
            if (_selectedLoreEntry != null)
            {
                LoreTitleBox.Text = _selectedLoreEntry.Title;
                LoreKeywordsBox.Text = string.Join(", ", _selectedLoreEntry.Keys);
                LorePriorityBox.Value = _selectedLoreEntry.PriorityOrder;
                LoreConstantCheck.IsChecked = _selectedLoreEntry.IsConstant;
                LoreContentBox.Text = _selectedLoreEntry.Content;
            }
        }
    }

    private void AddOrUpdateLore_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        var title = LoreTitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var existing = _selectedWorld.LoreEntries.FirstOrDefault(l => l.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new LoreEntry { Title = title };
            _selectedWorld.LoreEntries.Add(existing);
        }

        existing.Keys = LoreKeywordsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        existing.PriorityOrder = (int)(double.IsNaN(LorePriorityBox.Value) ? 10 : LorePriorityBox.Value);
        existing.IsConstant = LoreConstantCheck.IsChecked == true;
        existing.Content = LoreContentBox.Text.Trim();

        RefreshSubLists(_selectedWorld);
    }

    private void RemoveLore_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        if (LoreEntriesListView.SelectedItem is ListViewItem item && item.Tag is string loreId)
        {
            _selectedWorld.LoreEntries.RemoveAll(l => l.Id == loreId);
            RefreshSubLists(_selectedWorld);
        }
    }

    private void AddLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        var name = LocNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _selectedWorld.Locations.Add(new WorldLocation
        {
            Name = name,
            Region = LocRegionBox.Text.Trim(),
            Description = LocDescBox.Text.Trim()
        });

        LocNameBox.Text = string.Empty;
        LocRegionBox.Text = string.Empty;
        LocDescBox.Text = string.Empty;
        RefreshSubLists(_selectedWorld);
    }

    private void RemoveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        if (LocationsListView.SelectedItem is ListViewItem item && item.Tag is string locId)
        {
            _selectedWorld.Locations.RemoveAll(l => l.Id == locId);
            RefreshSubLists(_selectedWorld);
        }
    }

    private void AddFaction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        var name = FactionNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _selectedWorld.Factions.Add(new WorldFaction
        {
            Name = name,
            AlignmentOrDisposition = FactionAlignmentBox.Text.Trim(),
            Description = FactionDescBox.Text.Trim()
        });

        FactionNameBox.Text = string.Empty;
        FactionAlignmentBox.Text = string.Empty;
        FactionDescBox.Text = string.Empty;
        RefreshSubLists(_selectedWorld);
    }

    private void RemoveFaction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        if (FactionsListView.SelectedItem is ListViewItem item && item.Tag is string fId)
        {
            _selectedWorld.Factions.RemoveAll(f => f.Id == fId);
            RefreshSubLists(_selectedWorld);
        }
    }

    private void SetWorldVar_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        var key = VarKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key)) return;

        _selectedWorld.WorldStateVariables[key] = VarValBox.Text.Trim();
        VarKeyBox.Text = string.Empty;
        VarValBox.Text = string.Empty;
        RefreshSubLists(_selectedWorld);
    }

    private void RemoveWorldVar_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        if (StateVarsListView.SelectedItem is ListViewItem item && item.Tag is string key)
        {
            _selectedWorld.WorldStateVariables.Remove(key);
            RefreshSubLists(_selectedWorld);
        }
    }

    private void ReadEditorIntoWorld(World w)
    {
        w.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled World" : NameBox.Text.Trim();
        w.Genre = GenreBox.Text.Trim();
        w.Tone = ToneBox.Text.Trim();
        w.Setting = SettingSummaryBox.Text.Trim();
        w.NarrativeRules = NarrativeRulesBox.Text.Trim();
        w.CustomRulesAndMechanics = CustomMechanicsBox.Text.Trim();
        w.WorldHistory = WorldHistoryBox.Text.Trim();
        w.ScenarioStartingConditions = StartingConditionsBox.Text.Trim();
        w.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void SaveWorld_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        ReadEditorIntoWorld(_selectedWorld);
        App.Platform.Repository.SaveWorld(_selectedWorld);
        App.MainWindowInstance?.ShowNotification("World Saved", $"Saved '{_selectedWorld.Name}'.", InfoBarSeverity.Success);
        LoadWorlds(_selectedWorld.Id);
    }

    private void NewWorld_Click(object sender, RoutedEventArgs e)
    {
        var nw = new World
        {
            Name = "New Roleplay World",
            Genre = "Speculative Fiction",
            Tone = "Immersive, Dynamic",
            Setting = "Describe the overarching world, atmosphere, and core conflict.",
            ScenarioStartingConditions = "The story opens at a pivotal crossroads..."
        };
        App.Platform.Repository.SaveWorld(nw);
        LoadWorlds(nw.Id);
    }

    private void DuplicateWorld_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        ReadEditorIntoWorld(_selectedWorld);
        var json = RoleplayFormatSerializer.ExportWorld(_selectedWorld);
        var res = RoleplayFormatSerializer.ImportWorld(json);
        if (res.IsValid && res.Data != null)
        {
            var copy = res.Data;
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = $"{_selectedWorld.Name} (Copy)";
            App.Platform.Repository.SaveWorld(copy);
            LoadWorlds(copy.Id);
        }
    }

    private void DeleteWorld_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        var name = _selectedWorld.Name;
        App.Platform.Repository.DeleteWorld(_selectedWorld.Id);
        _selectedWorld = null;
        App.MainWindowInstance?.ShowNotification("World Deleted", $"Removed '{name}'.", InfoBarSeverity.Informational);
        LoadWorlds();
    }

    private async void ImportWorld_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickOpenFileAsync(".rpworld", ".json");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var result = RoleplayFormatSerializer.ImportWorld(json);
            if (!result.IsValid || result.Data == null)
            {
                App.MainWindowInstance?.ShowNotification("Import Validation Failed", string.Join(" ", result.Errors), InfoBarSeverity.Error);
                return;
            }

            var imported = result.Data;
            imported.Id = Guid.NewGuid().ToString("N");
            App.Platform.Repository.SaveWorld(imported);
            LoadWorlds(imported.Id);
            App.MainWindowInstance?.ShowNotification("World Imported", $"Imported '{imported.Name}' ({imported.Locations.Count} locations, {imported.LoreEntries.Count} lore entries).", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Import Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void ExportWorld_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorld == null) return;
        ReadEditorIntoWorld(_selectedWorld);
        var safeName = string.Join("_", _selectedWorld.Name.Split(Path.GetInvalidFileNameChars()));
        var path = await NativePickerHelper.PickSaveFileAsync(safeName, ".rpworld", "Aetheria World Package");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = RoleplayFormatSerializer.ExportWorld(_selectedWorld);
            await File.WriteAllTextAsync(path, json);
            App.MainWindowInstance?.ShowNotification("World Exported", $"Saved to {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Export Error", ex.Message, InfoBarSeverity.Error);
        }
    }
}
