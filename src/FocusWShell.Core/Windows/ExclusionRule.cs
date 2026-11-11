namespace FocusWShell.Core.Windows;

/// <summary>Con qué se identifica una ventana excluida (03-arquitectura, elegibilidad).</summary>
public enum ExclusionKind
{
    /// <summary>Nombre del ejecutable (<c>NetBird.exe</c>); con o sin <c>.exe</c>.</summary>
    Executable,

    /// <summary>AppUserModelID de una app empaquetada.</summary>
    Aumid,

    /// <summary>Clase de ventana (<c>GetClassName</c>): excluye un tipo de ventana, no la app entera.</summary>
    WindowClass,
}

/// <summary>
/// Una regla de la lista de exclusión del usuario (M2): las ventanas que la cumplen no se gestionan ni
/// aparecen en la lista, aunque pasen el resto de reglas de elegibilidad.
/// </summary>
public sealed record ExclusionRule(ExclusionKind Kind, string Value)
{
    public bool Matches(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (string.IsNullOrWhiteSpace(Value))
        {
            return false;
        }

        var value = Value.Trim();
        return Kind switch
        {
            ExclusionKind.Executable => Same(WithoutExe(window.ProcessName), WithoutExe(value)),
            ExclusionKind.Aumid => window.AppUserModelId is { } aumid && Same(aumid, value),
            ExclusionKind.WindowClass => Same(window.ClassName, value),
            _ => false,
        };
    }

    /// <summary>Si dos reglas excluyen lo mismo (para no repetirlas).</summary>
    public bool SameAs(ExclusionRule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Kind == other.Kind && (Kind == ExclusionKind.Executable
            ? Same(WithoutExe(Value.Trim()), WithoutExe(other.Value.Trim()))
            : Same(Value.Trim(), other.Value.Trim()));
    }

    /// <summary>Texto para la UI.</summary>
    public string Describe() => Kind switch
    {
        ExclusionKind.Executable => Text.F(Strings.Excl_App, Value),
        ExclusionKind.Aumid => Text.F(Strings.Excl_Packaged, Value),
        _ => Text.F(Strings.Excl_Class, Value),
    };

    /// <summary>La regla que excluye la app de una ventana: el AUMID si es empaquetada; si no, el ejecutable.</summary>
    public static ExclusionRule? ForApp(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return !string.IsNullOrWhiteSpace(window.AppUserModelId) ? new ExclusionRule(ExclusionKind.Aumid, window.AppUserModelId)
            : !string.IsNullOrWhiteSpace(window.ProcessName) ? new ExclusionRule(ExclusionKind.Executable, window.ProcessName)
            : null;
    }

    /// <summary>Si alguna regla excluye la ventana.</summary>
    public static bool AnyMatches(IEnumerable<ExclusionRule> rules, WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Any(r => r.Matches(window));
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string WithoutExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
