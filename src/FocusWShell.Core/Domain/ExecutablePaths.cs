namespace FocusWShell.Core.Domain;

/// <summary>
/// Rutas de ejecutables que se quedan viejas: muchas apps se instalan en una carpeta por versión
/// (<c>Drive File Stream\133.0.2.0\…</c>, <c>WindowsApps\Claude_4.x…\…</c>) y al actualizarse la anterior
/// desaparece. El catálogo guarda la última ruta vista; esto busca la actual si la app está cerrada.
/// </summary>
public static class ExecutablePaths
{
    /// <summary>
    /// La ruta si existe; si no, la misma ruta relativa en la carpeta hermana más nueva (subiendo hasta dos
    /// niveles: la carpeta de versión puede contener subcarpetas). <c>null</c> si no hay ninguna.
    /// </summary>
    /// <param name="exists">Si un archivo existe.</param>
    /// <param name="directories">Subcarpetas de una carpeta (vacío si no se puede leer).</param>
    public static string? Resolve(string path, Func<string, bool> exists, Func<string, IEnumerable<string>> directories)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(directories);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (exists(path))
        {
            return path;
        }

        var versionDir = Path.GetDirectoryName(path);
        for (int level = 0; level < 2 && versionDir is not null; level++, versionDir = Path.GetDirectoryName(versionDir))
        {
            var parent = Path.GetDirectoryName(versionDir);
            if (parent is null)
            {
                break;
            }

            var relative = Path.GetRelativePath(versionDir, path);
            var found = directories(parent)
                .Where(d => !string.Equals(d, versionDir, StringComparison.OrdinalIgnoreCase))
                .Select(d => (Version: VersionOf(Path.GetFileName(d)), File: Path.Combine(d, relative)))
                .Where(c => exists(c.File))
                .OrderByDescending(c => c.Version)
                .Select(c => c.File)
                .FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// AUMID probable de una app de la Store a partir de su carpeta en <c>WindowsApps</c>
    /// (<c>Nombre_Versión_Arquitectura_Recurso_Editor</c>): <c>Nombre_Editor!App</c>. Sólo para pedir su
    /// icono al shell: el id de la app ("App") es el habitual, no está garantizado.
    /// </summary>
    public static string? GuessPackagedAumid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int index = Array.FindIndex(parts, p => string.Equals(p, "WindowsApps", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= parts.Length)
        {
            return null;
        }

        var fields = parts[index + 1].Split('_');
        return fields.Length >= 5 && fields[0].Length > 0 && fields[^1].Length > 0
            ? $"{fields[0]}_{fields[^1]}!App"
            : null;
    }

    /// <summary>Versión de un nombre de carpeta ("134.0.1.0", "app-3.6.10"), para quedarse con la más nueva.</summary>
    private static Version VersionOf(string? folder)
    {
        var digits = new string((folder ?? "").SkipWhile(c => !char.IsDigit(c)).ToArray());
        return Version.TryParse(digits, out var v) ? v : new Version(0, 0);
    }
}
