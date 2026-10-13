using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Aetheria.App.Helpers;

public sealed class AccessibilityManager
{
    private static AccessibilityManager? _instance;
    public static AccessibilityManager Current => _instance ??= new AccessibilityManager();

    private MediaPlayer? _activePlayer;
    private SpeechSynthesizer? _activeSynth;

    public event Action? AccessibilitySettingsChanged;

    public double ZoomFactor { get; private set; } = 1.0;
    public int RoleplayFontSize { get; private set; } = 14;
    public string RoleplayFontFamily { get; private set; } = "Default";
    public bool ExpandedLineSpacing { get; private set; } = false;
    public bool HighContrastCards { get; private set; } = false;
    public bool ReduceMotion { get; private set; } = false;
    public bool EnableTtsNarration { get; private set; } = true;

    private AccessibilityManager()
    {
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            var repo = App.Platform?.Repository;
            if (repo == null) return;

            if (int.TryParse(repo.GetSetting("UiZoomPercent", "100"), out var zoomPct))
            {
                ZoomFactor = Math.Clamp(zoomPct / 100.0, 0.75, 2.0);
            }
            else
            {
                ZoomFactor = 1.0;
            }

            if (int.TryParse(repo.GetSetting("RoleplayFontSize", "14"), out var fSize))
            {
                RoleplayFontSize = Math.Clamp(fSize, 12, 28);
            }
            else
            {
                RoleplayFontSize = 14;
            }

            RoleplayFontFamily = repo.GetSetting("RoleplayFontFamily", "Default");
            ExpandedLineSpacing = repo.GetSetting("ExpandedLineSpacing", "false") == "true";
            HighContrastCards = repo.GetSetting("HighContrastCards", "false") == "true";
            ReduceMotion = repo.GetSetting("ReduceMotion", "false") == "true";
            EnableTtsNarration = repo.GetSetting("EnableTtsNarration", "true") == "true";
        }
        catch
        {
            // Non-fatal fallback defaults
        }
    }

    public void SetZoomFactor(double factor, bool notify = true)
    {
        ZoomFactor = Math.Clamp(factor, 0.75, 2.0);
        int pct = (int)Math.Round(ZoomFactor * 100);
        App.Platform?.Repository.SetSetting("UiZoomPercent", pct.ToString());
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public void SetRoleplayFontSize(int size, bool notify = true)
    {
        RoleplayFontSize = Math.Clamp(size, 12, 28);
        App.Platform?.Repository.SetSetting("RoleplayFontSize", RoleplayFontSize.ToString());
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public void SetRoleplayFontFamily(string family, bool notify = true)
    {
        RoleplayFontFamily = string.IsNullOrWhiteSpace(family) ? "Default" : family;
        App.Platform?.Repository.SetSetting("RoleplayFontFamily", RoleplayFontFamily);
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public void SetExpandedLineSpacing(bool enabled, bool notify = true)
    {
        ExpandedLineSpacing = enabled;
        App.Platform?.Repository.SetSetting("ExpandedLineSpacing", enabled ? "true" : "false");
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public void SetHighContrastCards(bool enabled, bool notify = true)
    {
        HighContrastCards = enabled;
        App.Platform?.Repository.SetSetting("HighContrastCards", enabled ? "true" : "false");
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public void SetReduceMotion(bool enabled, bool notify = true)
    {
        ReduceMotion = enabled;
        App.Platform?.Repository.SetSetting("ReduceMotion", enabled ? "true" : "false");
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public void SetEnableTtsNarration(bool enabled, bool notify = true)
    {
        EnableTtsNarration = enabled;
        App.Platform?.Repository.SetSetting("EnableTtsNarration", enabled ? "true" : "false");
        if (notify) AccessibilitySettingsChanged?.Invoke();
    }

    public FontFamily GetConfiguredFontFamily()
    {
        return RoleplayFontFamily switch
        {
            "Dyslexic" => new FontFamily("Comic Sans MS, Segoe UI"),
            "Monospace" => new FontFamily("Consolas, Courier New"),
            "SansSerif" => new FontFamily("Arial, Segoe UI"),
            _ => new FontFamily("Segoe UI Variable Text, Segoe UI")
        };
    }

    public double GetCalculatedLineHeight()
    {
        return ExpandedLineSpacing ? RoleplayFontSize * 1.85 : RoleplayFontSize * 1.5;
    }

    public async Task SpeakTextAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        StopSpeaking();

        try
        {
            _activeSynth = new SpeechSynthesizer();
            // Clean markdown asterisks and markup for cleaner spoken voice
            var clean = text.Replace("*", "").Replace("#", "").Replace("`", "").Trim();
            var stream = await _activeSynth.SynthesizeTextToStreamAsync(clean);

            _activePlayer = new MediaPlayer();
            _activePlayer.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _activePlayer.Play();
        }
        catch (Exception ex)
        {
            App.Platform?.Log($"TTS Narration note: {ex.Message}");
        }
    }

    public void StopSpeaking()
    {
        try
        {
            if (_activePlayer != null)
            {
                _activePlayer.Pause();
                _activePlayer.Dispose();
                _activePlayer = null;
            }
            if (_activeSynth != null)
            {
                _activeSynth.Dispose();
                _activeSynth = null;
            }
        }
        catch
        {
        }
    }
}
