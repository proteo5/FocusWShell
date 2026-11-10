using System.Text.Json;
using System.Text.Json.Serialization;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

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

    /// <param name="View">Vista del principal (ADR 0012); los archivos anteriores no la tienen: foco al centro.</param>
    private sealed record FileModel(int SchemaVersion, List<Panel> Panels, FocusPosition View = FocusPosition.Center);

    /// <summary>Los paneles guardados, o una lista vacía si no hay archivo o está dañado.</summary>
    public List<Panel> Load() => LoadAll().Panels;

    /// <summary>Paneles y vista guardados.</summary>
    public (List<Panel> Panels, FocusPosition View) LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            return ([], FocusPosition.Center);
        }

        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options);
            return (model?.Panels ?? [], model?.View ?? FocusPosition.Center);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".dañado", overwrite: true);
            return ([], FocusPosition.Center);
        }
    }

    public void Save(IEnumerable<Panel> panels, FocusPosition view = FocusPosition.Center)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileModel(1, [.. panels], view), Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
