using System.Text.Json;
using FocusWShell.Core.Layout;

namespace FocusWShell.Win32.Persistence;

/// <summary>Persiste el catálogo de tamaños mínimos aprendidos.</summary>
public sealed class MinSizeStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary><c>%LOCALAPPDATA%\FocusWShell\min-sizes.json</c>, compartido por la app y el probe.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "min-sizes.json");

    public string FilePath { get; } = filePath;

    public MinSizeCatalog Load()
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

    public void Save(MinSizeCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(catalog, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
