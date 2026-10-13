using System;
using System.Linq;
using Aetheria.App.Helpers;
using Aetheria.App.Pages;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Aetheria.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        App.MainWindowInstance = this;
        this.InitializeComponent();

        // Enable Windows 11 Mica Backdrop if supported
        try
        {
            if (MicaController.IsSupported())
            {
                this.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                this.SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        }
        catch
        {
            // Fallback to solid theme background if backdrop controller is unavailable
        }

        // Native Windows App SDK Title Bar Integration
        try
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
        }
        catch
        {
        }

        ConfigureWindowSize();

        if (this.Content is UIElement rootEl)
        {
            rootEl.KeyDown += MainWindow_KeyDown;
        }

        AccessibilityManager.Current.AccessibilitySettingsChanged += () =>
        {
            ApplyZoom(AccessibilityManager.Current.ZoomFactor, notify: false);
        };
        ApplyZoom(AccessibilityManager.Current.ZoomFactor, notify: false);
    }

    public void InitializeDefaultNavigation()
    {
        if (MainNavView.MenuItems.Count > 0)
        {
            MainNavView.SelectedItem = MainNavView.MenuItems[0];
        }
    }

    private void ConfigureWindowSize()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                var workArea = displayArea.WorkArea;
                int targetW = Math.Min(1360, workArea.Width - 32);
                int targetH = Math.Min(820, workArea.Height - 32);
                int posX = workArea.X + Math.Max(0, (workArea.Width - targetW) / 2);
                int posY = workArea.Y + Math.Max(0, (workArea.Height - targetH) / 2);

                appWindow.MoveAndResize(new RectInt32(posX, posY, targetW, targetH));
                appWindow.Show();
            }
        }
        catch
        {
            // Ignore in headless environments
        }
    }

    public void NavigateToSection(string tag)
    {
        var targetItem = FindNavItem(MainNavView.MenuItems, tag) ??
                         FindNavItem(MainNavView.FooterMenuItems, tag);
        if (targetItem != null)
        {
            MainNavView.SelectedItem = targetItem;
        }
    }

    private static NavigationViewItem? FindNavItem(System.Collections.Generic.IList<object> items, string tag)
    {
        foreach (var item in items)
        {
            if (item is NavigationViewItem nvi)
            {
                if (string.Equals(nvi.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                    return nvi;
                if (nvi.MenuItems.Count > 0)
                {
                    var child = FindNavItem(nvi.MenuItems, tag);
                    if (child != null) return child;
                }
            }
        }
        return null;
    }

    public void ShowNotification(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            GlobalInfoBar.Title = title;
            GlobalInfoBar.Message = message;
            GlobalInfoBar.Severity = severity;
            GlobalInfoBar.IsOpen = true;
        });
    }

    public void UpdateHeaderBadge(string statusText)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            HeaderStatusBadge.Text = statusText;
        });
    }

    private void MainNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;

        Type pageType = tag switch
        {
            "home" => typeof(HomePage),
            "roleplay" => typeof(RoleplayPage),
            "characters" => typeof(CharactersPage),
            "personas" => typeof(PersonasPage),
            "worlds" => typeof(WorldsPage),
            "models" => typeof(ModelsPage),
            "multiplayer" => typeof(MultiplayerPage),
            "library" => typeof(LibraryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
        else if (ContentFrame.Content is Pages.RoleplayPage rpPage)
        {
            rpPage.RefreshFromNavigation();
        }
    }

    private void TitleBarDice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && int.TryParse(item.Tag as string, out int sides))
        {
            var roll = Random.Shared.Next(1, sides + 1);
            ShowNotification("🎲 Tabletop Die Rolled", $"Result: Rolled d{sides} -> Total: {roll}", InfoBarSeverity.Informational);
        }
    }

    private void MainWindow_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        if (ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            if (e.Key == Windows.System.VirtualKey.Add || (int)e.Key == 187) // + or =
            {
                SetZoom(AccessibilityManager.Current.ZoomFactor + 0.1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Subtract || (int)e.Key == 189) // -
            {
                SetZoom(AccessibilityManager.Current.ZoomFactor - 0.1);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Number0 || (int)e.Key == 48) // 0
            {
                SetZoom(1.0);
                e.Handled = true;
            }
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(AccessibilityManager.Current.ZoomFactor + 0.1);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(AccessibilityManager.Current.ZoomFactor - 0.1);
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
    }

    public void SetZoom(double factor, bool notify = true)
    {
        AccessibilityManager.Current.SetZoomFactor(factor, notify: true);
        ApplyZoom(AccessibilityManager.Current.ZoomFactor, notify);
    }

    public void ApplyZoom(double factor, bool notify = true)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ZoomLabelText != null)
            {
                ZoomLabelText.Text = $"{(int)Math.Round(factor * 100)}%";
            }
            if (notify)
            {
                ShowNotification("Zoom Updated", $"Interface display zoom set to {(int)Math.Round(factor * 100)}%", InfoBarSeverity.Informational);
            }
        });
    }
}
