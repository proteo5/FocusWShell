using System.Text.Json;
using System.Text.Json.Serialization;
using FocusWShell.Core.Domain;

namespace FocusWShell.Win32.Persistence;

/// <summary>Escenas guardadas: <c>%LOCALAPPDATA%\FocusWShell\scenes.json</c> (03-arquitectura, Persistencia).</summary>
public sealed class SceneStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "scenes.json");

    public string FilePath { get; } = filePath;

    private sealed record FileModel(int SchemaVersion, List<SavedScene> Scenes);

    public List<SavedScene> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options)?.Scenes ?? [];
        }
        catch (JsonException)
        {
            // Se conserva el archivo dañado para no perder las escenas y se empieza sin ninguna.
            File.Copy(FilePath, FilePath + ".dañado", overwrite: true);
            return [];
        }
    }

    public void Save(List<SavedScene> scenes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileModel(1, scenes), Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Guarda o reemplaza (por nombre, sin distinguir mayúsculas) y devuelve la lista resultante.</summary>
    public List<SavedScene> Upsert(SavedScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var scenes = Load();
        int i = scenes.FindIndex(s => string.Equals(s.Name, scene.Name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            scenes[i] = scene;
        }
        else
        {
            scenes.Add(scene);
        }

        Save(scenes);
        return scenes;
    }

    public List<SavedScene> Delete(string name)
    {
        var scenes = Load();
        scenes.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        Save(scenes);
        return scenes;
    }
}
