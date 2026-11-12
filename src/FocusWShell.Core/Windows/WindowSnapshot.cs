using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Windows;

/// <summary>Nivel de integridad del proceso dueño de una ventana.</summary>
public enum IntegrityLevel
{
    /// <summary>No se pudo consultar (acceso denegado, proceso protegido o ya terminado).</summary>
    Unknown = 0,
    Untrusted,
    Low,
    Medium,
    High,
    System,
}

/// <summary>
/// Foto de una ventana top-level tal como la ve Windows en un instante.
/// La capa Win32 la llena; el núcleo sólo la interpreta.
/// </summary>
public sealed record WindowSnapshot
{
    public required nint Handle { get; init; }

    public required string Title { get; init; }

    public required string ClassName { get; init; }

    public required int ProcessId { get; init; }

    /// <summary>Nombre del ejecutable (p. ej. <c>Code.exe</c>), o vacío si no se pudo leer.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Ruta completa del ejecutable, o vacío si no se pudo leer.</summary>
    public string ProcessPath { get; init; } = string.Empty;

    /// <summary>AppUserModelID de apps empaquetadas/UWP, si existe.</summary>
    public string? AppUserModelId { get; init; }

    public bool IsVisible { get; init; }

    /// <summary>Ocultada por DWM: otro escritorio virtual, app UWP suspendida, etc.</summary>
    public bool IsCloaked { get; init; }

    /// <summary>Oculta porque está en otro escritorio virtual de Windows (ADR 0016).</summary>
    public bool IsOnOtherDesktop { get; init; }

    public bool IsMinimized { get; init; }

    public bool IsMaximized { get; init; }

    public bool HasOwner { get; init; }

    public bool IsToolWindow { get; init; }

    public bool IsAppWindow { get; init; }

    public bool IsNoActivate { get; init; }

    /// <summary>Tiene barra de título (<c>WS_CAPTION</c>).</summary>
    public bool HasCaption { get; init; }

    /// <summary>Se puede redimensionar (<c>WS_THICKFRAME</c>).</summary>
    public bool IsResizable { get; init; }

    public bool IsTopmost { get; init; }

    /// <summary>Rectángulo completo, incluye bordes invisibles de redimensionado (<c>GetWindowRect</c>).</summary>
    public PixelRect Bounds { get; init; }

    /// <summary>Rectángulo visible (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>).</summary>
    public PixelRect VisibleBounds { get; init; }

    /// <summary>Rectángulo completo del monitor que contiene la mayor parte de la ventana.</summary>
    public PixelRect MonitorBounds { get; init; }

    public int Dpi { get; init; }

    public IntegrityLevel Integrity { get; init; }

    public bool IsOwnProcess { get; init; }
}
