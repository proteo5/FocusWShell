namespace FocusWShell.Core.Windows;

/// <summary>
/// Decide qué ventanas top-level son candidatas a ser gestionadas.
/// Las reglas están descritas en docs/03-arquitectura.md#elegibilidad-de-ventanas.
/// </summary>
public sealed class EligibilityFilter
{
    /// <summary>Clases de ventana del sistema que nunca se gestionan.</summary>
    public static readonly IReadOnlySet<string> DefaultSystemClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",              // barra de tareas principal
        "Shell_SecondaryTrayWnd",     // barra de tareas en otros monitores
        "Progman",                    // escritorio
        "WorkerW",                    // escritorio / fondo
        "Windows.UI.Core.CoreWindow", // superficies de shell (Inicio, búsqueda, centro de acciones)
        "Shell_InputSwitchTopLevelWindow",
        "XamlExplorerHostIslandWindow", // ventanas emergentes de la barra de tareas de Windows 11
        "TopLevelWindowForOverflowXamlIsland",
        "NotifyIconOverflowWindow",
        "MultitaskingViewFrame",      // Alt+Tab / vista de tareas
        "ForegroundStaging",
        "#32768",                     // menús emergentes
        "#32770",                     // cuadros de diálogo estándar
        "Credential Dialog Xaml Host",
        "tooltips_class32",
    };

    private readonly IReadOnlySet<string> _systemClasses;
    private readonly Func<WindowSnapshot, bool> _userExcluded;

    /// <param name="minVisibleSize">Ancho y alto mínimos (px físicos) del área visible.</param>
    /// <param name="systemClasses">Clases a excluir; por defecto <see cref="DefaultSystemClasses"/>.</param>
    /// <param name="userExcluded">Regla de exclusión del usuario (ejecutable, AUMID, clase…).</param>
    public EligibilityFilter(
        int minVisibleSize = 120,
        IReadOnlySet<string>? systemClasses = null,
        Func<WindowSnapshot, bool>? userExcluded = null)
    {
        MinVisibleSize = minVisibleSize;
        _systemClasses = systemClasses ?? DefaultSystemClasses;
        _userExcluded = userExcluded ?? (_ => false);
    }

    public int MinVisibleSize { get; }

    /// <param name="ownIntegrity">Nivel de integridad del proceso del gestor.</param>
    public Eligibility Evaluate(WindowSnapshot window, IntegrityLevel ownIntegrity)
    {
        ArgumentNullException.ThrowIfNull(window);

        var reason = FindExclusion(window);
        if (reason != ExclusionReason.None)
        {
            return Eligibility.Excluded(reason);
        }

        return new Eligibility(ExclusionReason.None, CanControl(window.Integrity, ownIntegrity));
    }

    private ExclusionReason FindExclusion(WindowSnapshot w)
    {
        if (w.IsOwnProcess)
        {
            return ExclusionReason.OwnProcess;
        }

        if (!w.IsVisible)
        {
            return ExclusionReason.NotVisible;
        }

        if (w.IsCloaked)
        {
            return ExclusionReason.Cloaked;
        }

        if (_systemClasses.Contains(w.ClassName))
        {
            return ExclusionReason.SystemClass;
        }

        if (w.IsToolWindow && !w.IsAppWindow)
        {
            return ExclusionReason.ToolWindow;
        }

        if (w.IsNoActivate && !w.IsAppWindow)
        {
            return ExclusionReason.NoActivate;
        }

        // Diálogos y ventanas auxiliares tienen dueño; WS_EX_APPWINDOW las fuerza a la barra de tareas.
        if (w.HasOwner && !w.IsAppWindow)
        {
            return ExclusionReason.Owned;
        }

        if (string.IsNullOrWhiteSpace(w.Title))
        {
            return ExclusionReason.NoTitle;
        }

        if (_userExcluded(w))
        {
            return ExclusionReason.UserExcluded;
        }

        // Una ventana sin barra de título que cubre exactamente su monitor es pantalla completa propia
        // (juego, vídeo, presentación). El gestor deja ese monitor en paz.
        if (!w.HasCaption && !w.MonitorBounds.IsEmpty && w.VisibleBounds == w.MonitorBounds)
        {
            return ExclusionReason.Fullscreen;
        }

        // Sólo para ventanas no minimizadas: una minimizada tiene un rectángulo diminuto fuera de pantalla.
        if (!w.IsMinimized && (w.VisibleBounds.Width < MinVisibleSize || w.VisibleBounds.Height < MinVisibleSize))
        {
            return ExclusionReason.TooSmall;
        }

        // Ventanas sin barra de título ni marco redimensionable suelen ser popups, splash screens u overlays.
        // Excepción: apps con barra de título dibujada por ellas mismas (Chromium, Electron, Terminal)
        // conservan WS_THICKFRAME aunque quiten WS_CAPTION.
        if (!w.HasCaption && !w.IsResizable && !w.IsAppWindow)
        {
            return ExclusionReason.NoCaption;
        }

        return ExclusionReason.None;
    }

    /// <summary>UIPI: un proceso no puede mover ventanas de un proceso con más integridad.</summary>
    internal static bool CanControl(IntegrityLevel target, IntegrityLevel own) =>
        target != IntegrityLevel.Unknown && target <= own;
}
