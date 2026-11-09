using System.Globalization;
using System.Text;
using System.Text.Json;
using FocusWShell.Core.Displays;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Core.Windows;
using FocusWShell.Probe;
using FocusWShell.Win32;

// focus-probe: herramienta de diagnóstico para los spikes de M0 (docs/04-riesgos-y-spikes.md).

Console.OutputEncoding = Encoding.UTF8;

var command = args.FirstOrDefault() ?? "list";
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "list" => List(showAll: Has(rest, "--all"), json: Has(rest, "--json")),
        "monitors" => Monitors(),
        "place" => Place(rest),
        "restore" => Restore(),
        _ => Usage($"Comando desconocido: {command}"),
    };
}
catch (ArgumentException ex)
{
    return Usage(ex.Message);
}

static int Usage(string? error = null)
{
    if (error is not null)
    {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine();
    }

    Console.Error.WriteLine("""
        Uso:
          focus-probe list [--all] [--json]
              Lista las ventanas top-level y si son candidatas. Sólo lee.
          focus-probe monitors
              Lista los monitores, su área de trabajo y DPI. Sólo lee.
          focus-probe place --context a,b --center c --work d,e [--split 18,55,27] [--gap 8] [--dry-run]
              Coloca ventanas en Contexto | Centro | Trabajo del monitor más ancho.
              Cada nombre busca una ventana candidata cuyo ejecutable lo contenga (p. ej. slack, msedge).
              Antes de mover nada guarda la colocación original en el diario.
          focus-probe restore
              Devuelve todas las ventanas del diario a su colocación original y borra el diario.
        """);
    return 2;
}

static bool Has(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

static string? Option(string[] args, string name)
{
    int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string[] ListOption(string[] args, string name) =>
    Option(args, name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

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

static int Monitors()
{
    foreach (var d in DisplayEnumerator.Enumerate())
    {
        Console.WriteLine(
            $"{d.DeviceName,-14} {(d.IsPrimary ? "principal" : string.Empty),-9} monitor {d.Bounds,-24} " +
            $"trabajo {d.WorkArea,-24} {d.Dpi} DPI ({d.Scale:P0})");
    }

    return 0;
}

static int Place(string[] args)
{
    var contextNames = ListOption(args, "--context");
    var centerNames = ListOption(args, "--center");
    var workNames = ListOption(args, "--work");
    if (contextNames.Length + centerNames.Length + workNames.Length == 0)
    {
        throw new ArgumentException("Indica al menos una ventana con --context, --center o --work.");
    }

    if (centerNames.Length > 1)
    {
        throw new ArgumentException("El Centro aloja una sola ventana.");
    }

    var split = ListOption(args, "--split") is { Length: > 0 } s
        ? s.Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()
        : [18.0, 55.0, 27.0];
    if (split.Length != 3)
    {
        throw new ArgumentException("--split necesita tres pesos: contexto,centro,trabajo.");
    }

    int gap = int.Parse(Option(args, "--gap") ?? "0", CultureInfo.InvariantCulture);
    bool dryRun = Has(args, "--dry-run");

    // Monitor más ancho: en esta configuración, el ultrawide.
    var display = DisplayEnumerator.Enumerate().MaxBy(d => d.WorkArea.Width)
        ?? throw new InvalidOperationException("No se encontró ningún monitor.");
    var columns = Splitter.Columns(display.WorkArea, split, gap);

    var ownIntegrity = WindowEnumerator.CurrentIntegrity();
    var filter = new EligibilityFilter();
    var candidates = WindowEnumerator.EnumerateTopLevel()
        .Where(w => filter.Evaluate(w, ownIntegrity) is { IsCandidate: true, Manageable: true })
        .ToList();

    var used = new HashSet<nint>();
    WindowSnapshot Find(string name)
    {
        var match = candidates.FirstOrDefault(w =>
            !used.Contains(w.Handle) && w.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new ArgumentException($"No hay ventana candidata libre cuyo ejecutable contenga '{name}'.");
        }

        used.Add(match.Handle);
        return match;
    }

    var plan = new List<(string Region, WindowSnapshot Window, PixelRect Target)>();
    void AddStack(string region, string[] names, PixelRect area)
    {
        if (names.Length == 0)
        {
            return;
        }

        var windows = names.Select(Find).ToList();
        var rows = Splitter.EqualRows(area, windows.Count, gap);
        for (int i = 0; i < windows.Count; i++)
        {
            plan.Add((region, windows[i], rows[i]));
        }
    }

    AddStack("Contexto", contextNames, columns[0]);
    AddStack("Centro", centerNames, columns[1]);
    AddStack("Trabajo", workNames, columns[2]);

    Console.WriteLine($"Monitor {display.DeviceName}: trabajo {display.WorkArea}, {display.Dpi} DPI · split {string.Join("/", split)} · gap {gap}");
    Console.WriteLine();

    if (dryRun)
    {
        foreach (var (region, w, target) in plan)
        {
            Console.WriteLine($"{region,-9} {Trim(w.ProcessName, 22),-22} {target,-24} desde {w.VisibleBounds}{(w.IsMaximized ? " (max)" : string.Empty)}{(w.IsMinimized ? " (min)" : string.Empty)}");
        }

        Console.WriteLine();
        Console.WriteLine("--dry-run: no se movió nada.");
        return 0;
    }

    // Diario ANTES de mover (ADR 0005).
    var captures = plan
        .Select(p => WindowMover.Capture(p.Window.Handle, p.Window.ProcessId, p.Window.ProcessName, p.Window.Title))
        .OfType<SavedPlacement>()
        .ToList();
    if (captures.Count != plan.Count)
    {
        Console.Error.WriteLine("No se pudo capturar el estado original de todas las ventanas; no se movió nada.");
        return 1;
    }

    ProbeJournal.AddIfMissing(captures);
    Console.WriteLine($"Diario: {ProbeJournal.FilePath}");
    Console.WriteLine();
    Console.WriteLine($"{"Región",-9} {"Proceso",-22} {"Objetivo",-24} {"Obtenido",-24} {"ΔL",4} {"ΔT",4} {"ΔR",4} {"ΔB",4} {"Bordes",-10} {"Int.",4}  Resultado");
    Console.WriteLine(new string('-', 140));

    int exact = 0;
    foreach (var (region, w, target) in plan)
    {
        var r = WindowMover.Place(w.Handle, target);
        var a = r.Actual;
        string outcome = r.Error ?? (r.IsExact ? "exacto" : $"desviación {r.MaxDeviation}px");
        if (r.IsExact)
        {
            exact++;
        }

        Console.WriteLine(
            $"{region,-9} {Trim(w.ProcessName, 22),-22} {target,-24} {a,-24} " +
            $"{a.Left - target.Left,4} {a.Top - target.Top,4} {a.Right - target.Right,4} {a.Bottom - target.Bottom,4} " +
            $"{r.Margins,-10} {r.Attempts,4}  {outcome}");
    }

    Console.WriteLine();
    Console.WriteLine($"{exact}/{plan.Count} ventanas colocadas con precisión de ±1 px.");
    Console.WriteLine("Para devolverlas a su lugar: focus-probe restore");
    return exact == plan.Count ? 0 : 1;
}

static int Restore()
{
    var entries = ProbeJournal.Load();
    if (entries.Count == 0)
    {
        Console.WriteLine("El diario está vacío: no hay nada que restaurar.");
        return 0;
    }

    int ok = 0;
    foreach (var e in entries)
    {
        var error = WindowMover.Restore(e);
        string verdict;
        if (error is not null)
        {
            verdict = error;
        }
        else
        {
            var now = WindowMover.Capture((nint)e.Handle, e.ProcessId, e.ProcessName, e.Title);
            bool same = now is not null && now.NormalPosition == e.NormalPosition && now.ShowCmd == e.ShowCmd;
            verdict = same ? "restaurada" : $"restaurada con diferencias: {now?.NormalPosition} show {now?.ShowCmd} (original {e.NormalPosition} show {e.ShowCmd})";
            if (same)
            {
                ok++;
            }
        }

        Console.WriteLine($"{Trim(e.ProcessName, 22),-22} {e.NormalPosition,-24} {verdict}");
    }

    ProbeJournal.Delete();
    Console.WriteLine();
    Console.WriteLine($"{ok}/{entries.Count} ventanas restauradas exactamente. Diario borrado.");
    return ok == entries.Count ? 0 : 1;
}

static string Trim(string value, int max) =>
    value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
