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
