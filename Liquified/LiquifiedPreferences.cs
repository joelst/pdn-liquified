using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiquifiedPlugin;

internal sealed class LiquifiedPreferences
{
    public LiquifiedMode Mode { get; set; }
    public int BrushSize { get; set; } = 80;
    public double Strength { get; set; } = 0.25;
    public double Density { get; set; } = 0.25;
    public int Rate { get; set; } = 20;
    public int Quality { get; set; } = 2;
    public int MeshSize { get; set; } = 32;
    public bool ShowMesh { get; set; }
    public bool ShowMask { get; set; } = true;
    public bool ShowBackdrop { get; set; }
    public bool UsePenPressure { get; set; } = true;
    public bool SidebarCollapsed { get; set; }

    private static string DefaultsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pdn-liquified", "liquified-defaults.json");

    private static string LastUsedPath => Path.Combine(Path.GetDirectoryName(DefaultsPath)!, "liquified-last-used.json");

    internal static LiquifiedConfigToken LoadDefaultToken() => LoadStartup(DefaultsPath, LastUsedPath);

    internal static LiquifiedConfigToken LoadStartup(string defaultsPath, string lastUsedPath) =>
        Load(File.Exists(lastUsedPath) ? lastUsedPath : defaultsPath);

    internal static LiquifiedConfigToken Load(string path)
    {
        if (!File.Exists(path)) return new LiquifiedConfigToken();
        var settings = JsonSerializer.Deserialize(File.ReadAllText(path), LiquifiedPreferencesJsonContext.Default.LiquifiedPreferences)
            ?? throw new JsonException("The defaults file contains no settings.");
        LiquifiedDeformation.ValidateBrushSettings(settings.Mode, settings.BrushSize, settings.Strength, settings.Density);
        if (settings.Rate is < 1 or > 120 || settings.Quality is < 1 or > 4 || settings.MeshSize is < 8 or > 256)
            throw new ArgumentException("Saved rate, quality, or mesh size is out of range.");
        return new LiquifiedConfigToken
        {
            Mode = settings.Mode, BrushSize = settings.BrushSize,
            Strength = settings.Strength, Density = settings.Density,
            Rate = settings.Rate, Quality = settings.Quality, MeshSize = settings.MeshSize,
            ShowMesh = settings.ShowMesh, ShowMask = settings.ShowMask,
            ShowBackdrop = settings.ShowBackdrop, UsePenPressure = settings.UsePenPressure,
            SidebarCollapsed = settings.SidebarCollapsed
        };
    }

    internal static void SaveDefaults(LiquifiedConfigToken token) => Save(token, DefaultsPath);

    internal static void SaveLastUsed(LiquifiedConfigToken token) => Save(token, LastUsedPath);

    internal static void Save(LiquifiedConfigToken token, string path)
    {
        var settings = new LiquifiedPreferences
        {
            Mode = token.Mode, BrushSize = token.BrushSize,
            Strength = token.Strength, Density = token.Density,
            Rate = token.Rate, Quality = token.Quality, MeshSize = token.MeshSize,
            ShowMesh = token.ShowMesh, ShowMask = token.ShowMask,
            ShowBackdrop = token.ShowBackdrop, UsePenPressure = token.UsePenPressure,
            SidebarCollapsed = token.SidebarCollapsed
        };
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, LiquifiedPreferencesJsonContext.Default.LiquifiedPreferences));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

[JsonSerializable(typeof(LiquifiedPreferences))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class LiquifiedPreferencesJsonContext : JsonSerializerContext
{
}
