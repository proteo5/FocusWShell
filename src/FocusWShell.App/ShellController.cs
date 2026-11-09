using FocusWShell.Core.Displays;
using FocusWShell.Core.Windows;
using FocusWShell.Win32;
using FocusWShell.Win32.Persistence;
using FocusWShell.Win32.Scenes;

namespace FocusWShell.App;

/// <summary>
/// Punto único por el que la app mueve o restaura ventanas. Serializa las operaciones:
/// el atajo de emergencia llega desde otro hilo y no debe cruzarse con una aplicación en curso.
/// </summary>
public sealed class ShellController
{
    private readonly Lock _gate = new();
    private readonly EligibilityFilter _filter = new();

    public RecoveryJournal Journal { get; } = new(RecoveryJournal.DefaultPath);

    public MinSizeStore MinSizes { get; } = new(MinSizeStore.DefaultPath);

    /// <summary>Monitor donde se aplica el layout fijo de M0: el más ancho.</summary>
    public static DisplayInfo TargetDisplay() =>
        DisplayEnumerator.Enumerate().MaxBy(d => d.WorkArea.Width)
        ?? throw new InvalidOperationException("No se encontró ningún monitor.");

    /// <summary>Ventanas que el gestor puede colocar, en orden Z (la de arriba primero).</summary>
    public IReadOnlyList<WindowSnapshot> ListManageable()
    {
        var own = WindowEnumerator.CurrentIntegrity();
        return WindowEnumerator.EnumerateTopLevel()
            .Where(w => _filter.Evaluate(w, own) is { IsCandidate: true, Manageable: true })
            .ToList();
    }

    /// <summary>
    /// Aplica la escena. Antes devuelve a su sitio las ventanas gestionadas que ya no tienen rol
    /// (equivale a <c>Release</c>), para que nada quede movido sin estar en el layout.
    /// </summary>
    public (RestoreReport Released, SceneApplyReport Applied) Apply(SceneAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        lock (_gate)
        {
            var keep = assignment.All.Select(w => (long)w.Handle).ToHashSet();
            var released = JournalRestorer.Restore(Journal, e => !keep.Contains(e.Handle));
            var applier = new SceneApplier(TargetDisplay(), assignment, new SceneOptions(), MinSizes);
            return (released, applier.Apply(Journal));
        }
    }

    /// <summary><c>RestoreAll</c>: aplica el diario directamente, sin motor de layout.</summary>
    public RestoreReport RestoreAll()
    {
        lock (_gate)
        {
            return JournalRestorer.Restore(Journal);
        }
    }

    public int PendingCount => Journal.Load().Count;
}
