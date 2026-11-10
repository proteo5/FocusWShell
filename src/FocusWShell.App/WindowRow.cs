using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using FocusWShell.Core.Windows;

namespace FocusWShell.App;

/// <summary>Una opción de la columna Panel: libre, al foco o un panel concreto.</summary>
public sealed record AssignmentOption(string Key, string Label)
{
    /// <summary>Ventana libre: FocusWShell no la gestiona (ADR 0011).</summary>
    public const string Free = "";

    /// <summary>En el foco (el Centro del monitor principal).</summary>
    public const string Focus = "@foco";
}

/// <summary>Una ventana en la lista de la ventana principal, con lo que el usuario quiere hacer con ella.</summary>
public sealed class WindowRow(WindowSnapshot snapshot, string assignment, string? appName = null) : INotifyPropertyChanged
{
    public WindowSnapshot Snapshot { get; } = snapshot;

    /// <summary>Nombre de la app (el del catálogo si se conoce; si no, el del proceso).</summary>
    public string App { get; } = appName ?? Path.GetFileNameWithoutExtension(snapshot.ProcessName);

    public string Title => Snapshot.Title;

    /// <summary>Lo que el gestor hace ahora con la ventana, según el modelo.</summary>
    public string State { get; init; } = "";

    /// <summary>Destino elegido: <see cref="AssignmentOption.Free"/>, <see cref="AssignmentOption.Focus"/> o el id de un panel.</summary>
    public string Assignment
    {
        get => assignment;
        set
        {
            if (assignment != value)
            {
                assignment = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Nombre accesible de la fila (lectores de pantalla, UI Automation).</summary>
    public override string ToString() => $"{App} — {Title}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
