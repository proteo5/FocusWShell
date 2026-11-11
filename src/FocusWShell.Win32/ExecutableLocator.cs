using FocusWShell.Core.Domain;

namespace FocusWShell.Win32;

/// <summary><see cref="ExecutablePaths.Resolve"/> sobre el disco de verdad.</summary>
public static class ExecutableLocator
{
    /// <summary>La ruta si existe; si no, la de la versión más nueva junto a ella; <c>null</c> si no hay.</summary>
    public static string? Resolve(string path) => ExecutablePaths.Resolve(path, File.Exists, Directories);

    private static IEnumerable<string> Directories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return []; // p. ej. WindowsApps no se puede listar
        }
    }
}
