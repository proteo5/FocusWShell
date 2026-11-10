namespace FocusWShell.Win32.Persistence;

/// <summary>Qué pasó con una entrada del diario al restaurar.</summary>
public enum RestoreOutcome
{
    /// <summary>Volvió exactamente a su colocación original. Sale del diario.</summary>
    Restored,

    /// <summary>Se restauró, pero ni con dos pasadas coincide la colocación. Se conserva para reintentar.</summary>
    RestoredWithDifferences,

    /// <summary>La ventana ya no existe. Su entrada se descarta.</summary>
    Gone,

    /// <summary>Sigue viva pero no se pudo restaurar (p. ej. no responde). Se conserva para reintentar.</summary>
    Pending,
}

public sealed record RestoreEntryResult(SavedPlacement Entry, RestoreOutcome Outcome, string Detail);

public sealed record RestoreReport(IReadOnlyList<RestoreEntryResult> Entries)
{
    public int Count(RestoreOutcome outcome) => Entries.Count(e => e.Outcome == outcome);

    public bool IsComplete => Entries.All(e => e.Outcome is RestoreOutcome.Restored or RestoreOutcome.Gone);

    public string Summary =>
        $"{Count(RestoreOutcome.Restored)} restauradas · {Count(RestoreOutcome.Gone)} ya cerradas · " +
        $"{Count(RestoreOutcome.Pending)} pendientes · {Count(RestoreOutcome.RestoredWithDifferences)} con diferencias";
}

/// <summary>Aplica el diario directamente, sin pasar por el motor de layout (ADR 0005).</summary>
public static class JournalRestorer
{
    /// <summary>
    /// Restaura las entradas que cumplen <paramref name="which"/> (todas por defecto) y deja en el
    /// diario sólo las que no se tocaron y las pendientes.
    /// </summary>
    public static RestoreReport Restore(RecoveryJournal journal, Func<SavedPlacement, bool>? which = null)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var entries = journal.Load();
        var keep = new List<SavedPlacement>();
        var results = new List<RestoreEntryResult>();
        foreach (var e in entries)
        {
            if (which is not null && !which(e))
            {
                keep.Add(e);
                continue;
            }

            var result = RestoreOne(e);
            results.Add(result);
            // Una restauración imperfecta conserva la entrada: el original no se pierde y se puede reintentar.
            if (result.Outcome is RestoreOutcome.Pending or RestoreOutcome.RestoredWithDifferences)
            {
                keep.Add(e);
            }
        }

        journal.Save(keep);
        return new RestoreReport(results);
    }

    private static RestoreEntryResult RestoreOne(SavedPlacement e)
    {
        if (!WindowMover.IsSameWindow(e.Handle, e.ProcessId))
        {
            return new(e, RestoreOutcome.Gone, "ya no existe");
        }

        // Si la ventana cruza a un monitor con otro DPI, la app se reescala al llegar y el tamaño
        // queda mal: una segunda pasada, ya en el monitor correcto, lo deja exacto.
        SavedPlacement? now = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (WindowMover.Restore(e) is { } error)
            {
                return new(e, RestoreOutcome.Pending, error);
            }

            now = WindowMover.Capture((nint)e.Handle, e.ProcessId, e.ProcessName, e.Title);
            if (now is not null && now.NormalPosition == e.NormalPosition && now.ShowCmd == e.ShowCmd)
            {
                return new(e, RestoreOutcome.Restored, attempt == 1 ? "restaurada" : "restaurada (2 pasadas)");
            }
        }

        return new(e, RestoreOutcome.RestoredWithDifferences,
            $"quedó en {now?.NormalPosition} show {now?.ShowCmd} (original {e.NormalPosition} show {e.ShowCmd}); se conserva en el diario");
    }
}
