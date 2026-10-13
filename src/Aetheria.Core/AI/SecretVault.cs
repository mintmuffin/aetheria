using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aetheria.Core.AI;

public sealed class SecretVault
{
    private readonly string _vaultFilePath;
    private readonly object _lock = new();
    private Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public SecretVault(string? customDirectory = null)
    {
        var baseDir = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aetheria");
        Directory.CreateDirectory(baseDir);
        _vaultFilePath = Path.Combine(baseDir, "credentials.vault");
        Load();
    }

    public void SetApiKey(string providerOrPresetKey, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(providerOrPresetKey)) return;
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _secrets.Remove(providerOrPresetKey.Trim());
            }
            else
            {
                _secrets[providerOrPresetKey.Trim()] = apiKey.Trim();
            }
            Save();
        }
    }

    public string GetApiKey(string providerOrPresetKey)
    {
        if (string.IsNullOrWhiteSpace(providerOrPresetKey)) return string.Empty;
        lock (_lock)
        {
            return _secrets.TryGetValue(providerOrPresetKey.Trim(), out var val) ? val : string.Empty;
        }
    }

    public bool HasApiKey(string providerOrPresetKey) =>
        !string.IsNullOrWhiteSpace(GetApiKey(providerOrPresetKey));

    private void Load()
    {
        if (!File.Exists(_vaultFilePath)) return;
        try
        {
            var encryptedBytes = File.ReadAllBytes(_vaultFilePath);
            byte[] plainBytes;
            if (OperatingSystem.IsWindows())
            {
                plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            }
            else
            {
                plainBytes = encryptedBytes;
            }
            var json = Encoding.UTF8.GetString(plainBytes);
            _secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_secrets);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encryptedBytes;
            if (OperatingSystem.IsWindows())
            {
                encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            }
            else
            {
                encryptedBytes = plainBytes;
            }
            var tempPath = _vaultFilePath + ".tmp";
            File.WriteAllBytes(tempPath, encryptedBytes);
            File.Move(tempPath, _vaultFilePath, overwrite: true);
        }
        catch
        {
            // Ignore write errors in restricted sandboxes
        }
    }
}
