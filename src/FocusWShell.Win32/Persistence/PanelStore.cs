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
    /// <param name="Proportions">Proporciones ajustadas por vista; las que faltan son las de partida.</param>
    private sealed record FileModel(
        int SchemaVersion, List<Panel> Panels, FocusPosition View = FocusPosition.Center, Dictionary<FocusPosition, ViewWeights>? Proportions = null);

    /// <summary>Los paneles guardados, o una lista vacía si no hay archivo o está dañado.</summary>
    public List<Panel> Load() => LoadAll().Panels;

    /// <summary>Paneles, vista y proporciones guardados.</summary>
    public (List<Panel> Panels, FocusPosition View, IReadOnlyDictionary<FocusPosition, ViewWeights> Proportions) LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            return ([], FocusPosition.Center, new Dictionary<FocusPosition, ViewWeights>());
        }

        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options);
            return (model?.Panels ?? [], model?.View ?? FocusPosition.Center, model?.Proportions ?? []);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".dañado", overwrite: true);
            return ([], FocusPosition.Center, new Dictionary<FocusPosition, ViewWeights>());
        }
    }

    public void Save(
        IEnumerable<Panel> panels, FocusPosition view = FocusPosition.Center, IReadOnlyDictionary<FocusPosition, ViewWeights>? proportions = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        var model = new FileModel(1, [.. panels], view, proportions is { Count: > 0 } ? new(proportions) : null);
        File.WriteAllText(temp, JsonSerializer.Serialize(model, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
