using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Aetheria.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Aetheria.App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _suppressEvents;

    public SettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;
        try
        {
            var savedTheme = App.Platform.Repository.GetSetting("AppTheme", "Dark");
            ThemeCombo.SelectedIndex = savedTheme switch
            {
                "Light" => 1,
                "System" => 2,
                _ => 0
            };

            var acc = AccessibilityManager.Current;
            acc.LoadSettings();

            // Zoom ComboBox
            int zoomPct = (int)Math.Round(acc.ZoomFactor * 100);
            ZoomScaleCombo.SelectedIndex = zoomPct switch
            {
                80 => 0,
                90 => 1,
                110 => 3,
                125 => 4,
                150 => 5,
                175 => 6,
                200 => 7,
                _ => 2 // 100%
            };

            // Font Size ComboBox
            FontSizeCombo.SelectedIndex = acc.RoleplayFontSize switch
            {
                12 => 0,
                16 => 2,
                18 => 3,
                22 => 4,
                26 => 5,
                _ => 1 // 14px
            };

            // Font Family ComboBox
            FontFamilyCombo.SelectedIndex = acc.RoleplayFontFamily switch
            {
                "Dyslexic" => 1,
                "SansSerif" => 2,
                "Monospace" => 3,
                _ => 0
            };

            ExpandedLineSpacingToggle.IsOn = acc.ExpandedLineSpacing;
            HighContrastCardsToggle.IsOn = acc.HighContrastCards;
            ReduceMotionToggle.IsOn = acc.ReduceMotion;
            EnableTtsToggle.IsOn = acc.EnableTtsNarration;
        }
        finally
        {
            _suppressEvents = false;
        }

        DataDirPathText.Text = App.Platform.Repository.DataDirectory;
        RefreshLogs();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            var themeName = tag == "Default" ? "System" : tag;
            App.ApplyTheme(themeName);
            App.MainWindowInstance?.ShowNotification("Theme Updated", $"Switched application theme to {themeName}.", InfoBarSeverity.Informational);
        }
    }

    private void ZoomScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ZoomScaleCombo.SelectedItem is ComboBoxItem cbi && int.TryParse(cbi.Tag as string, out int pct))
        {
            App.MainWindowInstance?.SetZoom(pct / 100.0);
        }
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (FontSizeCombo.SelectedItem is ComboBoxItem cbi && int.TryParse(cbi.Tag as string, out int size))
        {
            AccessibilityManager.Current.SetRoleplayFontSize(size);
            App.MainWindowInstance?.ShowNotification("Font Size Updated", $"Roleplay text size adjusted to {size}px.", InfoBarSeverity.Informational);
        }
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (FontFamilyCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            AccessibilityManager.Current.SetRoleplayFontFamily(tag);
            App.MainWindowInstance?.ShowNotification("Font Family Updated", $"Roleplay typography updated to {cbi.Content}.", InfoBarSeverity.Informational);
        }
    }

    private void ExpandedLineSpacing_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AccessibilityManager.Current.SetExpandedLineSpacing(ExpandedLineSpacingToggle.IsOn);
    }

    private void HighContrastCards_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AccessibilityManager.Current.SetHighContrastCards(HighContrastCardsToggle.IsOn);
    }

    private void ReduceMotion_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AccessibilityManager.Current.SetReduceMotion(ReduceMotionToggle.IsOn);
    }

    private void EnableTts_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AccessibilityManager.Current.SetEnableTtsNarration(EnableTtsToggle.IsOn);
    }

    private void ResetAccessibility_Click(object sender, RoutedEventArgs e)
    {
        AccessibilityManager.Current.SetZoomFactor(1.0, notify: false);
        AccessibilityManager.Current.SetRoleplayFontSize(14, notify: false);
        AccessibilityManager.Current.SetRoleplayFontFamily("Default", notify: false);
        AccessibilityManager.Current.SetExpandedLineSpacing(false, notify: false);
        AccessibilityManager.Current.SetHighContrastCards(false, notify: false);
        AccessibilityManager.Current.SetReduceMotion(false, notify: false);
        AccessibilityManager.Current.SetEnableTtsNarration(true, notify: true);

        App.MainWindowInstance?.ApplyZoom(1.0, notify: false);
        SettingsPage_Loaded(this, new RoutedEventArgs());
        App.MainWindowInstance?.ShowNotification("Accessibility Reset", "Reset all visual accessibility settings to default.", InfoBarSeverity.Success);
    }

    private void CopyVulkanCmd_Click(object sender, RoutedEventArgs e)
    {
        const string cmd = "koboldcpp.exe --model model-8b-q4_k_m.gguf --usevulkan --gpulayers 33 --threads 10 --blasthreads 10 --contextsize 4096 --port 5001";
        var dp = new DataPackage();
        dp.SetText(cmd);
        Clipboard.SetContent(dp);
        App.MainWindowInstance?.ShowNotification("Vulkan Command Copied", "Copied optimal RX 580 8GB + Xeon E5-2690 v3 KoboldCpp command to clipboard.", InfoBarSeverity.Success);
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = App.Platform.Repository.DataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Error Opening Folder", ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void BackupDatabase_Click(object sender, RoutedEventArgs e)
    {
        var path = await NativePickerHelper.PickSaveFileAsync($"aetheria_backup_{DateTime.Now:yyyyMMdd}", ".db", "SQLite Database Backup");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var srcPath = Path.Combine(App.Platform.Repository.DataDirectory, "aetheria_platform.db");
            File.Copy(srcPath, path, overwrite: true);
            App.MainWindowInstance?.ShowNotification("Database Backed Up", $"Saved SQLite database backup to {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Backup Failed", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void RefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogs();
    }

    private void RefreshLogs()
    {
        LogsListView.Items.Clear();
        foreach (var log in App.Platform.GetDiagnosticLogs().Reverse())
        {
            LogsListView.Items.Add(log);
        }
    }

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, App.Platform.GetDiagnosticLogs());
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        App.MainWindowInstance?.ShowNotification("Logs Copied", "Copied diagnostic log history to clipboard.", InfoBarSeverity.Success);
    }
}
