using System.Text.Json;
using System.Text.Json.Serialization;
using FocusWShell.Core.Domain;

namespace FocusWShell.Win32.Persistence;

/// <summary>Paneles del usuario: <c>%LOCALAPPDATA%\FocusWShell\panels.json</c> (ADR 0011).</summary>
public sealed class PanelStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "panels.json");

    public string FilePath { get; } = filePath;

    private sealed record FileModel(int SchemaVersion, List<Panel> Panels);

    /// <summary>Los paneles guardados, o una lista vacía si no hay archivo o está dañado.</summary>
    public List<Panel> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options)?.Panels ?? [];
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".dañado", overwrite: true);
            return [];
        }
    }

    public void Save(IEnumerable<Panel> panels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileModel(1, [.. panels]), Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
