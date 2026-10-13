using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Aetheria.App.Helpers;
using Aetheria.Core.Domain;
using Aetheria.Core.Formats;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aetheria.App.Pages;

public sealed partial class LibraryPage : Page
{
    private List<World> _worlds = new();
    private List<Character> _chars = new();
    private List<Persona> _personas = new();
    private List<ModelPreset> _models = new();
    private List<string> _allCatalogEntries = new();

    public LibraryPage()
    {
        this.InitializeComponent();
        this.Loaded += LibraryPage_Loaded;
    }

    private void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshLibrary();
    }

    private void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        _worlds = App.Platform.Repository.GetAllWorlds().ToList();
        _chars = App.Platform.Repository.GetAllCharacters().ToList();
        _personas = App.Platform.Repository.GetAllPersonas().ToList();
        _models = App.Platform.Repository.GetAllModelPresets().ToList();

        BundleWorldsList.Items.Clear();
        foreach (var w in _worlds)
            BundleWorldsList.Items.Add(new ListViewItem { Content = $"{w.Name} ({w.Genre})", Tag = w.Id });
        if (BundleWorldsList.Items.Count > 0) BundleWorldsList.SelectedIndex = 0;

        BundleCharsList.Items.Clear();
        foreach (var c in _chars)
            BundleCharsList.Items.Add(new ListViewItem { Content = $"{c.Name} — {c.Title}", Tag = c.Id });
        if (BundleCharsList.Items.Count > 0) BundleCharsList.SelectedIndex = 0;

        BundlePersonasList.Items.Clear();
        foreach (var p in _personas)
            BundlePersonasList.Items.Add(new ListViewItem { Content = p.Name, Tag = p.Id });
        if (BundlePersonasList.Items.Count > 0) BundlePersonasList.SelectedIndex = 0;

        BundleModelsList.Items.Clear();
        foreach (var m in _models)
            BundleModelsList.Items.Add(new ListViewItem { Content = $"{m.Name} ({m.ProviderType})", Tag = m.Id });
        if (BundleModelsList.Items.Count > 0) BundleModelsList.SelectedIndex = 0;

        _allCatalogEntries.Clear();
        foreach (var w in _worlds)
            _allCatalogEntries.Add($"[World .rpworld] {w.Name} — {w.Genre} ({w.Locations.Count} locations, {w.LoreEntries.Count} lore)");
        foreach (var c in _chars)
            _allCatalogEntries.Add($"[Character .rpchar] {c.Name} — {c.Title} (v{c.Version})");
        foreach (var p in _personas)
            _allCatalogEntries.Add($"[Persona .rppersona] {p.Name} — {p.Description}");
        foreach (var m in _models)
            _allCatalogEntries.Add($"[Model Preset .rpmodel] {m.Name} — {m.ProviderType} ({m.ModelId})");

        var samplesDir = Path.Combine(App.Platform.Repository.DataDirectory, "Samples");
        if (Directory.Exists(samplesDir))
        {
            foreach (var file in Directory.GetFiles(samplesDir))
            {
                var fi = new FileInfo(file);
                _allCatalogEntries.Add($"[Sample File] {fi.Name} ({fi.Length / 1024.0:F1} KB)");
            }
        }

        ApplyCatalogFilter();
    }

    private void CatalogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter()
    {
        var query = CatalogSearchBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(query)
            ? _allCatalogEntries
            : _allCatalogEntries.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        CatalogListView.Items.Clear();
        foreach (var item in filtered)
        {
            CatalogListView.Items.Add(item);
        }
    }

    private void OpenSamplesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Platform.EnsureSampleFilesExported();
            var samplesDir = Path.Combine(App.Platform.Repository.DataDirectory, "Samples");
            Process.Start(new ProcessStartInfo
            {
                FileName = samplesDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Could Not Open Folder", ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void UniversalImport_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickOpenFileAsync(".rpack", ".rpchar", ".rpworld", ".rppersona", ".rpmodel", ".rpsession", ".json");
        if (string.IsNullOrEmpty(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".rpack")
            {
                var res = RoleplayFormatSerializer.ImportBundleFromFile(path);
                FormatValidationReport(path, "rpack", res.IsValid, res.Errors, res.Warnings, res.MigratedFromVersion);
                if (!res.IsValid || res.Data == null)
                {
                    App.MainWindowInstance?.ShowNotification("Bundle Validation Failed", string.Join(" ", res.Errors), InfoBarSeverity.Error);
                    return;
                }

                var bundle = res.Data;
                foreach (var w in bundle.Worlds) { w.Id = Guid.NewGuid().ToString("N"); App.Platform.Repository.SaveWorld(w); }
                foreach (var c in bundle.Characters) { c.Id = Guid.NewGuid().ToString("N"); App.Platform.Repository.SaveCharacter(c); }
                foreach (var p in bundle.Personas) { p.Id = Guid.NewGuid().ToString("N"); p.IsDefault = false; App.Platform.Repository.SavePersona(p); }
                foreach (var m in bundle.ModelPresets) { m.Id = Guid.NewGuid().ToString("N"); m.IsDefault = false; App.Platform.Repository.SaveModelPreset(m); }

                RefreshLibrary();
                App.MainWindowInstance?.ShowNotification(
                    "Bundle Imported (.rpack)",
                    $"Imported '{bundle.Title}': {bundle.Worlds.Count} worlds, {bundle.Characters.Count} characters, {bundle.Personas.Count} personas, {bundle.ModelPresets.Count} model presets.",
                    InfoBarSeverity.Success);
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            if (ext == ".rpchar")
            {
                var res = RoleplayFormatSerializer.ImportCharacter(json);
                FormatValidationReport(path, "rpchar", res.IsValid, res.Errors, res.Warnings, res.MigratedFromVersion);
                if (res.IsValid && res.Data != null)
                {
                    var c = res.Data;
                    c.Id = Guid.NewGuid().ToString("N");
                    App.Platform.Repository.SaveCharacter(c);
                    RefreshLibrary();
                    App.MainWindowInstance?.ShowNotification("Character Imported", $"Imported '{c.Name}'.", InfoBarSeverity.Success);
                }
            }
            else if (ext == ".rpworld")
            {
                var res = RoleplayFormatSerializer.ImportWorld(json);
                FormatValidationReport(path, "rpworld", res.IsValid, res.Errors, res.Warnings, res.MigratedFromVersion);
                if (res.IsValid && res.Data != null)
                {
                    var w = res.Data;
                    w.Id = Guid.NewGuid().ToString("N");
                    App.Platform.Repository.SaveWorld(w);
                    RefreshLibrary();
                    App.MainWindowInstance?.ShowNotification("World Imported", $"Imported '{w.Name}'.", InfoBarSeverity.Success);
                }
            }
            else if (ext == ".rppersona")
            {
                var res = RoleplayFormatSerializer.ImportPersona(json);
                FormatValidationReport(path, "rppersona", res.IsValid, res.Errors, res.Warnings, res.MigratedFromVersion);
                if (res.IsValid && res.Data != null)
                {
                    var p = res.Data;
                    p.Id = Guid.NewGuid().ToString("N");
                    p.IsDefault = false;
                    App.Platform.Repository.SavePersona(p);
                    RefreshLibrary();
                    App.MainWindowInstance?.ShowNotification("Persona Imported", $"Imported '{p.Name}'.", InfoBarSeverity.Success);
                }
            }
            else if (ext == ".rpmodel")
            {
                var res = RoleplayFormatSerializer.ImportModelPreset(json);
                FormatValidationReport(path, "rpmodel", res.IsValid, res.Errors, res.Warnings, res.MigratedFromVersion);
                if (res.IsValid && res.Data != null)
                {
                    var m = res.Data;
                    m.Id = Guid.NewGuid().ToString("N");
                    m.IsDefault = false;
                    App.Platform.Repository.SaveModelPreset(m);
                    RefreshLibrary();
                    App.MainWindowInstance?.ShowNotification("Model Preset Imported", $"Imported '{m.Name}'.", InfoBarSeverity.Success);
                }
            }
            else if (ext == ".rpsession")
            {
                var res = RoleplayFormatSerializer.ImportSession(json);
                FormatValidationReport(path, "rpsession", res.IsValid, res.Errors, res.Warnings, res.MigratedFromVersion);
                if (res.IsValid && res.Data != null)
                {
                    var s = res.Data;
                    s.SessionId = Guid.NewGuid().ToString("N");
                    App.Platform.Repository.SaveSession(s);
                    App.Platform.ActiveSession = s;
                    RefreshLibrary();
                    App.MainWindowInstance?.ShowNotification("Session Imported", $"Imported '{s.Title}'.", InfoBarSeverity.Success);
                }
            }
        }
        catch (Exception ex)
        {
            ValidationReportText.Text = $"Import Exception: {ex.Message}";
            App.MainWindowInstance?.ShowNotification("Import Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void FormatValidationReport(string path, string resourceType, bool isValid, List<string> errors, List<string> warnings, string? migratedFrom)
    {
        var lines = new List<string>
        {
            $"File: {Path.GetFileName(path)}",
            $"Status: {(isValid ? "VALID (Passed Schema & Security Checks)" : "INVALID")}",
            $"Resource Type: {resourceType} • Schema Version: 1.0 {(migratedFrom != null ? $"(Migrated from {migratedFrom})" : "")}"
        };
        if (warnings.Count > 0)
            lines.Add($"Warnings / Migrations: {string.Join("; ", warnings)}");
        if (errors.Count > 0)
            lines.Add($"Errors: {string.Join("; ", errors)}");

        ValidationReportText.Text = string.Join("\n", lines);
    }

    private async void ExportBundle_Click(object sender, RoutedEventArgs e)
    {
        var selectedWorldIds = BundleWorldsList.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag as string).Where(id => id != null).ToHashSet();
        var selectedCharIds = BundleCharsList.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag as string).Where(id => id != null).ToHashSet();
        var selectedPersonaIds = BundlePersonasList.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag as string).Where(id => id != null).ToHashSet();
        var selectedModelIds = BundleModelsList.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag as string).Where(id => id != null).ToHashSet();

        var manifest = new ResourceBundleManifest
        {
            Title = string.IsNullOrWhiteSpace(BundleTitleBox.Text) ? "Aetheria Bundle" : BundleTitleBox.Text.Trim(),
            Author = BundleAuthorBox.Text.Trim(),
            Description = BundleDescBox.Text.Trim(),
            Tags = BundleTagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Worlds = _worlds.Where(w => selectedWorldIds.Contains(w.Id)).ToList(),
            Characters = _chars.Where(c => selectedCharIds.Contains(c.Id)).ToList(),
            Personas = _personas.Where(p => selectedPersonaIds.Contains(p.Id)).ToList(),
            ModelPresets = _models.Where(m => selectedModelIds.Contains(m.Id)).ToList()
        };

        var safeName = string.Join("_", manifest.Title.Split(Path.GetInvalidFileNameChars()));
        var path = await NativePickerHelper.PickSaveFileAsync(safeName, ".rpack", "Aetheria Resource Bundle Archive");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            RoleplayFormatSerializer.ExportBundleToFile(path, manifest);
            ValidationReportText.Text = $"Exported .rpack Bundle to {path}\nIncluded: {manifest.Worlds.Count} worlds, {manifest.Characters.Count} characters, {manifest.Personas.Count} personas, {manifest.ModelPresets.Count} secret-free model presets.";
            App.MainWindowInstance?.ShowNotification("Bundle Exported (.rpack)", $"Saved to {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Bundle Export Error", ex.Message, InfoBarSeverity.Error);
        }
    }
}
