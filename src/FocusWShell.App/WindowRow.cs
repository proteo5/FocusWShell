using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using FocusWShell.Core.Windows;

namespace FocusWShell.App;

public enum SceneRole
{
    None,
    Context,
    Center,
    Work,
}

public sealed record RoleOption(SceneRole Role, string Label);

/// <summary>Una ventana en la lista de la ventana principal, con el rol que le asigna el usuario.</summary>
public sealed class WindowRow(WindowSnapshot snapshot, SceneRole role) : INotifyPropertyChanged
{
    public static IReadOnlyList<RoleOption> Roles { get; } =
    [
        new(SceneRole.None, "—"),
        new(SceneRole.Context, "Contexto"),
        new(SceneRole.Center, "Centro"),
        new(SceneRole.Work, "Trabajo"),
    ];

    public WindowSnapshot Snapshot { get; } = snapshot;

    public string App => Path.GetFileNameWithoutExtension(Snapshot.ProcessName);

    public string Title => Snapshot.Title;

    public SceneRole Role
    {
        get => role;
        set
        {
            if (role != value)
            {
                role = value;
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
