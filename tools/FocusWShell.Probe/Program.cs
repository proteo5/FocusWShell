using System.Text;
using System.Text.Json;
using FocusWShell.Core.Windows;
using FocusWShell.Probe;
using FocusWShell.Win32;

// focus-probe: herramienta de diagnóstico para los spikes de M0 (docs/04-riesgos-y-spikes.md).
// Sólo lee el estado del escritorio; no mueve ninguna ventana.

Console.OutputEncoding = Encoding.UTF8;

var command = args.FirstOrDefault() ?? "list";
var options = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);

switch (command)
{
    case "list":
        return List(showAll: options.Contains("--all"), json: options.Contains("--json"));
    default:
        Console.Error.WriteLine($"Comando desconocido: {command}");
        Console.Error.WriteLine("Uso: focus-probe list [--all] [--json]");
        Console.Error.WriteLine("  --all   incluye ventanas invisibles (por defecto se omiten porque son cientos)");
        Console.Error.WriteLine("  --json  salida JSON para analizar con otras herramientas");
        return 2;
}

static int List(bool showAll, bool json)
{
    var ownIntegrity = WindowEnumerator.CurrentIntegrity();
    var filter = new EligibilityFilter();
    var rows = WindowEnumerator.EnumerateTopLevel()
        .Select(w => (Window: w, Result: filter.Evaluate(w, ownIntegrity)))
        .Where(r => showAll || r.Result.Reason != ExclusionReason.NotVisible)
        .ToList();

    if (json)
    {
        var payload = rows.Select(r => new
        {
            handle = $"0x{r.Window.Handle:X}",
            candidate = r.Result.IsCandidate,
            manageable = r.Result.Manageable,
            reason = r.Result.Reason.ToString(),
            window = r.Window,
        });
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new HandleConverter() } };
        Console.WriteLine(JsonSerializer.Serialize(payload, jsonOptions));
        return 0;
    }

    Console.WriteLine($"Integridad del gestor: {ownIntegrity}");
    Console.WriteLine();
    Console.WriteLine($"{"HWND",-10} {"Estado",-14} {"Proceso",-24} {"Clase",-30} {"Visible",-22} {"DPI",4} {"Integr.",-8} Título");
    Console.WriteLine(new string('-', 150));

    foreach (var (w, r) in rows.OrderByDescending(r => r.Result.IsCandidate))
    {
        var state = r.IsCandidate
            ? (r.Manageable ? "CANDIDATA" : "NO-GESTIONABLE")
            : r.Reason.ToString();
        var flags = (w.IsMinimized ? "min " : string.Empty) + (w.IsMaximized ? "max " : string.Empty);
        var process = string.IsNullOrEmpty(w.ProcessName) ? $"pid {w.ProcessId}" : w.ProcessName;
        Console.WriteLine(
            $"{w.Handle,-10:X} {state,-14} {Trim(process, 24),-24} {Trim(w.ClassName, 30),-30} " +
            $"{Trim(flags + w.VisibleBounds, 22),-22} {w.Dpi,4} {w.Integrity,-8} {Trim(w.Title, 50)}");
    }

    var candidates = rows.Count(r => r.Result.IsCandidate);
    var manageable = rows.Count(r => r.Result.IsCandidate && r.Result.Manageable);
    Console.WriteLine();
    Console.WriteLine($"{rows.Count} ventanas listadas · {candidates} candidatas · {manageable} gestionables");
    Console.WriteLine("Exclusiones: " + string.Join(", ", rows
        .Where(r => !r.Result.IsCandidate)
        .GroupBy(r => r.Result.Reason)
        .OrderByDescending(g => g.Count())
        .Select(g => $"{g.Key} {g.Count()}")));
    return 0;
}

static string Trim(string value, int max) =>
    value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
