using System.Globalization;
using System.Text;
using System.Text.Json;
using FocusWShell.Core.Displays;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Core.Windows;
using FocusWShell.Probe;
using FocusWShell.Win32;
using FocusWShell.Win32.Persistence;
using FocusWShell.Win32.Scenes;

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
        "mins" => Mins(rest),
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
          focus-probe place --context a,b --center c --work d,e [--split 18,55,27] [--gap 8]
                            [--context-template tiles|stack] [--work-template tiles|stack]
                            [--dry-run] [--no-learn] [--pause-after N]
                            [--mode async|batch|seq] [--cycles N] [--cycle-delay ms]
              Coloca ventanas en Contexto | Centro | Trabajo del monitor más ancho, respetando
              los tamaños mínimos aprendidos. Si una app impone uno nuevo, lo aprende y recoloca.
              Cada nombre busca una ventana candidata cuyo ejecutable lo contenga (p. ej. slack, msedge).
              Antes de mover nada guarda la colocación original en el diario.
              --mode async (por defecto) pide el movimiento a todas en paralelo (SWP_ASYNCWINDOWPOS);
              batch usa un lote DeferWindowPos; seq, SetWindowPos una a una.
              --cycles N revierte y reaplica la escena N veces midiendo cada transición (S3).
          focus-probe restore
              Devuelve todas las ventanas del diario a su colocación original y borra el diario.
          focus-probe mins [--clear]
              Muestra (u olvida) los tamaños mínimos aprendidos.
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

    var mode = (Option(args, "--mode") ?? "async").ToLowerInvariant() switch
    {
        "async" => MoveMode.Async,
        "batch" => MoveMode.Batch,
        "seq" => MoveMode.Sequential,
        var m => throw new ArgumentException($"Modo desconocido: {m} (async | batch | seq)"),
    };
    int? pauseAfter = Option(args, "--pause-after") is { } p ? int.Parse(p, CultureInfo.InvariantCulture) : null;
    if (pauseAfter is not null)
    {
        // S5 necesita detenerse entre dos ventanas: sólo es posible moviéndolas una a una.
        mode = MoveMode.Sequential;
    }

    var options = new SceneOptions
    {
        ContextWeight = split[0],
        CenterWeight = split[1],
        WorkWeight = split[2],
        Gap = int.Parse(Option(args, "--gap") ?? "0", CultureInfo.InvariantCulture),
        ContextTemplate = ParseTemplate(Option(args, "--context-template")),
        WorkTemplate = ParseTemplate(Option(args, "--work-template")),
        Mode = mode,
        Learn = !Has(args, "--no-learn"),
    };
    bool dryRun = Has(args, "--dry-run");
    int cycles = int.Parse(Option(args, "--cycles") ?? "0", CultureInfo.InvariantCulture);
    int cycleDelay = int.Parse(Option(args, "--cycle-delay") ?? "1500", CultureInfo.InvariantCulture);

    // Monitor más ancho: en esta configuración, el ultrawide.
    var display = DisplayEnumerator.Enumerate().MaxBy(d => d.WorkArea.Width)
        ?? throw new InvalidOperationException("No se encontró ningún monitor.");

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

    var assignment = new SceneAssignment(
        contextNames.Select(Find).ToList(),
        centerNames.Select(Find).FirstOrDefault(),
        workNames.Select(Find).ToList());
    var minSizes = new MinSizeStore(MinSizeStore.DefaultPath);
    var applier = new SceneApplier(display, assignment, options, minSizes);

    Console.WriteLine($"Monitor {display.DeviceName}: trabajo {display.WorkArea}, {display.Dpi} DPI · pesos {string.Join("/", split)} · gap {options.Gap}");
    Console.WriteLine($"Mínimos conocidos: {minSizes.FilePath} ({applier.Catalog.Entries.Count} apps)");

    if (dryRun)
    {
        PrintLayout(applier.Plan());
        Console.WriteLine("--dry-run: no se movió nada.");
        return 0;
    }

    // Geometría de partida, para revertir en los ciclos de S3.
    var originals = assignment.All
        .Select(w => (w.Handle, Rect: WindowMover.VisibleBounds(w.Handle), Special: WindowMover.IsMinimizedOrMaximized(w.Handle)))
        .ToList();

    var journal = new RecoveryJournal(RecoveryJournal.DefaultPath);
    Console.WriteLine($"Diario: {journal.FilePath}");
    var report = applier.Apply(
        journal,
        onPass: pass =>
        {
            PrintLayout(pass.Layout);
            PrintPass(pass);
        },
        beforeEach: pauseAfter is int limit
            ? i =>
            {
                if (i == limit)
                {
                    // S5: deja el proceso detenido a mitad de la escena para matarlo desde fuera.
                    Console.WriteLine($"PAUSA tras {i} ventanas (pid {Environment.ProcessId}). Esperando a ser terminado…");
                    Thread.Sleep(Timeout.Infinite);
                }
            }
            : null);
    if (report.Error is not null)
    {
        Console.Error.WriteLine(report.Error);
        return 1;
    }

    if (cycles > 0)
    {
        RunCycles(applier, report.Layout, originals, cycles, cycleDelay, mode);
        var applied = applier.Move(report.Layout, out _, out _);
        report = report with { Applied = applied };
    }

    return Report(report);
}

static void PrintPass(ApplyPass pass)
{
    string how = pass.Stats switch
    {
        null => "ventana por ventana",
        { Batched: false } => "LOTE FALLIDO, ventana por ventana",
        _ => "en paralelo",
    };
    string extra = pass.Stats is { } s ? $" · {s.Normalized} normalizadas · {s.Corrections} corregidas después" : "";
    Console.WriteLine($"Pasada {pass.Number}: {how} · movimiento {pass.MoveTime.TotalMilliseconds:F0} ms{extra}");
    if (pass.Learned.Count > 0)
    {
        Console.WriteLine($"  aprendido {string.Join("; ", pass.Learned)}. Recalculando…");
    }
}

// Ciclos de S3: revertir a la geometría de partida y reaplicar la escena, midiendo cada transición.
static void RunCycles(
    SceneApplier applier,
    SceneLayoutResult layout,
    List<(nint Handle, PixelRect Rect, bool Special)> originals,
    int cycles,
    int delayMs,
    MoveMode mode)
{
    var revertible = originals.Where(o => !o.Special).Select(o => (o.Handle, o.Rect)).ToList();
    int skipped = originals.Count - revertible.Count;
    Console.WriteLine();
    Console.WriteLine($"S3: {cycles} ciclos revertir/aplicar (modo {mode}), pausa {delayMs} ms." +
        (skipped > 0 ? $" {skipped} ventanas empezaron maximizadas o minimizadas y no se revierten en los ciclos." : ""));

    var applyTimes = new List<double>();
    var revertTimes = new List<double>();
    int corrections = 0;
    int failedBatches = 0;
    for (int c = 1; c <= cycles; c++)
    {
        Thread.Sleep(delayMs);
        revertTimes.Add(applier.MoveTo(revertible).TotalMilliseconds);
        Thread.Sleep(delayMs);
        applier.Move(layout, out var moveTime, out var stats);
        applyTimes.Add(moveTime.TotalMilliseconds);
        corrections += stats?.Corrections ?? 0;
        failedBatches += stats is { Batched: false } ? 1 : 0;
        Console.Write($"\r  ciclo {c}/{cycles}: aplicar {applyTimes[^1],5:F0} ms · revertir {revertTimes[^1],5:F0} ms");
    }

    Console.WriteLine();
    Console.WriteLine($"  aplicar:  media {applyTimes.Average():F0} ms · máx {applyTimes.Max():F0} ms");
    Console.WriteLine($"  revertir: media {revertTimes.Average():F0} ms · máx {revertTimes.Max():F0} ms");
    if (mode != MoveMode.Sequential)
    {
        Console.WriteLine($"  {corrections} correcciones individuales · {failedBatches} lotes fallidos");
    }
}

static int Report(SceneApplyReport report)
{
    Console.WriteLine();
    Console.WriteLine($"{"Región",-9} {"App",-18} {"Objetivo",-24} {"Obtenido",-24} {"ΔL",4} {"ΔT",4} {"ΔR",4} {"ΔB",4} {"Bordes",-10}  Resultado");
    Console.WriteLine(new string('-', 130));
    foreach (var (t, r) in report.Applied)
    {
        if (r is null)
        {
            Console.WriteLine($"{t.Region,-9} {Trim(t.Label, 18),-18} (minimizada)");
            continue;
        }

        var a = r.Actual;
        string outcome = r.Error ?? (r.IsExact ? "exacto" : $"desviación {r.MaxDeviation}px");
        Console.WriteLine(
            $"{t.Region,-9} {Trim(t.Label, 18),-18} {t.Rect,-24} {a,-24} " +
            $"{a.Left - t.Rect.Left,4} {a.Top - t.Rect.Top,4} {a.Right - t.Rect.Right,4} {a.Bottom - t.Rect.Bottom,4} " +
            $"{r.Margins,-10}  {outcome}");
    }

    var invaders = report.CenterInvaders;
    Console.WriteLine();
    Console.WriteLine($"{report.ExactCount}/{report.Applied.Count} ventanas en su destino (±1 px).");
    Console.WriteLine(invaders.Count == 0
        ? "Centro protegido: ninguna ventana de panel lo invade."
        : $"CENTRO INVADIDO por: {string.Join(", ", invaders)}");
    Console.WriteLine("Para devolverlas a su lugar: focus-probe restore");
    return report.Succeeded ? 0 : 1;
}

static PanelTemplate ParseTemplate(string? value) => value?.ToLowerInvariant() switch
{
    null or "tiles" => PanelTemplate.Tiles,
    "stack" => PanelTemplate.Stack,
    _ => throw new ArgumentException($"Template desconocido: {value} (tiles | stack)"),
};

static void PrintLayout(SceneLayoutResult layout)
{
    Console.WriteLine();
    Console.WriteLine(
        $"Columnas: Contexto {layout.LeftColumn.Width} px ({layout.EffectiveTemplates["Contexto"]}) · " +
        $"Centro {layout.CenterColumn.Width} px · Trabajo {layout.RightColumn.Width} px ({layout.EffectiveTemplates["Trabajo"]})");
    foreach (var note in layout.Notes)
    {
        Console.WriteLine($"  · {note}");
    }
}

static int Mins(string[] args)
{
    var store = new MinSizeStore(MinSizeStore.DefaultPath);
    if (Has(args, "--clear"))
    {
        store.Save(new MinSizeCatalog());
        Console.WriteLine("Mínimos olvidados.");
        return 0;
    }

    var catalog = store.Load();
    Console.WriteLine(store.FilePath);
    foreach (var (key, size) in catalog.Entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
    {
        string width = size.Width > 0 ? size.Width.ToString(CultureInfo.InvariantCulture) : "-";
        string height = size.Height > 0 ? size.Height.ToString(CultureInfo.InvariantCulture) : "-";
        Console.WriteLine($"  {key,-40} {width,5} x {height,-5} (px a 96 DPI)");
    }

    return 0;
}

static int Restore()
{
    var journal = new RecoveryJournal(RecoveryJournal.DefaultPath);
    if (!journal.IsDirty)
    {
        Console.WriteLine("El diario está vacío: no hay nada que restaurar.");
        return 0;
    }

    var report = JournalRestorer.Restore(journal);
    foreach (var r in report.Entries)
    {
        string verdict = r.Outcome switch
        {
            RestoreOutcome.Gone => "ya no existe (se descarta)",
            RestoreOutcome.Pending => $"{r.Detail} (se conserva en el diario)",
            _ => r.Detail,
        };
        Console.WriteLine($"{Trim(r.Entry.ProcessName, 22),-22} {r.Entry.NormalPosition,-24} {verdict}");
    }

    Console.WriteLine();
    Console.WriteLine(report.Summary);
    Console.WriteLine(journal.IsDirty ? "Diario conservado con las entradas pendientes." : "Diario borrado.");
    return report.IsComplete ? 0 : 1;
}

static string Trim(string value, int max) =>
    value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
