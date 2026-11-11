namespace FocusWShell.Core.Windows;

/// <summary>Por qué una ventana no es candidata a ser gestionada.</summary>
public enum ExclusionReason
{
    None = 0,
    OwnProcess,
    NotVisible,
    Cloaked,
    SystemClass,
    ToolWindow,
    NoActivate,
    Owned,
    NoTitle,
    NoCaption,
    TooSmall,
    Fullscreen,
    UserExcluded,
}

/// <summary>Resultado de evaluar una ventana.</summary>
/// <param name="Reason">Motivo de exclusión, o <see cref="ExclusionReason.None"/> si es candidata.</param>
/// <param name="Manageable">
/// Si el gestor puede moverla. Una candidata puede no ser gestionable si su proceso
/// tiene más integridad que el gestor (UIPI lo impide).
/// </param>
public readonly record struct Eligibility(ExclusionReason Reason, bool Manageable)
{
    public bool IsCandidate => Reason == ExclusionReason.None;

    public static Eligibility Excluded(ExclusionReason reason) => new(reason, Manageable: false);
}

/// <summary>
/// Ventanas que se ven pero el gestor no puede mover (M2): su proceso tiene más integridad que el gestor
/// (UIPI) o no se pudo leer. Se muestran igual, con el motivo, para que no "desaparezcan".
/// </summary>
public static class Unmanageable
{
    /// <summary>Motivo para la UI, o <c>null</c> si sí se puede gestionar.</summary>
    public static string? Reason(IntegrityLevel target, IntegrityLevel own) => target switch
    {
        _ when EligibilityFilter.CanControl(target, own) => null,
        IntegrityLevel.Unknown => "no se pudo leer su nivel de permisos (protegida o como administrador)",
        IntegrityLevel.System => "es del sistema",
        _ => "corre como administrador",
    };
}
