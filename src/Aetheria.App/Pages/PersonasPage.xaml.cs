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

public sealed partial class PersonasPage : Page
{
    private List<Persona> _personas = new();
    private Persona? _selectedPersona;

    public PersonasPage()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) => LoadPersonas();
    }

    private void LoadPersonas(string? selectId = null)
    {
        _personas = App.Platform.Repository.GetAllPersonas();
        PersonasList.Items.Clear();

        int targetIndex = 0;
        for (int i = 0; i < _personas.Count; i++)
        {
            var p = _personas[i];
            var star = p.IsDefault ? "★ " : "";
            PersonasList.Items.Add(new ListViewItem
            {
                Content = $"{star}{p.Name}",
                Tag = p.Id
            });
            if (selectId != null && p.Id == selectId)
                targetIndex = i;
        }

        if (PersonasList.Items.Count > 0)
            PersonasList.SelectedIndex = targetIndex;
    }

    private void PersonasList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PersonasList.SelectedItem is ListViewItem item && item.Tag is string id)
        {
            var p = _personas.FirstOrDefault(x => x.Id == id);
            if (p != null)
            {
                _selectedPersona = p;
                PersonaNameBox.Text = p.Name;
                IsDefaultSwitch.IsOn = p.IsDefault;
                PersonaDescBox.Text = p.Description;
                PersonaAppearanceBox.Text = p.Appearance;
                PersonaPersonalityBox.Text = p.Personality;
                PersonaBackgroundBox.Text = p.Background;
            }
        }
    }

    private void SavePersona_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPersona == null) _selectedPersona = new Persona();
        var name = PersonaNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            App.MainWindowInstance.ShowNotification("Validation Error", "Persona Name cannot be empty.", InfoBarSeverity.Error);
            return;
        }

        _selectedPersona.Name = name;
        _selectedPersona.IsDefault = IsDefaultSwitch.IsOn;
        _selectedPersona.Description = PersonaDescBox.Text.Trim();
        _selectedPersona.Appearance = PersonaAppearanceBox.Text.Trim();
        _selectedPersona.Personality = PersonaPersonalityBox.Text.Trim();
        _selectedPersona.Background = PersonaBackgroundBox.Text.Trim();

        App.Platform.Repository.SavePersona(_selectedPersona);
        LoadPersonas(_selectedPersona.Id);
        App.MainWindowInstance.ShowNotification("Persona Saved", $"Saved '{_selectedPersona.Name}'.", InfoBarSeverity.Success);
    }

    private void NewPersona_Click(object sender, RoutedEventArgs e)
    {
        var p = new Persona { Name = "New Persona", Description = "A traveler ready for adventure." };
        App.Platform.Repository.SavePersona(p);
        LoadPersonas(p.Id);
    }

    private void DeletePersona_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPersona == null) return;
        if (_personas.Count <= 1)
        {
            App.MainWindowInstance.ShowNotification("Cannot Delete", "You must keep at least one persona.", InfoBarSeverity.Warning);
            return;
        }
        App.Platform.Repository.DeletePersona(_selectedPersona.Id);
        LoadPersonas();
    }

    private async void ImportPersona_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickOpenFileAsync(App.MainWindowInstance, RoleplayFormatConstants.PersonaExtension, ".json");
        if (string.IsNullOrWhiteSpace(path)) return;

        var raw = await File.ReadAllTextAsync(path);
        var res = RoleplayFormatSerializer.ImportPersona(raw);
        if (!res.IsValid || res.Data == null)
        {
            App.MainWindowInstance.ShowNotification("Import Failed", string.Join(" ", res.Errors), InfoBarSeverity.Error);
            return;
        }

        App.Platform.Repository.SavePersona(res.Data);
        LoadPersonas(res.Data.Id);
        App.MainWindowInstance.ShowNotification("Persona Imported", $"Imported '{res.Data.Name}'.", InfoBarSeverity.Success);
    }

    private async void ExportPersona_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPersona == null) return;
        var path = await NativePickerHelper.PickSaveFileAsync(
            App.MainWindowInstance,
            _selectedPersona.Name.Replace(' ', '_'),
            "Aetheria Persona File",
            RoleplayFormatConstants.PersonaExtension);

        if (!string.IsNullOrWhiteSpace(path))
        {
            await File.WriteAllTextAsync(path, RoleplayFormatSerializer.ExportPersona(_selectedPersona));
            App.MainWindowInstance.ShowNotification("Persona Exported", $"Saved to {path}", InfoBarSeverity.Success);
        }
    }
}
