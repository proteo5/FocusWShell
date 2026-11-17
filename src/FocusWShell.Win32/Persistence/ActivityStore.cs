using System.Globalization;
using System.Text.Json;
using FocusWShell.Core.Activity;

namespace FocusWShell.Win32.Persistence;

/// <summary>
/// La actividad (ADR 0019): un archivo JSON Lines por día en <c>%LOCALAPPDATA%\FocusWShell\actividad</c>,
/// un tramo por línea. Sólo en este equipo; nunca guarda títulos de ventanas.
/// </summary>
public sealed class ActivityStore(string folder)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new();

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "actividad");

    public string Folder { get; } = folder;

    private string FileOf(DateOnly day) => Path.Combine(Folder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>Agrega tramos, cada uno al archivo de su día.</summary>
    public void Append(IEnumerable<ActivitySegment> segments)
    {
        lock (_gate)
        {
            foreach (var day in segments.GroupBy(s => DateOnly.FromDateTime(s.Start)))
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllLines(FileOf(day.Key), day.Select(s => JsonSerializer.Serialize(s, Options)));
            }
        }
    }

    /// <summary>Los tramos de un día. Una línea dañada se salta.</summary>
    public List<ActivitySegment> Load(DateOnly day)
    {
        lock (_gate)
        {
            var path = FileOf(day);
            if (!File.Exists(path))
            {
                return [];
            }

            var result = new List<ActivitySegment>();
            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ActivitySegment>(line, Options) is { } s)
                    {
                        result.Add(s);
                    }
                }
                catch (JsonException)
                {
                    // Una línea a medias (un apagado al escribir): se pierde sólo ese tramo.
                }
            }

            return result;
        }
    }

    /// <summary>Los días con actividad guardada, del más reciente al más antiguo.</summary>
    public List<DateOnly> Days()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(Folder, "*.jsonl")
            .Select(f => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
            .OfType<DateOnly>()
            .OrderDescending()];
    }

    /// <summary>Borra los días anteriores a <paramref name="keepDays"/> días.</summary>
    public void Prune(int keepDays, DateOnly today)
    {
        lock (_gate)
        {
            foreach (var day in Days().Where(d => d < today.AddDays(-keepDays)))
            {
                File.Delete(FileOf(day));
            }
        }
    }

    /// <summary>Borra todo el historial.</summary>
    public void DeleteAll()
    {
        lock (_gate)
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
