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

    int gap = int.Parse(Option(args, "--gap") ?? "0", CultureInfo.InvariantCulture);
    bool dryRun = Has(args, "--dry-run");
    bool noLearn = Has(args, "--no-learn");
    var mode = (Option(args, "--mode") ?? "async").ToLowerInvariant() switch
    {
        "async" => MoveMode.Async,
        "batch" => MoveMode.Batch,
        "seq" => MoveMode.Sequential,
        var m => throw new ArgumentException($"Modo desconocido: {m} (async | batch | seq)"),
    };
    bool batch = mode != MoveMode.Sequential;
    bool async = mode == MoveMode.Async;
    int cycles = int.Parse(Option(args, "--cycles") ?? "0", CultureInfo.InvariantCulture);
    int cycleDelay = int.Parse(Option(args, "--cycle-delay") ?? "1500", CultureInfo.InvariantCulture);
    int? pauseAfter = Option(args, "--pause-after") is { } p ? int.Parse(p, CultureInfo.InvariantCulture) : null;
    var contextTemplate = ParseTemplate(Option(args, "--context-template"));
    var workTemplate = ParseTemplate(Option(args, "--work-template"));

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

    var context = contextNames.Select(Find).ToList();
    var center = centerNames.Select(Find).FirstOrDefault();
    var work = workNames.Select(Find).ToList();
    var all = context.Concat(center is null ? [] : [center]).Concat(work).ToList();
    var byHandle = all.ToDictionary(w => w.Handle);

    var catalog = MinSizeStore.Load();

    SceneLayoutResult Plan()
    {
        LayoutWindow ToLayout(WindowSnapshot w) =>
            new(w.Handle, Path.GetFileNameWithoutExtension(w.ProcessName), catalog.Get(MinSizeCatalog.KeyFor(w), display.Dpi));

        return SceneLayoutEngine.Compute(new SceneInput(
            display.WorkArea,
            new PanelInput("Contexto", PanelSide.Left, split[0], contextTemplate, context.Select(ToLayout).ToList()),
            split[1],
            center is null ? null : ToLayout(center),
            new PanelInput("Trabajo", PanelSide.Right, split[2], workTemplate, work.Select(ToLayout).ToList()),
            gap));
    }

    Console.WriteLine($"Monitor {display.DeviceName}: trabajo {display.WorkArea}, {display.Dpi} DPI · pesos {string.Join("/", split)} · gap {gap}");
    Console.WriteLine($"Mínimos conocidos: {MinSizeStore.FilePath} ({catalog.Entries.Count} apps)");

    var layout = Plan();
    PrintLayout(layout);

    if (dryRun)
    {
        Console.WriteLine("--dry-run: no se movió nada.");
        return 0;
    }

    // Diario ANTES de mover (ADR 0005).
    var captures = all
        .Select(w => WindowMover.Capture(w.Handle, w.ProcessId, w.ProcessName, w.Title))
        .OfType<SavedPlacement>()
        .ToList();
    if (captures.Count != all.Count)
    {
        Console.Error.WriteLine("No se pudo capturar el estado original de todas las ventanas; no se movió nada.");
        return 1;
    }

    ProbeJournal.AddIfMissing(captures);
    Console.WriteLine($"Diario: {ProbeJournal.FilePath}");

    // Geometría de partida, para revertir en los ciclos de S3.
    var originals = all
        .Select(w => (w.Handle, Rect: WindowMover.VisibleBounds(w.Handle), Special: WindowMover.IsMinimizedOrMaximized(w.Handle)))
        .ToList();

    const int MaxPasses = 3;
    List<(WindowTarget Target, PlacementResult? Result)> applied = [];
    for (int pass = 1; pass <= MaxPasses; pass++)
    {
        if (batch && pauseAfter is null)
        {
            (applied, var stats) = ApplyBatch(layout, async);
            PrintBatch($"Pasada {pass}", stats);
        }
        else
        {
            applied = ApplySequential(layout, pauseAfter, out var elapsed);
            Console.WriteLine($"Pasada {pass}: ventana por ventana en {elapsed.TotalMilliseconds:F0} ms");
        }

        if (noLearn)
        {
            break;
        }

        var learned = new List<string>();
        foreach (var (target, result) in applied)
        {
            if (result is { Error: null } r)
            {
                var w = byHandle[target.Handle];
                var key = MinSizeCatalog.KeyFor(w);
                var requested = new PixelSize(r.Target.Width, r.Target.Height);
                var actual = new PixelSize(r.Actual.Width, r.Actual.Height);
                if (catalog.Observe(key, requested, actual, display.Dpi))
                {
                    learned.Add($"{target.Label} → mínimo {catalog.Get(key, display.Dpi)}");
                }
            }
        }

        if (learned.Count == 0)
        {
            break;
        }

        MinSizeStore.Save(catalog);
        Console.WriteLine();
        Console.WriteLine($"Pasada {pass}: aprendido {string.Join("; ", learned)}. Recalculando…");
        layout = Plan();
        PrintLayout(layout);
    }

    if (cycles > 0)
    {
        RunCycles(layout, originals, cycles, cycleDelay, batch, async);
        (applied, _) = batch ? ApplyBatch(layout, async) : (ApplySequential(layout, null, out _), null);
    }

    return Report(layout, applied);
}

// Aplica la escena: primero minimiza las ocultas de las pilas y luego mueve las visibles en un solo lote (R3).
static (List<(WindowTarget Target, PlacementResult? Result)> Applied, BatchPlacement Stats) ApplyBatch(SceneLayoutResult layout, bool async)
{
    foreach (var t in layout.Targets.Where(t => t.Kind == TargetKind.Minimized))
    {
        WindowMover.Minimize(t.Handle);
    }

    var visible = layout.Targets.Where(t => t.Kind == TargetKind.Visible).ToList();
    var stats = WindowMover.PlaceBatch(visible.Select(t => (t.Handle, t.Rect)).ToList(), async);
    var byHandle = visible.Zip(stats.Results).ToDictionary(x => x.First.Handle, x => x.Second);
    var applied = layout.Targets
        .Select(t => (t, t.Kind == TargetKind.Visible ? byHandle[t.Handle] : (PlacementResult?)null))
        .ToList();
    return (applied, stats);
}

static void PrintBatch(string label, BatchPlacement s) =>
    Console.WriteLine(
        $"{label}: {(s.Batched ? "lote" : "LOTE FALLIDO, ventana por ventana")} · " +
        $"movimiento {s.MoveTime.TotalMilliseconds:F0} ms · total {s.TotalTime.TotalMilliseconds:F0} ms · " +
        $"{s.Normalized} normalizadas · {s.Corrections} corregidas después");

// Ciclos de S3: revertir a la geometría de partida y reaplicar la escena, midiendo cada transición.
static void RunCycles(
    SceneLayoutResult layout,
    List<(nint Handle, PixelRect Rect, bool Special)> originals,
    int cycles,
    int delayMs,
    bool batch,
    bool async)
{
    var revertible = originals.Where(o => !o.Special).Select(o => (o.Handle, o.Rect)).ToList();
    int skipped = originals.Count - revertible.Count;
    Console.WriteLine();
    Console.WriteLine($"S3: {cycles} ciclos revertir/aplicar ({(async ? "asíncrono en paralelo" : batch ? "lote DeferWindowPos" : "ventana por ventana")}), pausa {delayMs} ms." +
        (skipped > 0 ? $" {skipped} ventanas empezaron maximizadas o minimizadas y no se revierten en los ciclos." : ""));

    var applyTimes = new List<double>();
    var revertTimes = new List<double>();
    int corrections = 0;
    int failedBatches = 0;
    for (int c = 1; c <= cycles; c++)
    {
        Thread.Sleep(delayMs);
        revertTimes.Add(Move(revertible));
        Thread.Sleep(delayMs);
        if (batch)
        {
            var (_, s) = ApplyBatch(layout, async);
            applyTimes.Add(s.MoveTime.TotalMilliseconds);
            corrections += s.Corrections;
            failedBatches += s.Batched ? 0 : 1;
        }
        else
        {
            ApplySequential(layout, null, out var elapsed);
            applyTimes.Add(elapsed.TotalMilliseconds);
        }

        Console.Write($"\r  ciclo {c}/{cycles}: aplicar {applyTimes[^1],5:F0} ms · revertir {revertTimes[^1],5:F0} ms");
    }

    Console.WriteLine();
    Console.WriteLine($"  aplicar:  media {applyTimes.Average():F0} ms · máx {applyTimes.Max():F0} ms");
    Console.WriteLine($"  revertir: media {revertTimes.Average():F0} ms · máx {revertTimes.Max():F0} ms");
    if (batch)
    {
        Console.WriteLine($"  {corrections} correcciones individuales · {failedBatches} lotes fallidos");
    }

    double Move(List<(nint Handle, PixelRect Rect)> items)
    {
        if (batch)
        {
            return WindowMover.PlaceBatch(items, async).MoveTime.TotalMilliseconds;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (h, r) in items)
        {
            WindowMover.Place(h, r);
        }

        return clock.Elapsed.TotalMilliseconds;
    }
}

static List<(WindowTarget Target, PlacementResult? Result)> ApplySequential(
    SceneLayoutResult layout, int? pauseAfter, out TimeSpan elapsed)
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var applied = new List<(WindowTarget Target, PlacementResult? Result)>();
    int placed = 0;
    foreach (var target in layout.Targets)
    {
        if (pauseAfter is int limit && placed == limit)
        {
            // S5: deja el proceso detenido a mitad de la escena para matarlo desde fuera.
            Console.WriteLine($"PAUSA tras {placed} ventanas (pid {Environment.ProcessId}). Esperando a ser terminado…");
            Thread.Sleep(Timeout.Infinite);
        }

        placed++;
        if (target.Kind == TargetKind.Minimized)
        {
            WindowMover.Minimize(target.Handle);
            applied.Add((target, null));
        }
        else
        {
            applied.Add((target, WindowMover.Place(target.Handle, target.Rect)));
        }
    }

    elapsed = clock.Elapsed;
    return applied;
}

static int Report(SceneLayoutResult layout, List<(WindowTarget Target, PlacementResult? Result)> applied)
{
    Console.WriteLine();
    Console.WriteLine($"{"Región",-9} {"App",-18} {"Objetivo",-24} {"Obtenido",-24} {"ΔL",4} {"ΔT",4} {"ΔR",4} {"ΔB",4} {"Bordes",-10}  Resultado");
    Console.WriteLine(new string('-', 130));
    int exact = 0;
    foreach (var (t, r) in applied)
    {
        if (r is null)
        {
            Console.WriteLine($"{t.Region,-9} {Trim(t.Label, 18),-18} (minimizada)");
            exact++;
            continue;
        }

        var a = r.Actual;
        string outcome = r.Error ?? (r.IsExact ? "exacto" : $"desviación {r.MaxDeviation}px");
        if (r.IsExact)
        {
            exact++;
        }

        Console.WriteLine(
            $"{t.Region,-9} {Trim(t.Label, 18),-18} {t.Rect,-24} {a,-24} " +
            $"{a.Left - t.Rect.Left,4} {a.Top - t.Rect.Top,4} {a.Right - t.Rect.Right,4} {a.Bottom - t.Rect.Bottom,4} " +
            $"{r.Margins,-10}  {outcome}");
    }

    var centerInvaders = applied
        .Where(x => x.Result is not null && x.Target.Region != "Centro")
        .Where(x => !x.Result!.Actual.Intersect(layout.CenterColumn).IsEmpty)
        .Select(x => x.Target.Label)
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"{exact}/{applied.Count} ventanas en su destino (±1 px).");
    Console.WriteLine(centerInvaders.Count == 0
        ? "Centro protegido: ninguna ventana de panel lo invade."
        : $"CENTRO INVADIDO por: {string.Join(", ", centerInvaders)}");
    Console.WriteLine("Para devolverlas a su lugar: focus-probe restore");
    return exact == applied.Count && centerInvaders.Count == 0 ? 0 : 1;
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
    if (Has(args, "--clear"))
    {
        MinSizeStore.Save(new MinSizeCatalog());
        Console.WriteLine("Mínimos olvidados.");
        return 0;
    }

    var catalog = MinSizeStore.Load();
    Console.WriteLine(MinSizeStore.FilePath);
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
    var entries = ProbeJournal.Load();
    if (entries.Count == 0)
    {
        Console.WriteLine("El diario está vacío: no hay nada que restaurar.");
        return 0;
    }

    int ok = 0;
    int gone = 0;
    var pending = new List<SavedPlacement>();
    foreach (var e in entries)
    {
        string verdict;
        if (!WindowMover.IsSameWindow(e.Handle, e.ProcessId))
        {
            // La ventana se cerró: no hay nada que restaurar y su entrada ya no sirve.
            verdict = "ya no existe (se descarta)";
            gone++;
        }
        else if (WindowMover.Restore(e) is { } error)
        {
            // Sigue viva pero no se pudo restaurar: se conserva para reintentar.
            verdict = $"{error} (se conserva en el diario)";
            pending.Add(e);
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

    Console.WriteLine();
    Console.WriteLine($"{ok} restauradas exactamente · {gone} ya cerradas · {pending.Count} pendientes · {entries.Count - ok - gone - pending.Count} con diferencias");
    if (pending.Count == 0)
    {
        ProbeJournal.Delete();
        Console.WriteLine("Diario borrado.");
    }
    else
    {
        ProbeJournal.Save(pending);
        Console.WriteLine($"Diario conservado con {pending.Count} entradas pendientes.");
    }

    return ok + gone == entries.Count ? 0 : 1;
}

static string Trim(string value, int max) =>
    value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");

internal enum MoveMode
{
    Async,
    Batch,
    Sequential,
}
