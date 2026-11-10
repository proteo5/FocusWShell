using System.Text.Json;

namespace FocusWShell.Win32.Persistence;

/// <summary>
/// Diario de recuperación (ADR 0005): guarda la colocación original de cada ventana
/// ANTES de moverla por primera vez, para poder restaurarla aunque el proceso muera.
/// Nunca sobrescribe una entrada existente: la primera captura es la original.
/// Un diario con entradas significa que hay ventanas fuera de su sitio ("sucio").
/// </summary>
public sealed class RecoveryJournal(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Diario de la app: <c>%LOCALAPPDATA%\FocusWShell\journal.json</c>.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "journal.json");

    public string FilePath { get; } = filePath;

    public List<SavedPlacement> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SavedPlacement>>(File.ReadAllText(FilePath), Options) ?? [];
    }

    /// <summary>Si hay ventanas pendientes de restaurar.</summary>
    public bool IsDirty => Load().Count > 0;

    /// <summary>Añade las capturas de ventanas que aún no están en el diario. Devuelve el diario resultante.</summary>
    public List<SavedPlacement> AddIfMissing(IEnumerable<SavedPlacement> captures)
    {
        var entries = Load();
        foreach (var capture in captures)
        {
            if (!entries.Any(e => e.Handle == capture.Handle && e.ProcessId == capture.ProcessId))
            {
                entries.Add(capture);
            }
        }

        Save(entries);
        return entries;
    }

    /// <summary>Escritura atómica: archivo temporal y reemplazo.</summary>
    public void Save(List<SavedPlacement> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            Delete();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
