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
    private bool _suppressThemeChange;

    public SettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressThemeChange = true;
        try
        {
            var savedTheme = App.Platform.Repository.GetSetting("AppTheme", "Dark");
            ThemeCombo.SelectedIndex = savedTheme switch
            {
                "Light" => 1,
                "System" => 2,
                _ => 0
            };
        }
        finally
        {
            _suppressThemeChange = false;
        }

        DataDirPathText.Text = App.Platform.Repository.DataDirectory;
        RefreshLogs();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            var themeName = tag == "Default" ? "System" : tag;
            App.ApplyTheme(themeName);
            App.MainWindowInstance?.ShowNotification("Theme Updated", $"Switched application theme to {themeName}.", InfoBarSeverity.Informational);
        }
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
