using System.Text.Json;

namespace BlockCraft.Client;

/// <summary>Persisted client configuration with safe defaults and bounded values.</summary>
internal sealed class GameSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public float FieldOfView { get; set; } = 75f;
    public int RenderDistance { get; set; } = 5;
    public bool VSync { get; set; } = true;
    public bool Fullscreen { get; set; }
    public float MasterVolume { get; set; } = 0.65f;

    public void Normalize()
    {
        Width = Math.Clamp(Width, 800, 3840);
        Height = Math.Clamp(Height, 600, 2160);
        FieldOfView = Math.Clamp(FieldOfView, 55f, 110f);
        RenderDistance = Math.Clamp(RenderDistance, 2, 12);
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
    }

    public static GameSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new GameSettings();
            var settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path)) ?? new GameSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new GameSettings();
        }
    }

    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
