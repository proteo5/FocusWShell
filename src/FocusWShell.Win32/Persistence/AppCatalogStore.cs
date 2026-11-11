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

    /// <summary>
    /// Versión 2: las apps se anclan (<see cref="CatalogApp.Pinned"/>). Un archivo anterior no tiene
    /// anclas; quien lo carga decide cuáles anclar (<see cref="LoadedOldVersion"/>).
    /// </summary>
    private const int Version = 2;

    /// <summary>Si el último <see cref="Load"/> leyó un archivo de antes de las anclas.</summary>
    public bool LoadedOldVersion { get; private set; }

    private sealed record FileModel(int SchemaVersion, List<CatalogApp> Apps);

    public AppCatalog Load()
    {
        LoadedOldVersion = false;
        if (!File.Exists(FilePath))
        {
            return new AppCatalog();
        }

        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options);
            LoadedOldVersion = model is not null && model.SchemaVersion < Version;
            return new AppCatalog(model?.Apps ?? []);
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
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileModel(Version, [.. catalog.Apps]), Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
