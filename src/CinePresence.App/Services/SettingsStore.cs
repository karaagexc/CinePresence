using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CinePresence.App.Services;

public sealed record AppSettings
{
    public bool SharingEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool OnboardingComplete { get; init; }
    public string DiscordApplicationIdOverride { get; init; } = "";
    public string ProtectedTmdbToken { get; init; } = "";
    public bool VlcEnabled { get; init; }
    public int VlcPort { get; init; } = 8080;
    public string ProtectedVlcPassword { get; init; } = "";
    public HashSet<string> ExcludedSources { get; init; } = [];
}

public sealed class SettingsStore
{
    public string DirectoryPath { get; }
    private string PathName => Path.Combine(DirectoryPath, "settings.json");
    public string Warning { get; private set; } = "";
    public SettingsStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinePresence");
    public AppSettings Load()
    {
        if (!File.Exists(PathName)) return new();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { Warning = "Saved settings could not be loaded. Please check Settings."; return new(); }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(PathName + ".tmp", PathName, true);
    }
    public static string Protect(string text) => string.IsNullOrEmpty(text) ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
    public string Unprotect(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        { Warning = "A saved credential could not be decrypted. Re-enter it in Settings on this Windows account."; return ""; }
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
            ?? throw new IOException("Windows startup settings are unavailable.");
        if (enabled) key.SetValue("CinePresence", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("CinePresence", false);
    }
    public static string ReleaseApplicationId()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "release.json")));
            return json.RootElement.GetProperty("discordApplicationId").GetString() ?? "";
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException) { return ""; }
    }
}
