using System.IO;
using System.Text.Json;
using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

public class ClientSettingsService
{
    private readonly string _settingsPath;

    public ClientSettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perigon.MiniDb.Client");

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "client-settings.json");
    }

    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ClientSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings();
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save(ClientSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json);
    }
}
