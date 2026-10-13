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

public sealed partial class ModelsPage : Page
{
    private List<ModelPreset> _presets = new();
    private ModelPreset? _selectedPreset;
    private bool _suppressProviderDefaults;

    public ModelsPage()
    {
        this.InitializeComponent();
        this.Loaded += ModelsPage_Loaded;
    }

    private void ModelsPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadPresets();
    }

    private void LoadPresets(string? selectId = null)
    {
        _presets = App.Platform.Repository.GetAllModelPresets().ToList();
        PresetsListView.Items.Clear();

        foreach (var p in _presets)
        {
            var title = p.IsDefault ? $"★ {p.Name} (Default)" : p.Name;
            var sp = new StackPanel { Spacing = 2, Padding = new Thickness(4) };
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{p.ProviderType} • {p.ModelId}",
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            PresetsListView.Items.Add(new ListViewItem { Content = sp, Tag = p.Id });
        }

        if (_presets.Count > 0)
        {
            var idx = selectId != null ? _presets.FindIndex(p => p.Id == selectId) : 0;
            if (idx < 0) idx = 0;
            PresetsListView.SelectedIndex = idx;
        }
    }

    private void PresetsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetsListView.SelectedItem is ListViewItem item && item.Tag is string id)
        {
            _selectedPreset = _presets.FirstOrDefault(p => p.Id == id);
            if (_selectedPreset != null)
            {
                PopulateEditor(_selectedPreset);
            }
        }
    }

    private void PopulateEditor(ModelPreset p)
    {
        _suppressProviderDefaults = true;
        try
        {
            EditorHeaderTitle.Text = $"Preset: {p.Name}";
            PresetNameBox.Text = p.Name;
            EndpointUrlBox.Text = p.EndpointUrl;
            ModelIdBox.Text = p.ModelId;

            var normalizedTag = NormalizeProviderTag(p.ProviderType);
            for (int i = 0; i < ProviderCombo.Items.Count; i++)
            {
                if (ProviderCombo.Items[i] is ComboBoxItem cbi &&
                    string.Equals(NormalizeProviderTag(cbi.Tag as string ?? ""), normalizedTag, StringComparison.OrdinalIgnoreCase))
                {
                    ProviderCombo.SelectedIndex = i;
                    break;
                }
            }

            TempBox.Value = p.Temperature;
            TopPBox.Value = p.TopP;
            TopKBox.Value = p.TopK;
            RepPenBox.Value = p.RepetitionPenalty;
            MaxTokensBox.Value = p.MaxOutputTokens;
            ContextLengthBox.Value = p.ContextLength;
            StreamToggle.IsChecked = p.StreamResponses;
            DefaultPresetCheck.IsChecked = p.IsDefault;
            StopSequencesBox.Text = string.Join(", ", p.StopSequences);
            SystemPrefixBox.Text = p.SystemInstructions;
            HardwareNotesBox.Text = p.ProviderSpecificOptions.TryGetValue("hardwareNotes", out var hn) ? hn : string.Empty;

            RefreshVaultStatus(normalizedTag);
        }
        finally
        {
            _suppressProviderDefaults = false;
        }
    }

    private static string NormalizeProviderTag(string raw)
    {
        return raw switch
        {
            "GoogleGemini" => "Gemini",
            "AnthropicClaude" => "Anthropic",
            "CustomOpenAiCompatible" => "Custom",
            _ => string.IsNullOrWhiteSpace(raw) ? "Ollama" : raw
        };
    }

    private string GetSelectedProvider()
    {
        if (ProviderCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            return NormalizeProviderTag(tag);
        }
        return "Ollama";
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = GetSelectedProvider();
        RefreshVaultStatus(provider);

        if (_suppressProviderDefaults) return;

        EndpointUrlBox.Text = provider switch
        {
            "Ollama" => "http://localhost:11434",
            "KoboldCpp" => "http://localhost:5001/v1",
            "LMStudio" => "http://localhost:1234/v1",
            "OpenAI" => "https://api.openai.com/v1",
            "Gemini" => "https://generativelanguage.googleapis.com/v1beta/openai",
            "Anthropic" => "https://api.anthropic.com",
            "OpenRouter" => "https://openrouter.ai/api/v1",
            "DeepSeek" => "https://api.deepseek.com/v1",
            "Groq" => "https://api.groq.com/openai/v1",
            _ => EndpointUrlBox.Text
        };
    }

    private void RefreshVaultStatus(string provider)
    {
        var key = App.Platform.SecretVault.GetApiKey(provider);
        var masked = string.IsNullOrEmpty(key)
            ? "No API Key Stored (Not required for local Ollama/KoboldCpp/LMStudio)"
            : $"Encrypted in DPAPI Vault (••••{key[^Math.Min(4, key.Length)..]})";
        VaultStatusBadge.Text = $"DPAPI Vault Status ({provider}): {masked}";
        ApiKeyPasswordBox.Password = string.Empty;
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        var provider = GetSelectedProvider();
        var key = ApiKeyPasswordBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            App.MainWindowInstance?.ShowNotification("Empty API Key", "Enter an API key before saving to the DPAPI vault.", InfoBarSeverity.Warning);
            return;
        }

        App.Platform.SecretVault.SetApiKey(provider, key);
        RefreshVaultStatus(provider);
        App.MainWindowInstance?.ShowNotification("Credential Encrypted", $"Stored API key for {provider} in Windows DPAPI Vault.", InfoBarSeverity.Success);
    }

    private void ClearApiKey_Click(object sender, RoutedEventArgs e)
    {
        var provider = GetSelectedProvider();
        App.Platform.SecretVault.SetApiKey(provider, string.Empty);
        RefreshVaultStatus(provider);
        App.MainWindowInstance?.ShowNotification("Credential Cleared", $"Removed stored API key for {provider}.", InfoBarSeverity.Informational);
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        ReadEditorIntoPreset(_selectedPreset);

        TestConnectionBtn.IsEnabled = false;
        ConnectionStatusText.Text = $"Testing connection to {_selectedPreset.ProviderType} ({_selectedPreset.EndpointUrl})...";

        try
        {
            var result = await App.Platform.AiOrchestrator.TestPresetConnectionAsync(_selectedPreset);
            ConnectionStatusText.Text = result.Message;

            DiscoveredModelsCombo.Items.Clear();
            foreach (var modelName in result.AvailableModels)
            {
                DiscoveredModelsCombo.Items.Add(modelName);
            }

            if (result.IsConnected)
            {
                App.MainWindowInstance?.ShowNotification("Provider Connected", result.Message, InfoBarSeverity.Success);
            }
            else
            {
                App.MainWindowInstance?.ShowNotification("Provider Unreachable", result.Message, InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"Connection error: {ex.Message}";
            App.MainWindowInstance?.ShowNotification("Connection Error", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            TestConnectionBtn.IsEnabled = true;
        }
    }

    private void DiscoveredModelsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredModelsCombo.SelectedItem is string modelId && !string.IsNullOrWhiteSpace(modelId))
        {
            ModelIdBox.Text = modelId;
        }
    }

    private void ReadEditorIntoPreset(ModelPreset p)
    {
        p.Name = string.IsNullOrWhiteSpace(PresetNameBox.Text) ? "Custom Model Preset" : PresetNameBox.Text.Trim();
        p.ProviderType = GetSelectedProvider();
        p.EndpointUrl = EndpointUrlBox.Text.Trim();
        p.ModelId = ModelIdBox.Text.Trim();
        p.Temperature = double.IsNaN(TempBox.Value) ? 0.8 : TempBox.Value;
        p.TopP = double.IsNaN(TopPBox.Value) ? 0.92 : TopPBox.Value;
        p.TopK = (int)(double.IsNaN(TopKBox.Value) ? 40 : TopKBox.Value);
        p.RepetitionPenalty = double.IsNaN(RepPenBox.Value) ? 1.08 : RepPenBox.Value;
        p.MaxOutputTokens = (int)(double.IsNaN(MaxTokensBox.Value) ? 600 : MaxTokensBox.Value);
        p.ContextLength = (int)(double.IsNaN(ContextLengthBox.Value) ? 4096 : ContextLengthBox.Value);
        p.StreamResponses = StreamToggle.IsChecked == true;
        p.IsDefault = DefaultPresetCheck.IsChecked == true;
        p.StopSequences = StopSequencesBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        p.SystemInstructions = SystemPrefixBox.Text.Trim();
        p.ProviderSpecificOptions["hardwareNotes"] = HardwareNotesBox.Text.Trim();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        ReadEditorIntoPreset(_selectedPreset);

        if (_selectedPreset.IsDefault)
        {
            foreach (var other in _presets.Where(x => x.Id != _selectedPreset.Id && x.IsDefault))
            {
                other.IsDefault = false;
                App.Platform.Repository.SaveModelPreset(other);
            }
        }

        App.Platform.Repository.SaveModelPreset(_selectedPreset);
        App.MainWindowInstance?.ShowNotification("Model Preset Saved", $"Saved '{_selectedPreset.Name}'.", InfoBarSeverity.Success);
        LoadPresets(_selectedPreset.Id);
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        DefaultPresetCheck.IsChecked = true;
        SavePreset_Click(sender, e);
    }

    private void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        var np = new ModelPreset
        {
            Name = "Custom Provider Preset",
            ProviderType = "Ollama",
            EndpointUrl = "http://localhost:11434",
            ModelId = "llama3.1:8b-instruct-q4_K_M",
            Temperature = 0.82,
            TopP = 0.92,
            MaxOutputTokens = 600,
            ContextLength = 4096
        };
        App.Platform.Repository.SaveModelPreset(np);
        LoadPresets(np.Id);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        if (_presets.Count <= 1)
        {
            App.MainWindowInstance?.ShowNotification("Cannot Delete", "At least one model preset must remain.", InfoBarSeverity.Warning);
            return;
        }
        var name = _selectedPreset.Name;
        App.Platform.Repository.DeleteModelPreset(_selectedPreset.Id);
        _selectedPreset = null;
        App.MainWindowInstance?.ShowNotification("Preset Deleted", $"Removed '{name}'.", InfoBarSeverity.Informational);
        LoadPresets();
    }

    private async void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickOpenFileAsync(".rpmodel", ".json");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var result = RoleplayFormatSerializer.ImportModelPreset(json);
            if (!result.IsValid || result.Data == null)
            {
                App.MainWindowInstance?.ShowNotification("Import Validation Failed", string.Join(" ", result.Errors), InfoBarSeverity.Error);
                return;
            }

            var imported = result.Data;
            imported.Id = Guid.NewGuid().ToString("N");
            imported.IsDefault = false;
            App.Platform.Repository.SaveModelPreset(imported);
            LoadPresets(imported.Id);
            App.MainWindowInstance?.ShowNotification("Model Preset Imported", $"Imported '{imported.Name}' (Secret-Free).", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Import Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;
        ReadEditorIntoPreset(_selectedPreset);
        var safeName = string.Join("_", _selectedPreset.Name.Split(Path.GetInvalidFileNameChars()));
        var path = await NativePickerHelper.PickSaveFileAsync(safeName, ".rpmodel", "Aetheria Model Preset (Secret-Free)");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = RoleplayFormatSerializer.ExportModelPreset(_selectedPreset);
            await File.WriteAllTextAsync(path, json);
            App.MainWindowInstance?.ShowNotification("Model Preset Exported", $"Saved secret-free .rpmodel to {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Export Error", ex.Message, InfoBarSeverity.Error);
        }
    }
}
