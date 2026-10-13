using System;
using System.Collections.Generic;
using System.Linq;
using Aetheria.Core.Domain;
using Aetheria.Core.Multiplayer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Aetheria.App.Pages;

public sealed partial class MultiplayerPage : Page
{
    private List<Persona> _personas = new();
    private List<Character> _characters = new();
    private List<World> _worlds = new();
    private List<DiscoveredLanSession> _discoveredLan = new();
    private bool _isReady = false;
    private bool _isLoadingLobbyRoles = false;

    public MultiplayerPage()
    {
        this.InitializeComponent();
        this.Loaded += MultiplayerPage_Loaded;
    }

    private void MultiplayerPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadWorlds();
        LoadPersonas();
        RefreshAdapters();

        if (App.Platform.ActiveHostServer != null && App.Platform.ActiveHostServer.IsRunning)
        {
            HostInviteTokenBox.Text = App.Platform.ActiveHostServer.InviteToken;
            HostPortBox.Value = App.Platform.ActiveHostServer.ListeningPort;
            HostRoomTitleBox.Text = App.Platform.ActiveHostServer.ActiveSession.Title;
        }
        else if (string.IsNullOrWhiteSpace(HostInviteTokenBox.Text))
        {
            HostInviteTokenBox.Text = MultiplayerHostServer.GenerateSecureInviteToken();
        }

        if (HostModeCombo.SelectedIndex < 0) HostModeCombo.SelectedIndex = 0;
        RefreshStatus();
    }

    private void LoadWorlds()
    {
        _worlds = App.Platform.Repository.GetAllWorlds().ToList();
        HostWorldCombo.Items.Clear();
        foreach (var w in _worlds)
        {
            HostWorldCombo.Items.Add(new ComboBoxItem
            {
                Content = w.Name,
                Tag = w.Id
            });
        }
        if (HostWorldCombo.Items.Count > 0) HostWorldCombo.SelectedIndex = 0;
    }

    private void LoadPersonas()
    {
        _isLoadingLobbyRoles = true;
        try
        {
            var prevHostTag = (HostPersonaCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var prevJoinTag = (JoinPersonaCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var prevLobbyTag = (LobbyRolePickerCombo.SelectedItem as ComboBoxItem)?.Tag as string;

            _personas = App.Platform.Repository.GetAllPersonas().ToList();
            _characters = App.Platform.Repository.GetAllCharacters().ToList();

            HostPersonaCombo.Items.Clear();
            JoinPersonaCombo.Items.Clear();
            SyncCustomCharCombo.Items.Clear();
            LobbyRolePickerCombo.Items.Clear();

            int defaultIdx = 0;

            for (int i = 0; i < _personas.Count; i++)
            {
                var p = _personas[i];
                if (p.IsDefault) defaultIdx = i;
                HostPersonaCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"[Persona] {p.Name}",
                    Tag = $"persona:{p.Id}"
                });
                JoinPersonaCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"[Persona] {p.Name}",
                    Tag = $"persona:{p.Id}"
                });
                LobbyRolePickerCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"[Persona] {p.Name}",
                    Tag = $"persona:{p.Id}"
                });
            }

            foreach (var c in _characters)
            {
                HostPersonaCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"[Playable Character] {c.Name} ({c.Title})",
                    Tag = $"char:{c.Id}"
                });
                JoinPersonaCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"[Playable Character] {c.Name} ({c.Title})",
                    Tag = $"char:{c.Id}"
                });
                SyncCustomCharCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{c.Name} — {c.Title}",
                    Tag = c.Id
                });
                LobbyRolePickerCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"[Playable Character] {c.Name} ({c.Title})",
                    Tag = $"char:{c.Id}"
                });
            }

            RestoreComboSelection(HostPersonaCombo, prevHostTag, defaultIdx);
            RestoreComboSelection(JoinPersonaCombo, prevJoinTag, defaultIdx);
            RestoreComboSelection(LobbyRolePickerCombo, prevLobbyTag, defaultIdx);

            if (SyncCustomCharCombo.Items.Count > 0)
            {
                SyncCustomCharCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            _isLoadingLobbyRoles = false;
        }

        if (LobbyRolePickerCombo.SelectedItem != null)
        {
            var (persona, customChar) = ResolveSelectedIdentity(LobbyRolePickerCombo);
            LobbyPreviewNameText.Text = persona.Name;
            LobbyPreviewBioText.Text = string.IsNullOrWhiteSpace(persona.Description)
                ? (customChar?.Description ?? "Custom character")
                : persona.Description;
        }
    }

    private static void RestoreComboSelection(ComboBox combo, string? targetTag, int fallbackIndex)
    {
        if (combo.Items.Count == 0) return;
        if (!string.IsNullOrWhiteSpace(targetTag))
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, targetTag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }
        combo.SelectedIndex = Math.Clamp(fallbackIndex, 0, combo.Items.Count - 1);
    }

    private (Persona SelectedPersona, Character? CustomCharacter) ResolveSelectedIdentity(ComboBox combo)
    {
        var fallbackPersona = App.Platform.GetDefaultPersona();
        if (combo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            if (tag.StartsWith("persona:", StringComparison.Ordinal))
            {
                var pid = tag["persona:".Length..];
                var p = _personas.FirstOrDefault(x => x.Id == pid) ?? fallbackPersona;
                var matchingChar = _characters.FirstOrDefault(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                return (p, matchingChar);
            }
            if (tag.StartsWith("char:", StringComparison.Ordinal))
            {
                var cid = tag["char:".Length..];
                var c = _characters.FirstOrDefault(x => x.Id == cid);
                if (c != null)
                {
                    var convertedPersona = new Persona
                    {
                        Id = $"persona-from-{c.Id}",
                        Name = c.Name,
                        Description = $"{c.Title}. {c.Description}".Trim(),
                        Appearance = c.Appearance,
                        Personality = $"{c.Personality} Speech style: {c.SpeechStyle}".Trim(),
                        Background = c.Background
                    };
                    App.Platform.Repository.SavePersona(convertedPersona);
                    return (convertedPersona, c);
                }
            }
        }
        return (fallbackPersona, null);
    }

    private void RefreshAdapters()
    {
        NetworkAdaptersListView.Items.Clear();
        var adapters = NetworkDiagnosticsHelper.GetLocalNetworkInterfaces();
        foreach (var a in adapters)
        {
            NetworkAdaptersListView.Items.Add(new ListViewItem
            {
                Content = $"{a.IpAddress} — [{a.AdapterCategory}] ({a.AdapterName})",
                Tag = a.IpAddress
            });
        }
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e) => RefreshAdapters();

    private void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        LoadPersonas();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        bool isHosting = App.Platform.ActiveHostServer?.IsRunning == true;
        bool isClient = App.Platform.ActiveClient?.IsConnected == true;
        bool inRoom = isHosting || isClient;

        StartHostBtn.IsEnabled = !inRoom;
        StopHostBtn.IsEnabled = isHosting;
        ConnectClientBtn.IsEnabled = !inRoom;
        DisconnectClientBtn.IsEnabled = isClient;

        var session = isClient
            ? App.Platform.ActiveClient?.SyncedSession
            : (isHosting ? App.Platform.ActiveHostServer?.ActiveSession : App.Platform.ActiveSession);

        if (inRoom && session != null)
        {
            ActiveLobbyPanel.Visibility = Visibility.Visible;
            LobbyRoomTitleText.Text = session.Title;
            LobbyGameModeBadge.Text = $"Mode: {session.GameMode}";

            var world = App.Platform.Repository.GetAllWorlds().FirstOrDefault(w => w.Id == session.WorldId);
            LobbyWorldBadge.Text = $"World: {world?.Name ?? "Custom Scenario"}";

            if (isHosting && App.Platform.ActiveHostServer != null)
            {
                var host = App.Platform.ActiveHostServer;
                LobbyConnectionInfoText.Text = $"Port {host.ListeningPort} • Mobile {host.MobileCompanionPort} • Token {host.InviteToken}";
                LiveStatusIndicator.Text = $"Hosting Room: {session.Title}";
                App.MainWindowInstance?.UpdateHeaderBadge($"Hosting ({session.Title})");

                LobbyLaunchBtn.Visibility = Visibility.Visible;
                LobbyWaitingLaunchText.Visibility = Visibility.Collapsed;
                LobbyLeaveBtn.Content = "Stop Hosting";

                var localIp = NetworkDiagnosticsHelper.GetLocalNetworkInterfaces()
                    .FirstOrDefault(a => !a.IpAddress.StartsWith("127."))?.IpAddress ?? "127.0.0.1";
                MobileCompanionUrlBox.Text = $"http://{localIp}:{host.MobileCompanionPort}/";
                MobileCompanionStatusText.Text = $"Active (Port {host.MobileCompanionPort})";
            }
            else if (isClient && App.Platform.ActiveClient != null)
            {
                var myId = App.Platform.ActiveClient.AssignedParticipantId ?? "Guest";
                LobbyConnectionInfoText.Text = $"Connected as {myId}";
                LiveStatusIndicator.Text = $"In Room Lobby: {session.Title}";
                App.MainWindowInstance?.UpdateHeaderBadge($"Connected ({session.Title})");

                LobbyLaunchBtn.Visibility = Visibility.Collapsed;
                LobbyWaitingLaunchText.Visibility = Visibility.Visible;
                LobbyLeaveBtn.Content = "Leave Lobby";
                MobileCompanionStatusText.Text = "(Client Mode)";
            }

            // Update Lobby Party Member Cards - HUMAN PLAYERS ONLY
            LobbyPartyListView.Items.Clear();
            var humanParticipants = session.Participants.Where(p => !p.IsAiCharacter).ToList();
            var aiParticipants = session.Participants.Where(p => p.IsAiCharacter).ToList();

            int readyCount = 0;
            int totalHuman = humanParticipants.Count;

            foreach (var p in humanParticipants)
            {
                if (p.IsReady || p.IsHost) readyCount++;

                var icon = p.IsHost ? "👑 " : (p.ParticipantId.StartsWith("mobile") ? "📱 " : "🌟 ");
                var roleTag = p.IsHost ? "HOST / LEADER" : "CO-OP PLAYER";
                var readyText = (p.IsHost || p.IsReady) ? "✓ READY" : "⌛ CHOOSING ROLE";
                var connText = p.IsConnected ? "Online" : "Offline";

                var charSub = !string.IsNullOrWhiteSpace(p.PersonaName) ? $"Playing as: {p.PersonaName}" : "No character selected";

                var card = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 4),
                    Child = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                            new ColumnDefinition { Width = GridLength.Auto }
                        },
                        Children =
                        {
                            new StackPanel
                            {
                                Spacing = 2,
                                Children =
                                {
                                    new TextBlock { Text = $"{icon}{p.DisplayName} [{roleTag}]", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 },
                                    new TextBlock { Text = $"{charSub} • {connText}", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], FontSize = 11 }
                                }
                            }
                        }
                    }
                };

                var readyBadge = new Border
                {
                    Background = (p.IsHost || p.IsReady)
                        ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"]
                        : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = readyText,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        FontSize = 10,
                        Foreground = (p.IsHost || p.IsReady)
                            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBrush"]
                    }
                };
                Grid.SetColumn(readyBadge, 1);
                ((Grid)card.Child).Children.Add(readyBadge);

                LobbyPartyListView.Items.Add(card);
            }

            // Also render AI Cast in Scenario
            if (aiParticipants.Count > 0)
            {
                var aiCard = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 4, 0, 4),
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = $"🤖 AI Companions in Scenario ({aiParticipants.Count}): " + string.Join(", ", aiParticipants.Select(a => a.DisplayName)), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] },
                            new TextBlock { Text = "Controlled authoritatively by Host AI Engine (not human players)", FontSize = 10, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] }
                        }
                    }
                };
                LobbyPartyListView.Items.Add(aiCard);
            }

            int connectedCount = humanParticipants.Count(p => p.IsConnected);
            LobbyPartyCountText.Text = $"{readyCount}/{totalHuman} Ready ({connectedCount} Player{(connectedCount == 1 ? "" : "s")} Online)";
            LobbyReadyBtn.Content = _isReady ? "✓ Ready (Waiting for Host)" : "⌛ Not Ready (Click to Ready)";
        }
        else
        {
            ActiveLobbyPanel.Visibility = Visibility.Collapsed;
            LiveStatusIndicator.Text = "Mode: Offline (Single-Player)";
            App.MainWindowInstance?.UpdateHeaderBadge("Single-Player Ready");

            var localIp = NetworkDiagnosticsHelper.GetLocalNetworkInterfaces()
                .FirstOrDefault(a => !a.IpAddress.StartsWith("127."))?.IpAddress ?? "127.0.0.1";
            MobileCompanionUrlBox.Text = $"http://{localIp}:7778/";
            MobileCompanionStatusText.Text = "(Starts with Host on Port 7778)";
        }

        // Refresh Participants list in diagnostics
        ParticipantsListView.Items.Clear();
        if (inRoom && session != null)
        {
            var activeHumans = session.Participants.Where(p => !p.IsAiCharacter).ToList();
            foreach (var p in activeHumans)
            {
                var role = p.IsHost ? "Host" : "Player";
                bool isConn = p.IsHost || (isHosting && App.Platform.ActiveHostServer?.IsRunning == true && p.IsConnected) || (isClient && p.IsConnected);
                var conn = isConn ? "Online" : "Offline";
                var ready = (p.IsHost || p.IsReady) ? "Ready" : "Not Ready";
                ParticipantsListView.Items.Add($"{p.DisplayName} [{role}] — {ready} • {conn} ({p.ParticipantId})");
            }
            if (activeHumans.Count == 1 && activeHumans[0].IsHost)
            {
                ParticipantsListView.Items.Add("ℹ️ Waiting for co-op partner to connect via LAN, Wi-Fi, or Android App...");
            }
        }
        else
        {
            ParticipantsListView.Items.Add("ℹ️ Offline / Single-Player (No active party room)");
            ParticipantsListView.Items.Add("💡 To play co-op, click 'Open Room & Party Lobby' above.");
        }

        // Refresh Diagnostic Logs
        MultiplayerLogListView.Items.Clear();
        foreach (var line in App.Platform.GetDiagnosticLogs().Reverse().Take(25))
        {
            MultiplayerLogListView.Items.Add(line);
        }
    }

    private async void LobbyRolePickerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingLobbyRoles) return;
        var (persona, customChar) = ResolveSelectedIdentity(LobbyRolePickerCombo);

        LobbyPreviewNameText.Text = persona.Name;
        LobbyPreviewBioText.Text = string.IsNullOrWhiteSpace(persona.Description)
            ? (customChar?.Description ?? "Custom character")
            : persona.Description;

        // Sync with active session
        if (App.Platform.ActiveClient?.IsConnected == true)
        {
            var myId = App.Platform.ActiveClient.AssignedParticipantId;
            var session = App.Platform.ActiveClient.SyncedSession;
            var myPart = session?.Participants.FirstOrDefault(p => p.ParticipantId == myId);
            if (myPart != null)
            {
                myPart.PersonaName = persona.Name;
                myPart.DisplayName = persona.Name;
                myPart.PersonaId = persona.Id;
                myPart.CharacterId = customChar?.Id;
            }
            await App.Platform.ActiveClient.UpdateIdentityAsync(
                persona.Name,
                persona.Id,
                persona.Description,
                customChar?.Id,
                isReady: _isReady);
            RefreshStatus();
        }
        else if (App.Platform.ActiveHostServer?.IsRunning == true)
        {
            var host = App.Platform.ActiveHostServer;
            host.HostPersona = persona;
            host.ActiveSession.ActivePersonaId = persona.Id;
            if (App.Platform.ActiveSession != null)
            {
                App.Platform.ActiveSession.ActivePersonaId = persona.Id;
            }
            var hostPart = host.ActiveSession.Participants.FirstOrDefault(p => p.IsHost || p.ParticipantId == "host-player");
            if (hostPart != null)
            {
                hostPart.PersonaName = persona.Name;
                hostPart.DisplayName = persona.Name;
                hostPart.PersonaId = persona.Id;
                hostPart.CharacterId = customChar?.Id;
            }
            await host.UpdateParticipantIdentityAsync(
                "host-player",
                persona.Name,
                persona.Name,
                persona.Id,
                persona.Description,
                customChar?.Id,
                isReady: true);
            RefreshStatus();
        }
    }

    private async void LobbyReady_Click(object sender, RoutedEventArgs e)
    {
        _isReady = !_isReady;
        LobbyReadyBtn.Content = _isReady ? "✓ Ready (Waiting for Host)" : "⌛ Not Ready (Click to Ready)";

        var (persona, customChar) = ResolveSelectedIdentity(LobbyRolePickerCombo);
        if (App.Platform.ActiveClient?.IsConnected == true)
        {
            await App.Platform.ActiveClient.UpdateIdentityAsync(
                persona.Name,
                persona.Id,
                persona.Description,
                customChar?.Id,
                isReady: _isReady);
        }
        RefreshStatus();
    }

    private async void LobbyLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (App.Platform.ActiveHostServer?.IsRunning == true)
        {
            await App.Platform.ActiveHostServer.LaunchGameSessionAsync();
            App.MainWindowInstance?.ShowNotification("Adventure Launched", "Entering roleplay workspace with all party members!", InfoBarSeverity.Success);
            App.MainWindowInstance?.NavigateToSection("roleplay");
        }
    }

    private async void LobbyLeave_Click(object sender, RoutedEventArgs e)
    {
        if (App.Platform.ActiveHostServer != null)
        {
            await App.Platform.ActiveHostServer.StopAsync();
            App.Platform.ActiveHostServer = null;
        }
        if (App.Platform.ActiveClient != null)
        {
            await App.Platform.ActiveClient.DisconnectAsync();
            App.Platform.ActiveClient = null;
        }
        _isReady = false;
        RefreshStatus();
        App.MainWindowInstance?.ShowNotification("Lobby Left", "You have left the multiplayer party room.", InfoBarSeverity.Informational);
    }

    private void LobbyChatInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            LobbyChatSend_Click(sender, e);
        }
    }

    private async void LobbyChatSend_Click(object sender, RoutedEventArgs e)
    {
        var text = LobbyChatInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        LobbyChatInputBox.Text = string.Empty;

        var myName = LobbyPreviewNameText.Text;
        LobbyChatListView.Items.Add($"[{myName}]: {text}");

        try
        {
            if (App.Platform.ActiveHostServer?.IsRunning == true)
            {
                await App.Platform.ActiveHostServer.SubmitHostActionAsync(RoleplayActionType.Speak, text);
            }
            else if (App.Platform.ActiveClient?.IsConnected == true)
            {
                await App.Platform.ActiveClient.SendActionAsync(RoleplayActionType.Speak, text);
            }
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Chat Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void CopyMobileUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = MobileCompanionUrlBox.Text;
        var dp = new DataPackage();
        dp.SetText(url);
        Clipboard.SetContent(dp);
        App.MainWindowInstance?.ShowNotification("Mobile URL Copied", $"'{url}' copied to clipboard. Open it in Chrome on your phone!", InfoBarSeverity.Success);
    }

    private void OpenMobileUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = MobileCompanionUrlBox.Text;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void RegenerateToken_Click(object sender, RoutedEventArgs e)
    {
        HostInviteTokenBox.Text = MultiplayerHostServer.GenerateSecureInviteToken();
    }

    private void CopyInviteDetails_Click(object sender, RoutedEventArgs e)
    {
        var port = (int)(double.IsNaN(HostPortBox.Value) ? 7777 : HostPortBox.Value);
        var token = HostInviteTokenBox.Text.Trim();
        var adapters = NetworkDiagnosticsHelper.GetLocalNetworkInterfaces();
        var ipSummary = string.Join(", ", adapters.Select(a => $"{a.IpAddress} ({a.AdapterCategory})"));

        var text = $"Aetheria Multiplayer Invite\nPort: {port}\nInvite Token: {token}\nHost Addresses: {ipSummary}";
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);

        App.MainWindowInstance?.ShowNotification("Invite Copied", "Copied Host IP addresses, TCP port, and Invite Token to clipboard.", InfoBarSeverity.Success);
    }

    private void StartHost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Platform.EnsureActiveSessionLoaded();
            var session = App.Platform.ActiveSession!;

            var roomTitle = HostRoomTitleBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(roomTitle))
            {
                session.Title = roomTitle;
            }

            if (HostModeCombo.SelectedItem is ComboBoxItem cbi &&
                cbi.Tag is string modeStr &&
                Enum.TryParse<RoleplayGameMode>(modeStr, out var mode))
            {
                session.GameMode = mode;
            }

            session.MaxPlayers = (int)(double.IsNaN(MaxPlayersBox.Value) ? 4 : MaxPlayersBox.Value);
            session.AllowAiInTwoPersonMode = AllowAiInPrivateCheck.IsChecked == true;
            if (session.GameMode == RoleplayGameMode.TwoPersonPrivate)
            {
                session.MaxPlayers = 2;
            }

            if (HostWorldCombo.SelectedItem is ComboBoxItem wItem && wItem.Tag is string wid)
            {
                session.WorldId = wid;
            }

            var (persona, customHostChar) = ResolveSelectedIdentity(HostPersonaCombo);
            session.ActivePersonaId = persona.Id;

            // Purge any stale disconnected participants from previous runs
            session.Participants.RemoveAll(p => !p.IsHost && p.ParticipantId != "host-player" && !p.IsAiCharacter);

            var hostParticipant = session.Participants.FirstOrDefault(p => p.IsHost || p.ParticipantId == "host-player");
            if (hostParticipant != null)
            {
                hostParticipant.DisplayName = persona.Name;
                hostParticipant.PersonaId = persona.Id;
                hostParticipant.PersonaName = persona.Name;
                hostParticipant.PersonaSummary = $"{persona.Description} Appearance: {persona.Appearance} Personality: {persona.Personality}".Trim();
                hostParticipant.IsReady = true;
            }

            var world = App.Platform.Repository.GetAllWorlds().FirstOrDefault(w => w.Id == session.WorldId)
                        ?? App.Platform.Repository.GetAllWorlds().FirstOrDefault();
            var chars = App.Platform.Repository.GetAllCharacters()
                .Where(c => session.ActiveCharacterIds.Contains(c.Id)).ToList();
            if (customHostChar != null && chars.All(c => c.Id != customHostChar.Id))
            {
                chars.Add(customHostChar);
            }
            if (chars.Count == 0)
            {
                chars = App.Platform.Repository.GetAllCharacters().Take(1).ToList();
            }
            var preset = App.Platform.GetDefaultModelPreset();

            var hostServer = new MultiplayerHostServer(
                App.Platform.Engine,
                App.Platform.AiOrchestrator,
                session,
                persona,
                world,
                chars,
                preset);

            hostServer.OnLogMessage += msg =>
            {
                App.Platform.Log($"[Host] {msg}");
                DispatcherQueue.TryEnqueue(RefreshStatus);
            };
            hostServer.OnSessionStateChanged += _ =>
            {
                DispatcherQueue.TryEnqueue(RefreshStatus);
            };

            int port = (int)(double.IsNaN(HostPortBox.Value) ? 7777 : HostPortBox.Value);
            var token = string.IsNullOrWhiteSpace(HostInviteTokenBox.Text)
                ? MultiplayerHostServer.GenerateSecureInviteToken()
                : HostInviteTokenBox.Text.Trim();
            HostInviteTokenBox.Text = token;

            hostServer.Start(port, LanBroadcastCheck.IsChecked == true, token);
            App.Platform.ActiveHostServer = hostServer;

            if (string.IsNullOrWhiteSpace(JoinInviteTokenBox.Text))
            {
                JoinInviteTokenBox.Text = token;
            }

            _isReady = true;
            RefreshStatus();
            App.MainWindowInstance?.ShowNotification(
                "Party Lobby Opened",
                $"Hosting room '{session.Title}' as '{persona.Name}' on Port {port}. Party lobby is now open!",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Failed to Start Host", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void StopHost_Click(object sender, RoutedEventArgs e)
    {
        if (App.Platform.ActiveHostServer != null)
        {
            await App.Platform.ActiveHostServer.StopAsync();
            App.Platform.ActiveHostServer = null;
            App.Platform.Log("Stopped Multiplayer Host Server.");
        }
        _isReady = false;
        RefreshStatus();
        App.MainWindowInstance?.ShowNotification("Host Stopped", "Multiplayer party room closed.", InfoBarSeverity.Informational);
    }

    private async void ConnectClient_Click(object sender, RoutedEventArgs e)
    {
        var hostIp = JoinHostAddressBox.Text.Trim();
        var port = (int)(double.IsNaN(JoinPortBox.Value) ? 7777 : JoinPortBox.Value);
        var token = JoinInviteTokenBox.Text.Trim();

        if (string.IsNullOrEmpty(hostIp) || string.IsNullOrEmpty(token))
        {
            App.MainWindowInstance?.ShowNotification("Missing Connection Info", "Enter both Host IPv4 Address and Session Invite Token.", InfoBarSeverity.Warning);
            return;
        }

        var (selectedPersona, customCharacter) = ResolveSelectedIdentity(JoinPersonaCombo);

        ConnectClientBtn.IsEnabled = false;
        try
        {
            var client = new MultiplayerClient();
            client.OnLogMessage += msg =>
            {
                App.Platform.Log($"[Client] {msg}");
                DispatcherQueue.TryEnqueue(RefreshStatus);
            };
            client.OnSessionSynced += synced =>
            {
                App.Platform.ActiveSession = synced;
                DispatcherQueue.TryEnqueue(RefreshStatus);
            };
            client.OnLobbyStateReceived += lobby =>
            {
                DispatcherQueue.TryEnqueue(RefreshStatus);
            };
            client.OnGameLaunchReceived += () =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    App.MainWindowInstance?.ShowNotification("Adventure Launched!", "The Host launched the roleplay session! Entering workspace...", InfoBarSeverity.Success);
                    App.MainWindowInstance?.NavigateToSection("roleplay");
                });
            };
            client.OnCharacterSynced += syncedChar =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadPersonas();
                    RefreshStatus();
                    App.MainWindowInstance?.ShowNotification("Character Synced", $"Received custom character '{syncedChar.Name}' from session!", InfoBarSeverity.Informational);
                });
            };
            client.OnDisconnected += () =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _isReady = false;
                    RefreshStatus();
                    App.MainWindowInstance?.ShowNotification("Disconnected", "Disconnected from party room.", InfoBarSeverity.Warning);
                });
            };

            var (success, message) = await client.ConnectAsync(
                hostIp,
                port,
                token,
                selectedPersona,
                customCharacter: customCharacter);
            if (success)
            {
                App.Platform.ActiveClient = client;
                if (client.SyncedSession != null)
                {
                    App.Platform.ActiveSession = client.SyncedSession;
                }
                LoadPersonas();
                _isReady = false;
                RefreshStatus();
                App.MainWindowInstance?.ShowNotification("Joined Party Lobby", $"Playing as '{selectedPersona.Name}'. {message}", InfoBarSeverity.Success);
            }
            else
            {
                RefreshStatus();
                App.MainWindowInstance?.ShowNotification("Connection Refused", message, InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            RefreshStatus();
            App.MainWindowInstance?.ShowNotification("Connection Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void DisconnectClient_Click(object sender, RoutedEventArgs e)
    {
        if (App.Platform.ActiveClient != null)
        {
            await App.Platform.ActiveClient.DisconnectAsync();
            App.Platform.ActiveClient = null;
        }
        _isReady = false;
        RefreshStatus();
    }

    private async void SyncCharacterToSession_Click(object sender, RoutedEventArgs e)
    {
        if (SyncCustomCharCombo.SelectedItem is not ComboBoxItem cbi || cbi.Tag is not string charId)
        {
            App.MainWindowInstance?.ShowNotification("Select a Character", "Choose a character from the dropdown to sync.", InfoBarSeverity.Warning);
            return;
        }

        var character = App.Platform.Repository.GetAllCharacters().FirstOrDefault(c => c.Id == charId);
        if (character == null) return;

        try
        {
            if (App.Platform.ActiveHostServer?.IsRunning == true)
            {
                await App.Platform.ActiveHostServer.SyncCharacterFromHostAsync(character);
                RefreshStatus();
                App.MainWindowInstance?.ShowNotification("Character Synced to Guests", $"'{character.Name}' was synced to all connected players and added to the session.", InfoBarSeverity.Success);
            }
            else if (App.Platform.ActiveClient?.IsConnected == true)
            {
                await App.Platform.ActiveClient.SyncCharacterToHostAsync(character);
                RefreshStatus();
                App.MainWindowInstance?.ShowNotification("Character Synced to Host", $"'{character.Name}' was sent from your PC to the Host and added to the shared session!", InfoBarSeverity.Success);
            }
            else
            {
                if (App.Platform.ActiveSession != null && !App.Platform.ActiveSession.ActiveCharacterIds.Contains(character.Id))
                {
                    App.Platform.ActiveSession.ActiveCharacterIds.Add(character.Id);
                    App.Platform.Repository.SaveSession(App.Platform.ActiveSession);
                }
                App.MainWindowInstance?.ShowNotification("Added to Active Session", $"'{character.Name}' is now active in the current session. When you host or join, it will sync automatically!", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowNotification("Character Sync Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void ScanLan_Click(object sender, RoutedEventArgs e)
    {
        ScanLanBtn.IsEnabled = false;
        DiscoveredLanListView.Items.Clear();
        DiscoveredLanListView.Items.Add("Scanning UDP broadcast port 47891...");

        try
        {
            _discoveredLan = await MultiplayerClient.DiscoverLanSessionsAsync(1200);
            DiscoveredLanListView.Items.Clear();

            if (_discoveredLan.Count == 0)
            {
                DiscoveredLanListView.Items.Add("No active LAN sessions discovered. For Hamachi/Internet play, enter Host IP directly above.");
            }
            else
            {
                foreach (var s in _discoveredLan)
                {
                    DiscoveredLanListView.Items.Add(new ListViewItem
                    {
                        Content = $"{s.Title} ({s.GameMode}) — Host: {s.HostName} @ {s.HostAddress}:{s.Port} [{s.CurrentPlayers}/{s.MaxPlayers}]",
                        Tag = s.SessionId
                    });
                }
            }
        }
        finally
        {
            ScanLanBtn.IsEnabled = true;
        }
    }

    private void DiscoveredLanListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredLanListView.SelectedItem is ListViewItem item && item.Tag is string sid)
        {
            var found = _discoveredLan.FirstOrDefault(s => s.SessionId == sid);
            if (found != null)
            {
                JoinHostAddressBox.Text = found.HostAddress;
                JoinPortBox.Value = found.Port;
            }
        }
    }

    private async void TestEndpoint_Click(object sender, RoutedEventArgs e)
    {
        var host = JoinHostAddressBox.Text.Trim();
        var port = (int)(double.IsNaN(JoinPortBox.Value) ? 7777 : JoinPortBox.Value);
        DiagnosticResultText.Text = $"Testing TCP handshake to {host}:{port}...";

        var (reachable, msg) = await NetworkDiagnosticsHelper.TestTcpEndpointAsync(host, port);
        DiagnosticResultText.Text = msg;
        App.MainWindowInstance?.ShowNotification(
            reachable ? "Endpoint Reachable" : "Endpoint Unreachable",
            msg,
            reachable ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private void GoToRoleplay_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateToSection("roleplay");
    }
}
