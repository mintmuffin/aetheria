using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aetheria.Core.Services;
using Microsoft.UI.Xaml;

namespace Aetheria.App;

public partial class App : Application
{
    public static MainWindow MainWindowInstance { get; internal set; } = null!;
    public static PlatformContext Platform { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aetheria");
        Directory.CreateDirectory(logDir);

        try
        {
            Platform = new PlatformContext();

            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();
            MainWindowInstance.InitializeDefaultNavigation();

            // Apply stored theme (Dark default)
            var savedTheme = Platform.Repository.GetSetting("AppTheme", "Dark");
            ApplyTheme(savedTheme);

            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
            {
                var sections = new[] { "home", "roleplay", "characters", "personas", "worlds", "models", "multiplayer", "library", "settings" };
                foreach (var sec in sections)
                {
                    MainWindowInstance.NavigateToSection(sec);
                    await Task.Delay(120);
                }
                MainWindowInstance.NavigateToSection("home");
                await Task.Delay(200);
                File.WriteAllText(Path.Combine(Platform.Repository.DataDirectory, "smoke_test_ok.txt"),
                    $"Aetheria WinUI 3 Smoke Test Passed at {DateTime.Now:O} across all 9 pages.");
                MainWindowInstance.Close();
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(logDir, "startup_error.log"), ex.ToString());
            Environment.Exit(1);
        }
    }

    public static void ApplyTheme(string themeName)
    {
        if (MainWindowInstance?.Content is FrameworkElement root)
        {
            root.RequestedTheme = themeName switch
            {
                "Light" => ElementTheme.Light,
                "System" => ElementTheme.Default,
                _ => ElementTheme.Dark
            };
        }
        Platform?.Repository.SetSetting("AppTheme", themeName);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aetheria");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "runtime_error.log"),
                $"[{DateTime.Now:O}] {e.Message}\n{e.Exception}\n\n");
        }
        catch
        {
        }

        // Only mark handled if MainWindow is already visible; otherwise exit so we never hang in background
        if (MainWindowInstance != null)
        {
            e.Handled = true;
            Platform?.Log($"Unhandled UI Exception: {e.Message}");
        }
    }
}
