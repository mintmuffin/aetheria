using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aetheria.App.Helpers;
using Aetheria.Core.Domain;
using Aetheria.Core.Formats;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aetheria.App.Pages;

public sealed partial class RoleplayPage : Page
{
    private bool _isLoadingSelectors;
    private CancellationTokenSource? _streamCts;
    private TextBlock? _liveStreamTextBlock;

    private List<RoleplaySession> _sessions = new();
    private List<Character> _characters = new();
    private List<Persona> _personas = new();
    private List<ModelPreset> _presets = new();
    private bool _isLoaded;

    public RoleplayPage()
    {
        this.InitializeComponent();
        this.Loaded += OnPageLoaded;
        this.Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        var ctx = App.Platform;
        ctx.EnsureActiveSessionLoaded();

        if (ctx.ActiveHostServer != null)
        {
            ctx.ActiveHostServer.OnMessageAdded += OnExternalMessageReceived;
            ctx.ActiveHostServer.OnSessionStateChanged += OnExternalSessionStateChanged;
        }
        if (ctx.ActiveClient != null)
        {
            ctx.ActiveClient.OnMessageReceived += OnExternalMessageReceived;
            ctx.ActiveClient.OnSessionSynced += OnExternalSessionStateChanged;
            ctx.ActiveClient.OnSystemNoticeReceived += OnClientNotice;
        }

        LoadTopSelectors();
        RefreshSessionWorkspace();
        AccessibilityManager.Current.AccessibilitySettingsChanged += OnAccessibilityChanged;
    }

    private void OnAccessibilityChanged()
    {
        DispatcherQueue.TryEnqueue(RefreshSessionWorkspace);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        AccessibilityManager.Current.AccessibilitySettingsChanged -= OnAccessibilityChanged;
        var ctx = App.Platform;
        if (ctx.ActiveHostServer != null)
        {
            ctx.ActiveHostServer.OnMessageAdded -= OnExternalMessageReceived;
            ctx.ActiveHostServer.OnSessionStateChanged -= OnExternalSessionStateChanged;
        }
        if (ctx.ActiveClient != null)
        {
            ctx.ActiveClient.OnMessageReceived -= OnExternalMessageReceived;
            ctx.ActiveClient.OnSessionSynced -= OnExternalSessionStateChanged;
            ctx.ActiveClient.OnSystemNoticeReceived -= OnClientNotice;
        }
    }

    private void OnExternalMessageReceived(SessionMessage msg)
    {
        DispatcherQueue.TryEnqueue(RefreshSessionWorkspace);
    }

    private void OnExternalSessionStateChanged(RoleplaySession session)
    {
        DispatcherQueue.TryEnqueue(RefreshSessionWorkspace);
    }

    private void OnClientNotice(string notice)
    {
        DispatcherQueue.TryEnqueue(() =>
            App.MainWindowInstance.ShowNotification("Multiplayer Session Notice", notice, InfoBarSeverity.Warning));
    }

    private void LoadTopSelectors()
    {
        _isLoadingSelectors = true;
        var ctx = App.Platform;
        var active = GetEffectiveSession();

        _sessions = ctx.Repository.GetAllSessions();
        _characters = ctx.Repository.GetAllCharacters();
        _personas = ctx.Repository.GetAllPersonas();
        _presets = ctx.Repository.GetAllModelPresets();

        SessionCombo.Items.Clear();
        foreach (var s in _sessions)
        {
            SessionCombo.Items.Add(new ComboBoxItem { Content = $"{s.Title} ({s.GameMode})", Tag = s.SessionId });
        }
        SelectComboByTag(SessionCombo, active.SessionId);

        GameModeCombo.Items.Clear();
        foreach (var mode in Enum.GetValues<RoleplayGameMode>())
        {
            GameModeCombo.Items.Add(new ComboBoxItem { Content = mode.ToString(), Tag = mode.ToString() });
        }
        SelectComboByTag(GameModeCombo, active.GameMode.ToString());

        CharacterCombo.Items.Clear();
        CharacterCombo.Items.Add(new ComboBoxItem { Content = "(None / Human Only)", Tag = "" });
        foreach (var c in _characters)
        {
            CharacterCombo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        }
        SelectComboByTag(CharacterCombo, active.ActiveCharacterIds.FirstOrDefault() ?? "");

        PersonaCombo.Items.Clear();
        foreach (var p in _personas)
        {
            PersonaCombo.Items.Add(new ComboBoxItem { Content = $"[Persona] {p.Name}", Tag = p.Id });
        }
        foreach (var c in _characters)
        {
            PersonaCombo.Items.Add(new ComboBoxItem { Content = $"[Character] {c.Name}", Tag = c.Id });
        }
        if (ctx.ActiveClient?.IsConnected == true)
        {
            var myPart = active.Participants.FirstOrDefault(p => p.ParticipantId == ctx.ActiveClient.AssignedParticipantId);
            if (myPart != null && (!string.IsNullOrWhiteSpace(myPart.CharacterId) || !string.IsNullOrWhiteSpace(myPart.PersonaId)))
            {
                SelectComboByTag(PersonaCombo, myPart.CharacterId ?? myPart.PersonaId);
            }
            else
            {
                SelectComboByTag(PersonaCombo, active.ActivePersonaId);
            }
        }
        else
        {
            SelectComboByTag(PersonaCombo, active.ActivePersonaId);
        }

        ModelPresetCombo.Items.Clear();
        foreach (var m in _presets)
        {
            ModelPresetCombo.Items.Add(new ComboBoxItem { Content = $"{m.Name} [{m.ProviderType}]", Tag = m.Id });
        }
        SelectComboByTag(ModelPresetCombo, active.ActiveModelPresetId ?? ctx.GetDefaultModelPreset().Id);

        _isLoadingSelectors = false;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private RoleplaySession GetEffectiveSession()
    {
        var ctx = App.Platform;
        if (ctx.ActiveClient?.IsConnected == true && ctx.ActiveClient.SyncedSession != null)
            return ctx.ActiveClient.SyncedSession;
        if (ctx.ActiveHostServer?.IsRunning == true)
            return ctx.ActiveHostServer.ActiveSession;
        ctx.EnsureActiveSessionLoaded();
        return ctx.ActiveSession!;
    }

    private void RefreshSessionWorkspace()
    {
        if (!_isLoaded) return;
        var ctx = App.Platform;
        var session = GetEffectiveSession();

        // Update Turn & Mode Banner
        var turnHolderId = ctx.Engine.GetCurrentTurnParticipantId(session);
        var turnHolderName = session.Participants.FirstOrDefault(p => p.ParticipantId == turnHolderId)?.DisplayName ?? "Any";

        TurnStatusText.Text = session.GameMode == RoleplayGameMode.TurnBased
            ? $"Turn #{session.TurnNumber} • Active Turn: {turnHolderName} ({session.CurrentTurnPhase}) • Location: {session.CurrentLocation}"
            : $"Mode: {session.GameMode} • Location: {session.CurrentLocation}";

        if (session.Participants.Count > 1)
        {
            PartyRosterBorder.Visibility = Visibility.Visible;
            var parts = session.Participants.Select(p =>
            {
                var icon = p.IsHost ? "👑 " : (p.IsAiCharacter ? "🤖 " : "🌟 ");
                return $"{icon}{p.PersonaName}";
            });
            PartyRosterText.Text = "Party: " + string.Join(" • ", parts);
        }
        else
        {
            PartyRosterBorder.Visibility = Visibility.Collapsed;
        }

        TwoPersonAiToggle.Visibility = session.GameMode == RoleplayGameMode.TwoPersonPrivate
            ? Visibility.Visible
            : Visibility.Collapsed;
        TwoPersonAiToggle.IsChecked = session.AllowAiInTwoPersonMode;

        AdvanceTurnBtn.Visibility = session.GameMode == RoleplayGameMode.TurnBased
            ? Visibility.Visible
            : Visibility.Collapsed;

        var activeHumans = session.Participants.Where(p => !p.IsAiCharacter && (p.IsHost || p.IsConnected)).ToList();
        var readyHumans = activeHumans.Count(p => p.IsReady);
        ReadyForTurnBtn.Content = $"✓ Ready ({readyHumans}/{Math.Max(1, activeHumans.Count)})";

        LocationBox.Text = session.CurrentLocation;
        AuthorsNoteBox.Text = session.AuthorsNote;
        RunningSummaryBox.Text = session.RunningSummary;

        int wordCount = session.Messages.Sum(m => m.Content.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        ArchiveStatsText.Text = $"Total Messages: {session.Messages.Count} • Words: {wordCount:N0} • Est. Tokens: {(int)(wordCount * 1.3):N0} • Turn: #{session.TurnNumber}";

        // Whisper targets
        WhisperTargetCombo.Items.Clear();
        foreach (var p in session.Participants.Where(x => !x.IsAiCharacter))
        {
            WhisperTargetCombo.Items.Add(new ComboBoxItem { Content = p.DisplayName, Tag = p.ParticipantId });
        }
        if (WhisperTargetCombo.Items.Count > 0) WhisperTargetCombo.SelectedIndex = 0;

        // Render World State Variables
        WorldStateVariablesPanel.Children.Clear();
        foreach (var kv in session.ActiveWorldState)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock { Text = kv.Key, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var valText = new TextBlock { Text = kv.Value, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(valText, 1);
            row.Children.Add(valText);

            var delBtn = new Button { Content = "×", Padding = new Thickness(6, 2, 6, 2) };
            var capturedKey = kv.Key;
            delBtn.Click += (_, _) =>
            {
                session.ActiveWorldState.Remove(capturedKey);
                ctx.Repository.SaveSession(session);
                RefreshSessionWorkspace();
            };
            Grid.SetColumn(delBtn, 2);
            row.Children.Add(delBtn);

            WorldStateVariablesPanel.Children.Add(row);
        }

        // Render Participants (Partitioned: Human Players vs AI Cast)
        ParticipantsPanel.Children.Clear();
        bool isMultiplayerActive = App.Platform.ActiveHostServer?.IsRunning == true || App.Platform.ActiveClient?.IsConnected == true;
        var humans = session.Participants.Where(p => !p.IsAiCharacter && (p.IsHost || p.ParticipantId == "host-player" || isMultiplayerActive)).ToList();
        var aiCast = session.Participants.Where(p => p.IsAiCharacter).ToList();

        ParticipantsPanel.Children.Add(new TextBlock
        {
            Text = $"👥 Party Members ({humans.Count})",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });

        foreach (var p in humans)
        {
            bool isTurn = session.GameMode == RoleplayGameMode.TurnBased && p.ParticipantId == turnHolderId;
            bool isOnline = p.IsHost || (isMultiplayerActive && p.IsConnected);
            var statusDot = isOnline ? "●" : "○";
            var roleTag = p.IsHost ? "Host" : (p.ParticipantId.StartsWith("mobile") ? "Mobile" : "Co-Op");
            var turnTag = isTurn ? " [ACTIVE TURN]" : "";
            var charDetail = !string.IsNullOrWhiteSpace(p.PersonaName) ? $"Playing as: {p.PersonaName}" : "No character";

            var card = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{statusDot} {p.DisplayName} [{roleTag}]{turnTag}",
                            FontSize = 12,
                            FontWeight = isTurn ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = charDetail,
                            FontSize = 11,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    }
                }
            };
            ParticipantsPanel.Children.Add(card);
        }

        if (aiCast.Count > 0)
        {
            ParticipantsPanel.Children.Add(new TextBlock
            {
                Text = $"🤖 AI Cast in Room ({aiCast.Count})",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            foreach (var ai in aiCast)
            {
                var card = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 0, 4),
                    Child = new TextBlock
                    {
                        Text = $"🤖 {ai.DisplayName} (AI Character)",
                        FontSize = 11,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    }
                };
                ParticipantsPanel.Children.Add(card);
            }
        }

        // Apply accessibility font to MessageInputBox
        var acc = AccessibilityManager.Current;
        if (MessageInputBox != null)
        {
            MessageInputBox.FontSize = acc.EffectiveFontSize;
            MessageInputBox.FontFamily = acc.GetConfiguredFontFamily();
            MessageInputBox.MinHeight = Math.Max(56, acc.ZoomScale(56));
        }

        // Render Transcript Messages
        MessagesListPanel.Children.Clear();
        foreach (var msg in session.Messages)
        {
            MessagesListPanel.Children.Add(CreateMessageCard(session, msg));
        }

        TranscriptScroll.ChangeView(null, TranscriptScroll.ScrollableHeight + 500, null);
    }

    private UIElement CreateMessageCard(RoleplaySession session, SessionMessage msg)
    {
        var acc = AccessibilityManager.Current;
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = acc.HighContrastCards ? new Thickness(2) : new Thickness(1),
            BorderBrush = acc.HighContrastCards
                ? (msg.Role == "assistant"
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue)
                    : (msg.Role == "system" || msg.Role == "narrator"
                        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gold)
                        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.MediumPurple)))
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Padding = new Thickness(acc.ZoomScale(14), acc.ZoomScale(10), acc.ZoomScale(14), acc.ZoomScale(10))
        };

        var stack = new StackPanel { Spacing = 6 };

        // Header Row
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var roleBadge = msg.IsPrivateWhisper
            ? " [Private Whisper]"
            : (msg.Role == "narrator" ? " [Narrator]" : $" [{msg.ActionType}]");

        var titleText = new TextBlock
        {
            Text = $"{msg.SenderName}{roleBadge} • {msg.Timestamp.ToLocalTime():HH:mm}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = Math.Max(12, acc.EffectiveFontSize - 2),
            FontFamily = acc.GetConfiguredFontFamily()
        };
        header.Children.Add(titleText);

        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        // Alternatives / Swipes
        if (msg.Alternatives.Count > 1)
        {
            var prevBtn = new Button { Content = "◀", Padding = new Thickness(6, 2, 6, 2) };
            prevBtn.Click += (_, _) =>
            {
                if (msg.ActiveAlternativeIndex > 0)
                {
                    msg.ActiveAlternativeIndex--;
                    msg.Content = msg.Alternatives[msg.ActiveAlternativeIndex];
                    App.Platform.Repository.SaveSession(session);
                    RefreshSessionWorkspace();
                }
            };
            actionsRow.Children.Add(prevBtn);

            actionsRow.Children.Add(new TextBlock
            {
                Text = $"{msg.ActiveAlternativeIndex + 1}/{msg.Alternatives.Count}",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            var nextBtn = new Button { Content = "▶", Padding = new Thickness(6, 2, 6, 2) };
            nextBtn.Click += (_, _) =>
            {
                if (msg.ActiveAlternativeIndex < msg.Alternatives.Count - 1)
                {
                    msg.ActiveAlternativeIndex++;
                    msg.Content = msg.Alternatives[msg.ActiveAlternativeIndex];
                    App.Platform.Repository.SaveSession(session);
                    RefreshSessionWorkspace();
                }
            };
            actionsRow.Children.Add(nextBtn);
        }

        if (acc.EnableTtsNarration && !string.IsNullOrWhiteSpace(msg.Content))
        {
            var ttsBtn = new Button { Content = "🔊", Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
            ToolTipService.SetToolTip(ttsBtn, "Read aloud (Text-to-Speech)");
            ttsBtn.Click += async (_, _) =>
            {
                await acc.SpeakTextAsync(msg.Content);
            };
            actionsRow.Children.Add(ttsBtn);
        }

        var transBtn = new Button { Content = "🌐 TR", Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
        ToolTipService.SetToolTip(transBtn, "Translate to Turkish");
        actionsRow.Children.Add(transBtn);

        var editBtn = new Button { Content = "Edit", Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
        editBtn.Click += async (_, _) => await EditMessageDialogAsync(session, msg);
        actionsRow.Children.Add(editBtn);

        var delBtn = new Button { Content = "Delete", Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
        delBtn.Click += (_, _) =>
        {
            session.Messages.Remove(msg);
            App.Platform.Repository.SaveSession(session);
            RefreshSessionWorkspace();
        };
        actionsRow.Children.Add(delBtn);

        Grid.SetColumn(actionsRow, 1);
        header.Children.Add(actionsRow);
        stack.Children.Add(header);

        // Message Body
        var bodyText = new TextBlock
        {
            Text = msg.Content,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = acc.EffectiveFontSize,
            FontFamily = acc.GetConfiguredFontFamily(),
            LineHeight = acc.GetCalculatedLineHeight()
        };
        stack.Children.Add(bodyText);

        // Translation Card
        var transCard = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            Padding = new Thickness(acc.ZoomScale(10), acc.ZoomScale(8), acc.ZoomScale(10), acc.ZoomScale(8)),
            Margin = new Thickness(0, 6, 0, 0),
            BorderThickness = acc.HighContrastCards ? new Thickness(1.5) : new Thickness(1),
            BorderBrush = acc.HighContrastCards
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue)
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Visibility = Visibility.Collapsed
        };

        var transStack = new StackPanel { Spacing = 4 };
        var transHeader = new Grid();
        transHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        transHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var transBadge = new TextBlock
        {
            Text = "🌐 Translation",
            FontSize = Math.Max(11, acc.EffectiveFontSize - 3),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(transBadge, 0);
        transHeader.Children.Add(transBadge);

        Button? transTtsBtn = null;
        if (acc.EnableTtsNarration)
        {
            transTtsBtn = new Button
            {
                Content = "🔊",
                Padding = new Thickness(6, 1, 6, 1),
                FontSize = 11,
                Visibility = Visibility.Collapsed
            };
            ToolTipService.SetToolTip(transTtsBtn, "Read translation aloud (Text-to-Speech)");
            Grid.SetColumn(transTtsBtn, 1);
            transHeader.Children.Add(transTtsBtn);
        }
        transStack.Children.Add(transHeader);

        var transText = new TextBlock
        {
            Text = "",
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            FontSize = acc.EffectiveFontSize,
            FontFamily = acc.GetConfiguredFontFamily(),
            LineHeight = acc.GetCalculatedLineHeight(),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        transStack.Children.Add(transText);
        transCard.Child = transStack;
        stack.Children.Add(transCard);

        async Task PerformTranslationAsync(string targetLang)
        {
            try
            {
                transBtn.IsEnabled = false;
                var res = await App.Platform.Translation.TranslateAsync(msg.Content, targetLang);
                var langLabel = targetLang == "tr" ? "🇹🇷 Turkish" : (targetLang == "en" ? "🇬🇧 English" : targetLang);
                transBadge.Text = $"🌐 [{langLabel}]";
                transText.Text = res;
                if (transTtsBtn != null)
                {
                    transTtsBtn.Visibility = Visibility.Visible;
                    transTtsBtn.Click += async (_, _) =>
                    {
                        await acc.SpeakTextAsync(res);
                    };
                }
                transCard.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                transBadge.Text = "🌐 [Translation Error]";
                transText.Text = ex.Message;
                transCard.Visibility = Visibility.Visible;
            }
            finally
            {
                transBtn.IsEnabled = true;
            }
        }

        transBtn.Click += async (_, _) =>
        {
            if (transCard.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(_autoTranslateTargetLanguage))
            {
                transCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                var target = _autoTranslateTargetLanguage ?? "tr";
                await PerformTranslationAsync(target);
            }
        };

        if (!string.IsNullOrWhiteSpace(_autoTranslateTargetLanguage))
        {
            _ = PerformTranslationAsync(_autoTranslateTargetLanguage);
        }

        card.Child = stack;
        return card;
    }

    private async Task EditMessageDialogAsync(RoleplaySession session, SessionMessage msg)
    {
        var editBox = new TextBox
        {
            Text = msg.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 140,
            Width = 460
        };

        var dlg = new ContentDialog
        {
            Title = $"Edit Message — {msg.SenderName}",
            Content = editBox,
            PrimaryButtonText = "Save Changes",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            msg.Content = editBox.Text.Trim();
            if (msg.Alternatives.Count > msg.ActiveAlternativeIndex)
            {
                msg.Alternatives[msg.ActiveAlternativeIndex] = msg.Content;
            }
            App.Platform.Repository.SaveSession(session);
            RefreshSessionWorkspace();
        }
    }

    private async void SendAction_Click(object sender, RoutedEventArgs e)
    {
        var text = MessageInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var actionTag = (ActionTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Speak";
        if (!Enum.TryParse<RoleplayActionType>(actionTag, out var actionType))
            actionType = RoleplayActionType.Speak;

        bool isWhisper = PrivateWhisperCheck.IsChecked == true;
        string? whisperTarget = (WhisperTargetCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        MessageInputBox.Text = string.Empty;
        var ctx = App.Platform;

        // 1. If Connected as Multiplayer Client -> Submit to authoritative Host
        if (ctx.ActiveClient?.IsConnected == true)
        {
            try
            {
                await ctx.ActiveClient.SendActionAsync(actionType, text, isWhisper, whisperTarget);
            }
            catch (Exception ex)
            {
                App.MainWindowInstance.ShowNotification("Multiplayer Error", ex.Message, InfoBarSeverity.Error);
            }
            return;
        }

        // 2. If Running as Multiplayer Host -> Process on Host & broadcast to all clients
        if (ctx.ActiveHostServer?.IsRunning == true)
        {
            await ctx.ActiveHostServer.SubmitHostActionAsync(actionType, text, isWhisper, whisperTarget);
            ctx.Repository.SaveSession(ctx.ActiveHostServer.ActiveSession);
            RefreshSessionWorkspace();
            return;
        }

        // 3. Single-Player Local Session
        var session = GetEffectiveSession();
        var validation = ctx.Engine.ValidateAndApplyPlayerAction(
            session,
            "host-player",
            actionType,
            text,
            out _,
            isWhisper,
            whisperTarget);

        if (!validation.IsAllowed)
        {
            App.MainWindowInstance.ShowNotification("Action Blocked by Mode Rules", validation.DenialReason ?? "Not allowed.", InfoBarSeverity.Warning);
            return;
        }

        ctx.Repository.SaveSession(session);
        RefreshSessionWorkspace();

        if (validation.ShouldTriggerNarrator)
        {
            await StreamSinglePlayerAiAsync(session, isNarrator: true);
        }
        else if (validation.ShouldTriggerAiCharacter)
        {
            await StreamSinglePlayerAiAsync(session, isNarrator: false);
        }
    }

    private async Task StreamSinglePlayerAiAsync(RoleplaySession session, bool isNarrator, SessionMessage? existingSwipeMessage = null, bool isRoundResolution = false)
    {
        var ctx = App.Platform;
        var activeCharId = session.ActiveCharacterIds.FirstOrDefault();
        var activeChar = _characters.FirstOrDefault(c => c.Id == activeCharId);
        if (activeChar == null || isRoundResolution)
        {
            isNarrator = true;
        }

        var persona = _personas.FirstOrDefault(p => p.Id == session.ActivePersonaId) ?? ctx.GetDefaultPersona();
        var world = ctx.Repository.GetAllWorlds().FirstOrDefault(w => w.Id == session.WorldId);
        var presetId = activeChar?.PreferredModelPresetId ?? session.ActiveModelPresetId;
        var preset = _presets.FirstOrDefault(m => m.Id == presetId) ?? ctx.GetDefaultModelPreset();

        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();

        SendActionBtn.IsEnabled = false;
        StopStreamBtn.IsEnabled = true;
        var responderName = isNarrator ? "Narrator" : activeChar!.Name;
        StreamingStatusText.Text = $"Streaming from {preset.ProviderType} ({preset.ModelId})...";

        // Add live streaming card to UI
        _liveStreamTextBlock = new TextBlock
        {
            Text = "▋",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22
        };

        var streamCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Padding = new Thickness(14, 10, 14, 10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = $"{responderName} (Streaming...)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    _liveStreamTextBlock
                }
            }
        };
        MessagesListPanel.Children.Add(streamCard);
        TranscriptScroll.ChangeView(null, TranscriptScroll.ScrollableHeight + 500, null);

        var sb = new StringBuilder();
        try
        {
            await foreach (var delta in ctx.AiOrchestrator.StreamSessionResponseAsync(
                session, activeChar, persona, world, preset, isNarrator, isRoundResolution, _streamCts.Token))
            {
                sb.Append(delta);
                _liveStreamTextBlock.Text = sb.ToString();
                TranscriptScroll.ChangeView(null, TranscriptScroll.ScrollableHeight + 500, null);
            }
        }
        catch (OperationCanceledException)
        {
            sb.Append(" [Stopped]");
        }
        catch (Exception ex)
        {
            sb.Append($"\n[Generation Error: {ex.Message}]");
        }
        finally
        {
            var finalText = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                if (existingSwipeMessage != null)
                {
                    existingSwipeMessage.Alternatives.Add(finalText);
                    existingSwipeMessage.ActiveAlternativeIndex = existingSwipeMessage.Alternatives.Count - 1;
                    existingSwipeMessage.Content = finalText;
                }
                else
                {
                    ctx.Engine.AppendAiMessage(session, activeChar?.Id ?? "narrator", responderName, finalText, isNarrator);
                }
                ctx.Repository.SaveSession(session);
            }

            SendActionBtn.IsEnabled = true;
            StopStreamBtn.IsEnabled = false;
            StreamingStatusText.Text = "Ready";
            RefreshSessionWorkspace();
        }
    }

    private void StopStream_Click(object sender, RoutedEventArgs e)
    {
        _streamCts?.Cancel();
    }

    private async void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        var session = GetEffectiveSession();
        var lastMsg = session.Messages.LastOrDefault();
        if (lastMsg != null && lastMsg.Role is "assistant" or "narrator")
        {
            // Temporarily remove last message from prompt context while generating new swipe
            session.Messages.RemoveAt(session.Messages.Count - 1);
            await StreamSinglePlayerAiAsync(session, lastMsg.Role == "narrator", existingSwipeMessage: lastMsg);
            if (!session.Messages.Contains(lastMsg))
                session.Messages.Add(lastMsg);
            App.Platform.Repository.SaveSession(session);
            RefreshSessionWorkspace();
        }
        else
        {
            await StreamSinglePlayerAiAsync(session, isNarrator: false);
        }
    }

    private async void TriggerNarrator_Click(object sender, RoutedEventArgs e)
    {
        var ctx = App.Platform;
        if (ctx.ActiveHostServer?.IsRunning == true)
        {
            await ctx.ActiveHostServer.TriggerAuthoritativeAiGenerationAsync(isNarrator: true);
            return;
        }
        await StreamSinglePlayerAiAsync(GetEffectiveSession(), isNarrator: true);
    }

    private async void RequestAiReply_Click(object sender, RoutedEventArgs e)
    {
        var ctx = App.Platform;
        var session = GetEffectiveSession();
        bool useNarrator = session.ActiveCharacterIds.Count == 0;
        if (ctx.ActiveHostServer?.IsRunning == true)
        {
            await ctx.ActiveHostServer.TriggerAuthoritativeAiGenerationAsync(isNarrator: useNarrator);
            return;
        }
        await StreamSinglePlayerAiAsync(session, isNarrator: useNarrator);
    }

    private void AdvanceTurn_Click(object sender, RoutedEventArgs e)
    {
        var session = GetEffectiveSession();
        App.Platform.Engine.AdvanceTurn(session);
        App.Platform.Repository.SaveSession(session);
        RefreshSessionWorkspace();
    }

    private string? _autoTranslateTargetLanguage = null;

    private void TranslateModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (TranslateModeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _autoTranslateTargetLanguage = tag == "off" ? null : tag;
            RefreshSessionWorkspace();
        }
    }

    private async void TranslateInputBox_Click(object sender, RoutedEventArgs e)
    {
        var text = MessageInputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var translated = await App.Platform.Translation.TranslateAsync(text, "tr");
            MessageInputBox.Text = translated;
            App.MainWindowInstance.ShowNotification("Translated to Turkish 🇹🇷", translated, InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance.ShowNotification("Translation Error", ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async void ReadyForTurn_Click(object sender, RoutedEventArgs e)
    {
        var ctx = App.Platform;
        if (ctx.ActiveHostServer?.IsRunning == true)
        {
            var hostParticipant = ctx.ActiveHostServer.ActiveSession.Participants.FirstOrDefault(p => p.IsHost);
            if (hostParticipant != null)
            {
                await ctx.ActiveHostServer.ToggleParticipantReadyAsync(hostParticipant.ParticipantId);
                var activeHumans = ctx.ActiveHostServer.ActiveSession.Participants.Where(x => !x.IsAiCharacter && x.IsConnected).ToList();
                if (activeHumans.Count <= 1)
                {
                    // Solo host mode: directly resolve round & narrate consequences
                    await ctx.ActiveHostServer.ResolveRoundAndNarrateConsequencesAsync();
                }
            }
            return;
        }

        if (ctx.ActiveClient?.IsConnected == true)
        {
            await ctx.ActiveClient.ToggleReadyAsync();
            return;
        }

        // Single player offline mode: advance turn & trigger round resolution continuation
        var session = GetEffectiveSession();
        ctx.Engine.AdvanceTurn(session);
        ctx.Repository.SaveSession(session);
        RefreshSessionWorkspace();
        await StreamSinglePlayerAiAsync(session, isNarrator: true, isRoundResolution: true);
    }

    private void SessionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        if (SessionCombo.SelectedItem is ComboBoxItem item && item.Tag is string sid)
        {
            var found = _sessions.FirstOrDefault(s => s.SessionId == sid);
            if (found != null)
            {
                App.Platform.ActiveSession = found;
                LoadTopSelectors();
                RefreshSessionWorkspace();
            }
        }
    }

    private void GameModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        if (GameModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string mStr &&
            Enum.TryParse<RoleplayGameMode>(mStr, out var mode))
        {
            var session = GetEffectiveSession();
            session.GameMode = mode;
            if (mode == RoleplayGameMode.TwoPersonPrivate)
                session.MaxPlayers = 2;
            App.Platform.Repository.SaveSession(session);
            RefreshSessionWorkspace();
        }
    }

    private async void CharacterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        if (CharacterCombo.SelectedItem is ComboBoxItem item && item.Tag is string cid)
        {
            var session = GetEffectiveSession();
            session.ActiveCharacterIds.Clear();
            if (!string.IsNullOrWhiteSpace(cid))
                session.ActiveCharacterIds.Add(cid);

            if (App.Platform.ActiveClient?.IsConnected == true)
            {
                var myId = App.Platform.ActiveClient.AssignedParticipantId;
                var myPart = session.Participants.FirstOrDefault(x => x.ParticipantId == myId);
                if (myPart != null)
                {
                    myPart.CharacterId = cid;
                }
                await App.Platform.ActiveClient.UpdateIdentityAsync(myPart?.PersonaName ?? "Player", characterId: cid);
            }
            else if (App.Platform.ActiveHostServer?.IsRunning == true)
            {
                App.Platform.Repository.SaveSession(session);
                var charObj = _characters.FirstOrDefault(c => c.Id == cid);
                if (charObj != null)
                {
                    await App.Platform.ActiveHostServer.SyncCharacterFromHostAsync(charObj);
                }
            }
            else
            {
                App.Platform.Repository.SaveSession(session);
            }
            RefreshSessionWorkspace();
        }
    }

    private async void PersonaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        if (PersonaCombo.SelectedItem is ComboBoxItem item && item.Tag is string pid)
        {
            var session = GetEffectiveSession();
            session.ActivePersonaId = pid;
            var p = _personas.FirstOrDefault(x => x.Id == pid);
            var charMatch = _characters.FirstOrDefault(c => c.Id == pid || string.Equals(c.Name, pid, StringComparison.OrdinalIgnoreCase));
            string chosenName = p?.Name ?? charMatch?.Name ?? pid;
            string chosenDesc = p?.Description ?? charMatch?.Description ?? "";

            // If Connected as Multiplayer Client:
            if (App.Platform.ActiveClient?.IsConnected == true)
            {
                var myId = App.Platform.ActiveClient.AssignedParticipantId;
                var myPart = session.Participants.FirstOrDefault(x => x.ParticipantId == myId);
                if (myPart != null)
                {
                    myPart.PersonaId = pid;
                    myPart.PersonaName = chosenName;
                    myPart.DisplayName = chosenName;
                    myPart.PersonaSummary = chosenDesc;
                    myPart.CharacterId = charMatch?.Id ?? myPart.CharacterId;
                }
                await App.Platform.ActiveClient.UpdateIdentityAsync(chosenName, pid, chosenDesc, charMatch?.Id);
            }
            else if (App.Platform.ActiveHostServer?.IsRunning == true)
            {
                // If Running as Host:
                var hostPart = session.Participants.FirstOrDefault(x => x.IsHost || x.ParticipantId == "host-player");
                if (hostPart != null)
                {
                    hostPart.PersonaId = pid;
                    hostPart.PersonaName = chosenName;
                    hostPart.DisplayName = chosenName;
                    hostPart.PersonaSummary = chosenDesc;
                    hostPart.CharacterId = charMatch?.Id;
                }
                if (p != null) App.Platform.ActiveHostServer.HostPersona = p;
                await App.Platform.ActiveHostServer.UpdateParticipantIdentityAsync(
                    "host-player",
                    chosenName,
                    chosenName,
                    personaId: pid,
                    personaSummary: chosenDesc,
                    characterId: charMatch?.Id);
                App.Platform.Repository.SaveSession(session);
            }
            else
            {
                // Single-Player Local Session
                var hostPart = session.Participants.FirstOrDefault(x => x.IsHost || x.ParticipantId == "host-player");
                if (hostPart != null)
                {
                    hostPart.PersonaId = pid;
                    hostPart.PersonaName = chosenName;
                    hostPart.DisplayName = chosenName;
                }
                App.Platform.Repository.SaveSession(session);
            }
            RefreshSessionWorkspace();
        }
    }

    private void ModelPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        if (ModelPresetCombo.SelectedItem is ComboBoxItem item && item.Tag is string mid)
        {
            var session = GetEffectiveSession();
            session.ActiveModelPresetId = mid;
            var preset = _presets.FirstOrDefault(x => x.Id == mid);
            if (preset != null && App.Platform.ActiveHostServer != null)
            {
                App.Platform.ActiveHostServer.ActiveModelPreset = preset;
            }
            App.Platform.Repository.SaveSession(session);
            App.MainWindowInstance.ShowNotification("Model Updated", $"Session model preset switched to '{preset?.Name}'.", InfoBarSeverity.Success);
        }
    }

    private void TwoPersonAiToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        var session = GetEffectiveSession();
        session.AllowAiInTwoPersonMode = TwoPersonAiToggle.IsChecked == true;
        App.Platform.Repository.SaveSession(session);
    }

    private void PrivateWhisperCheck_Changed(object sender, RoutedEventArgs e)
    {
        WhisperTargetCombo.IsEnabled = PrivateWhisperCheck.IsChecked == true;
    }

    private void UpdateLocation_Click(object sender, RoutedEventArgs e)
    {
        var session = GetEffectiveSession();
        var world = App.Platform.Repository.GetAllWorlds().FirstOrDefault(w => w.Id == session.WorldId);
        App.Platform.Engine.UpdateWorldStateVariable(session, world, "Location", LocationBox.Text.Trim(), "Manual scene update");
        if (world != null) App.Platform.Repository.SaveWorld(world);
        App.Platform.Repository.SaveSession(session);
        RefreshSessionWorkspace();
    }

    private void AddWorldStateVar_Click(object sender, RoutedEventArgs e)
    {
        var key = NewStateKeyBox.Text.Trim();
        var val = NewStateValBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key)) return;

        var session = GetEffectiveSession();
        var world = App.Platform.Repository.GetAllWorlds().FirstOrDefault(w => w.Id == session.WorldId);
        App.Platform.Engine.UpdateWorldStateVariable(session, world, key, val, "Player/GM state update");
        if (world != null) App.Platform.Repository.SaveWorld(world);
        App.Platform.Repository.SaveSession(session);

        NewStateKeyBox.Text = string.Empty;
        NewStateValBox.Text = string.Empty;
        RefreshSessionWorkspace();
    }

    private void AuthorsNoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        var session = GetEffectiveSession();
        session.AuthorsNote = AuthorsNoteBox.Text;
        App.Platform.Repository.SaveSession(session);
    }

    private void RunningSummaryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSelectors) return;
        var session = GetEffectiveSession();
        session.RunningSummary = RunningSummaryBox.Text;
        App.Platform.Repository.SaveSession(session);
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        var ctx = App.Platform;
        var persona = ctx.GetDefaultPersona();
        var world = ctx.Repository.GetAllWorlds().FirstOrDefault();
        var chars = ctx.Repository.GetAllCharacters().Take(1).ToList();
        var session = ctx.Engine.InitializeSession(
            $"Session {DateTime.Now:MMM dd HH:mm}",
            RoleplayGameMode.Freeform,
            persona,
            world,
            chars,
            ctx.GetDefaultModelPreset());

        ctx.Repository.SaveSession(session);
        ctx.ActiveSession = session;
        LoadTopSelectors();
        RefreshSessionWorkspace();
    }

    private async void ExportSession_Click(object sender, RoutedEventArgs e)
    {
        var session = GetEffectiveSession();
        var includeHistoryChk = new CheckBox { Content = "Include Conversation History", IsChecked = true };
        var includeWhispersChk = new CheckBox { Content = "Include Private Whispers", IsChecked = false };
        var includeStateChk = new CheckBox { Content = "Include Structured World State", IsChecked = true };

        var dlg = new ContentDialog
        {
            Title = "Export Session (.rpsession) Privacy Options",
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { includeHistoryChk, includeWhispersChk, includeStateChk }
            },
            PrimaryButtonText = "Choose Save Location",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var path = await NativePickerHelper.PickSaveFileAsync(
            App.MainWindowInstance,
            session.Title.Replace(' ', '_'),
            "Aetheria Portable Session",
            RoleplayFormatConstants.SessionExtension);

        if (!string.IsNullOrWhiteSpace(path))
        {
            var json = RoleplayFormatSerializer.ExportSession(session, new SessionExportOptions
            {
                IncludeChatHistory = includeHistoryChk.IsChecked == true,
                IncludePrivateWhispers = includeWhispersChk.IsChecked == true,
                IncludeWorldState = includeStateChk.IsChecked == true
            });
            await File.WriteAllTextAsync(path, json);
            App.MainWindowInstance.ShowNotification("Session Exported", $"Saved to {path}", InfoBarSeverity.Success);
        }
    }

    private async void RollDiceItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && int.TryParse(item.Tag as string, out int sides))
        {
            var roll = Random.Shared.Next(1, sides + 1);
            var diceDesc = $"*rolls a d{sides} -> Result: {roll}*";
            var ctx = App.Platform;

            if (ctx.ActiveClient?.IsConnected == true)
            {
                await ctx.ActiveClient.SendActionAsync(RoleplayActionType.Speak, diceDesc);
            }
            else if (ctx.ActiveHostServer?.IsRunning == true)
            {
                await ctx.ActiveHostServer.SubmitHostActionAsync(RoleplayActionType.Speak, diceDesc);
                ctx.Repository.SaveSession(ctx.ActiveHostServer.ActiveSession);
                RefreshSessionWorkspace();
            }
            else
            {
                var session = GetEffectiveSession();
                ctx.Engine.ValidateAndApplyPlayerAction(
                    session,
                    "host-player",
                    RoleplayActionType.Speak,
                    diceDesc,
                    out _,
                    isPrivateWhisper: false,
                    targetParticipantId: null);
                ctx.Repository.SaveSession(session);
                RefreshSessionWorkspace();
                await StreamSinglePlayerAiAsync(session, isNarrator: false);
            }
            App.MainWindowInstance.ShowNotification("🎲 Tabletop Die Rolled", $"Rolled d{sides}: {roll}", InfoBarSeverity.Informational);
        }
    }

    private void SaveBranchFork_Click(object sender, RoutedEventArgs e)
    {
        var title = NewBranchTitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"Checkpoint #{BranchCheckpointsListView.Items.Count + 1}";
        }
        NewBranchTitleBox.Text = string.Empty;

        var session = GetEffectiveSession();
        var fork = RoleplayFormatSerializer.ExportSession(session, new SessionExportOptions
        {
            IncludeChatHistory = true,
            IncludePrivateWhispers = true,
            IncludeWorldState = true
        });

        var btn = new Button
        {
            Content = $"Branch: {title} ({session.Messages.Count} msgs)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4)
        };
        btn.Click += (_, _) =>
        {
            var res = RoleplayFormatSerializer.ImportSession(fork);
            if (res.IsValid && res.Data != null)
            {
                var restored = res.Data;
                restored.SessionId = session.SessionId;
                restored.Title = $"{session.Title} ({title})";
                App.Platform.Repository.SaveSession(restored);
                App.Platform.ActiveSession = restored;
                RefreshSessionWorkspace();
                App.MainWindowInstance.ShowNotification("Branch Loaded", $"Rewound story state to '{title}'!", InfoBarSeverity.Success);
            }
        };

        BranchCheckpointsListView.Items.Insert(0, btn);
        App.MainWindowInstance.ShowNotification("Branch Saved", $"Fork point '{title}' saved with {session.Messages.Count} messages.", InfoBarSeverity.Success);
    }

    private void QuickRollD20_Click(object sender, RoutedEventArgs e)
    {
        var roll = Random.Shared.Next(1, 21);
        var diceDesc = $"*rolls a d20 -> Result: {roll}*";
        var ctx = App.Platform;

        if (ctx.ActiveClient?.IsConnected == true)
        {
            _ = ctx.ActiveClient.SendActionAsync(RoleplayActionType.Speak, diceDesc);
        }
        else if (ctx.ActiveHostServer?.IsRunning == true)
        {
            _ = ctx.ActiveHostServer.SubmitHostActionAsync(RoleplayActionType.Speak, diceDesc);
            ctx.Repository.SaveSession(ctx.ActiveHostServer.ActiveSession);
            RefreshSessionWorkspace();
        }
        else
        {
            var session = GetEffectiveSession();
            ctx.Engine.ValidateAndApplyPlayerAction(
                session,
                "host-player",
                RoleplayActionType.Speak,
                diceDesc,
                out _,
                isPrivateWhisper: false,
                targetParticipantId: null);
            ctx.Repository.SaveSession(session);
            RefreshSessionWorkspace();
            _ = StreamSinglePlayerAiAsync(session, isNarrator: false);
        }
        App.MainWindowInstance.ShowNotification("🎲 d20 Rolled", $"Result: Rolled d20 -> Total: {roll}", InfoBarSeverity.Informational);
    }

    private void QuickActionChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            for (int i = 0; i < ActionTypeCombo.Items.Count; i++)
            {
                if (ActionTypeCombo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    ActionTypeCombo.SelectedIndex = i;
                    break;
                }
            }
            MessageInputBox.Focus(FocusState.Programmatic);
        }
    }

    private void SearchHistoryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchHistoryBox.Text.Trim();
        SearchResultsListView.Items.Clear();
        if (string.IsNullOrWhiteSpace(query)) return;

        var session = GetEffectiveSession();
        var matches = session.Messages
            .Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase) || m.SenderName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .ToList();

        foreach (var m in matches)
        {
            SearchResultsListView.Items.Add(new TextBlock
            {
                Text = $"#{m.SequenceNumber} [{m.SenderName}]: {m.Content}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
    }

    private void InspectorTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            PanelParty.Visibility = string.Equals(tag, "Party", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            PanelBranches.Visibility = string.Equals(tag, "Branches", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            PanelWorld.Visibility = string.Equals(tag, "World", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            PanelArchive.Visibility = string.Equals(tag, "Archive", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

            TabBtnParty.Opacity = string.Equals(tag, "Party", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.65;
            TabBtnBranches.Opacity = string.Equals(tag, "Branches", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.65;
            TabBtnWorld.Opacity = string.Equals(tag, "World", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.65;
            TabBtnArchive.Opacity = string.Equals(tag, "Archive", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.65;
        }
    }
}

