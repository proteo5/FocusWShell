using System.Text.Json;
using FocusWShell.Win32;

namespace FocusWShell.Probe;

/// <summary>
/// Diario mínimo para los spikes: guarda la colocación original de cada ventana
/// ANTES de moverla, para poder restaurarla aunque el proceso muera (ADR 0005).
/// Nunca sobrescribe una entrada existente: la primera captura es la original.
/// </summary>
internal static class ProbeJournal
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "probe-journal.json");

    public static List<SavedPlacement> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<List<SavedPlacement>>(json, Options) ?? [];
    }

    /// <summary>Añade las capturas de ventanas que aún no están en el diario. Devuelve el diario resultante.</summary>
    public static List<SavedPlacement> AddIfMissing(IEnumerable<SavedPlacement> captures)
    {
        var entries = Load();
        foreach (var capture in captures)
        {
            bool known = entries.Any(e => e.Handle == capture.Handle && e.ProcessId == capture.ProcessId);
            if (!known)
            {
                entries.Add(capture);
            }
        }

        Save(entries);
        return entries;
    }

    public static void Save(List<SavedPlacement> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
