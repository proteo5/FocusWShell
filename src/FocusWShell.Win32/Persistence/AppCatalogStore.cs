using System.Text.Json;
using FocusWShell.Core.Domain;

namespace FocusWShell.Win32.Persistence;

/// <summary>Catálogo de apps: <c>%LOCALAPPDATA%\FocusWShell\apps.json</c> (ADR 0011). Sin títulos de ventanas.</summary>
public sealed class AppCatalogStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "apps.json");

    public string FilePath { get; } = filePath;

    private sealed record FileModel(int SchemaVersion, List<CatalogApp> Apps);

    public AppCatalog Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppCatalog();
        }

        try
        {
            return new AppCatalog(JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options)?.Apps ?? []);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".dañado", overwrite: true);
            return new AppCatalog();
        }
    }

    public void Save(AppCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileModel(1, [.. catalog.Apps]), Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
