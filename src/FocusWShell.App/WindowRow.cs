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

/// <summary>Monitor de un panel en la UI. La clave del principal es la cadena vacía.</summary>
public sealed record DisplayOption(string Key, string Label)
{
    public const string PrincipalKey = "";

    /// <summary>Clave del modelo: <c>null</c> para el principal.</summary>
    public static string? ToModel(string key) => key == PrincipalKey ? null : key;

    public static string FromModel(string? display) => display ?? PrincipalKey;
}

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

    /// <summary>Lo que el gestor hace ahora con la ventana, según el modelo.</summary>
    public string State { get; init; } = "";

    /// <summary>Monitor del panel (<see cref="DisplayOption.Key"/>).</summary>
    public string Display
    {
        get => _display;
        set
        {
            if (_display != value)
            {
                _display = value;
                OnPropertyChanged();
            }
        }
    }

    private string _display = DisplayOption.PrincipalKey;

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
