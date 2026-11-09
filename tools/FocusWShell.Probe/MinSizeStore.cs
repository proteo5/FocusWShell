using System.Text.Json;
using FocusWShell.Core.Layout;

namespace FocusWShell.Probe;

/// <summary>Persiste el catálogo de tamaños mínimos aprendidos.</summary>
internal static class MinSizeStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "min-sizes.json");

    public static MinSizeCatalog Load()
    {
        var catalog = new MinSizeCatalog();
        if (!File.Exists(FilePath))
        {
            return catalog;
        }

        var loaded = JsonSerializer.Deserialize<MinSizeCatalog>(File.ReadAllText(FilePath), Options);
        foreach (var (key, size) in loaded?.Entries ?? [])
        {
            catalog.Entries[key] = size;
        }

        return catalog;
    }

    public static void Save(MinSizeCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(catalog, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
