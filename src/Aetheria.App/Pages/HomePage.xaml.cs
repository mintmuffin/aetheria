using System.Linq;
using Aetheria.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aetheria.App.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) => RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        var ctx = App.Platform;
        var persona = ctx.GetDefaultPersona();
        var preset = ctx.GetDefaultModelPreset();
        var chars = ctx.Repository.GetAllCharacters();
        var worlds = ctx.Repository.GetAllWorlds();
        var sessions = ctx.Repository.GetAllSessions();

        MetricPersonaText.Text = persona.Name;
        MetricModelText.Text = $"{preset.ProviderType} ({preset.ModelId})";
        MetricCountsText.Text = $"{chars.Count} Characters • {worlds.Count} Worlds";

        if (ctx.ActiveHostServer?.IsRunning == true)
            MetricNetworkText.Text = $"Hosting (Port {ctx.ActiveHostServer.ListeningPort})";
        else if (ctx.ActiveClient?.IsConnected == true)
            MetricNetworkText.Text = "Connected to Host";
        else
            MetricNetworkText.Text = "Single-Player / Local";

        // Populate Recent Sessions (up to 12 so all 8 Couple & Co-Op scenarios display)
        RecentSessionsContainer.Children.Clear();
        foreach (var s in sessions.Take(12))
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel { Spacing = 2 };
            infoStack.Children.Add(new TextBlock { Text = s.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Mode: {s.GameMode} • {s.Messages.Count} messages • Location: {s.CurrentLocation}",
                FontSize = 12,
                Opacity = 0.75
            });
            Grid.SetColumn(infoStack, 0);

            var openBtn = new Button { Content = "Play" };
            var capturedSession = s;
            openBtn.Click += (_, _) =>
            {
                ctx.ActiveSession = capturedSession;
                App.MainWindowInstance.NavigateToSection("roleplay");
            };
            Grid.SetColumn(openBtn, 1);

            var delBtn = new Button { Content = "Delete" };
            delBtn.Click += (_, _) =>
            {
                ctx.Repository.DeleteSession(capturedSession.SessionId);
                var remaining = ctx.Repository.GetAllSessions();
                if (ctx.ActiveSession?.SessionId == capturedSession.SessionId)
                    ctx.ActiveSession = remaining.FirstOrDefault();
                RefreshDashboard();
            };
            Grid.SetColumn(delBtn, 2);

            row.Children.Add(infoStack);
            row.Children.Add(openBtn);
            row.Children.Add(delBtn);

            RecentSessionsContainer.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Child = row
            });
        }

        // Populate Characters Quick Launch with Couple RP & Co-Op PvE buttons
        CharactersQuickContainer.Children.Clear();
        foreach (var c in chars)
        {
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(10, 8, 10, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel { Spacing = 2 };
            infoStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(c.Title) ? c.Name : $"{c.Name} — {c.Title}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{c.Category} • {c.Description}",
                FontSize = 12,
                Opacity = 0.75,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(infoStack, 0);

            var capturedChar = c;
            var coupleBtn = new Button { Content = "Couple RP" };
            coupleBtn.Click += (_, _) => StartNewSessionWithCharacter(capturedChar, RoleplayGameMode.TwoPersonPrivate);
            Grid.SetColumn(coupleBtn, 1);

            var coopBtn = new Button { Content = "Co-Op PvE" };
            coopBtn.Click += (_, _) => StartNewSessionWithCharacter(capturedChar, RoleplayGameMode.CooperativePvE);
            Grid.SetColumn(coopBtn, 2);

            row.Children.Add(infoStack);
            row.Children.Add(coupleBtn);
            row.Children.Add(coopBtn);

            CharactersQuickContainer.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Child = row
            });
        }
    }

    private void StartNewSessionWithCharacter(Character character, RoleplayGameMode mode)
    {
        var ctx = App.Platform;
        var persona = ctx.GetDefaultPersona();
        var world = ctx.Repository.GetAllWorlds().FirstOrDefault(w => w.CharacterRosterIds.Contains(character.Id))
                    ?? ctx.Repository.GetAllWorlds().FirstOrDefault();
        var preset = ctx.GetDefaultModelPreset();

        var modeTag = mode == RoleplayGameMode.TwoPersonPrivate ? "Couple RP" : "Co-Op PvE";
        var session = ctx.Engine.InitializeSession(
            $"[{modeTag}] {character.Name} & {persona.Name}",
            mode,
            persona,
            world,
            new[] { character },
            preset);
        session.AllowAiInTwoPersonMode = true;
        if (!string.IsNullOrWhiteSpace(character.StartingLocation))
            session.CurrentLocation = character.StartingLocation;

        ctx.Repository.SaveSession(session);
        ctx.ActiveSession = session;
        App.MainWindowInstance.NavigateToSection("roleplay");
    }

    private void RestoreScenarios_Click(object sender, RoutedEventArgs e)
    {
        App.Platform.EnsureDefaultScenariosSeeded(forceReset: true);
        App.Platform.EnsureActiveSessionLoaded();
        RefreshDashboard();
    }

    private void ResumeSession_Click(object sender, RoutedEventArgs e) =>
        App.MainWindowInstance.NavigateToSection("roleplay");

    private void NewFreeform_Click(object sender, RoutedEventArgs e)
    {
        var firstChar = App.Platform.Repository.GetAllCharacters().FirstOrDefault();
        if (firstChar != null) StartNewSessionWithCharacter(firstChar, RoleplayGameMode.CooperativePvE);
    }

    private void NewTurnBased_Click(object sender, RoutedEventArgs e)
    {
        var firstChar = App.Platform.Repository.GetAllCharacters().FirstOrDefault();
        if (firstChar != null) StartNewSessionWithCharacter(firstChar, RoleplayGameMode.TurnBased);
    }

    private void NewTwoPerson_Click(object sender, RoutedEventArgs e)
    {
        var firstChar = App.Platform.Repository.GetAllCharacters().FirstOrDefault(c => c.Id == "char-csm-reze")
                        ?? App.Platform.Repository.GetAllCharacters().FirstOrDefault();
        if (firstChar != null) StartNewSessionWithCharacter(firstChar, RoleplayGameMode.TwoPersonPrivate);
    }

    private void OpenMultiplayer_Click(object sender, RoutedEventArgs e) =>
        App.MainWindowInstance.NavigateToSection("multiplayer");

    private void OpenModels_Click(object sender, RoutedEventArgs e) =>
        App.MainWindowInstance.NavigateToSection("models");
}
