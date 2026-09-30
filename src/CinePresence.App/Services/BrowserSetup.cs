using System.Text.Json;
using Microsoft.Win32;

namespace CinePresence.App.Services;

public static class BrowserSetup
{
    public static string ExtensionDirectory => Path.Combine(AppContext.BaseDirectory, "browser-companion");
    public static void Register()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "CinePresence.BrowserHost.exe");
        if (!File.Exists(host) || !File.Exists(Path.Combine(ExtensionDirectory, "manifest.json")))
            throw new ServiceException("Install the complete CinePresence release to set up its browser companion.");
        var manifest = Path.Combine(AppContext.BaseDirectory, "browser-host.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { name = BrowserProtocol.HostName, description = "CinePresence browser connection", path = host,
            type = "stdio", allowed_origins = new[] { $"chrome-extension://{BrowserProtocol.ExtensionId}/" } }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var browser in new[] { @"Software\Google\Chrome", @"Software\Microsoft\Edge" })
        {
            using var key = Registry.CurrentUser.CreateSubKey(browser + @"\NativeMessagingHosts\" + BrowserProtocol.HostName);
            key.SetValue("", manifest);
        }
    }
}
