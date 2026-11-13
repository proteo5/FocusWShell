using System.Reflection;

namespace FocusWShell.App;

/// <summary>La versión de FocusWShell (la de Directory.Build.props), para mostrarla y en los reportes.</summary>
public static class AppVersion
{
    /// <summary>Por ejemplo, «0.1.0-alpha» (sin el commit que el compilador añade tras «+»).</summary>
    public static string Display { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>El commit del que se compiló, abreviado (vacío si no se sabe).</summary>
    public static string Commit { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+') is [_, var sha, ..] ? sha[..Math.Min(7, sha.Length)] : "";

    /// <summary>«FocusWShell 0.1.0-alpha».</summary>
    public static string Title => $"FocusWShell {Display}";
}
