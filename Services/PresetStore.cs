using System.IO;
using System.Text.Json;
using ImageMark.Models;

namespace ImageMark.Services;

public static class PresetStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImageMark");

    private static readonly string FilePath = Path.Combine(Dir, "presets.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<WatermarkPreset> Load(string defaultName)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<WatermarkPreset>>(File.ReadAllText(FilePath));
                if (list is { Count: > 0 })
                    return list;
            }
        }
        catch
        {
            // sérült fájl esetén alapértelmezettel indulunk
        }
        return new List<WatermarkPreset> { new() { Name = defaultName } };
    }

    public static void Save(IEnumerable<WatermarkPreset> presets)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(presets.ToList(), Options));
    }

    public static void ExportTo(string path, IEnumerable<WatermarkPreset> presets) =>
        File.WriteAllText(path, JsonSerializer.Serialize(presets.ToList(), Options));

    public static List<WatermarkPreset> ImportFrom(string path) =>
        JsonSerializer.Deserialize<List<WatermarkPreset>>(File.ReadAllText(path)) ?? new List<WatermarkPreset>();
}
