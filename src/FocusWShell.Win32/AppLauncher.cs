using System.ComponentModel;
using System.Diagnostics;
using FocusWShell.Core.Domain;

namespace FocusWShell.Win32;

/// <summary>Abre una app del catálogo (ADR 0011).</summary>
public static class AppLauncher
{
    /// <summary>
    /// Las apps empaquetadas (AUMID con "!", p. ej. WhatsApp de la Store) se abren por
    /// <c>shell:AppsFolder</c>; el resto, por su ejecutable. Devuelve un error legible o <c>null</c>.
    /// </summary>
    public static string? Launch(CatalogApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        try
        {
            if (app.Aumid is { } aumid && aumid.Contains('!', StringComparison.Ordinal))
            {
                using var shell = Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + aumid) { UseShellExecute = true });
                return null;
            }

            if (app.ExecutablePath is { } path && File.Exists(path))
            {
                using var process = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                });
                return null;
            }

            return $"No se sabe cómo abrir {app.Name}: no hay ejecutable ni AUMID.";
        }
        catch (Win32Exception ex)
        {
            return $"No se pudo abrir {app.Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Nombre de una app: el que le da Windows por su AUMID si es una app empaquetada (p. ej. "Calculator",
    /// alojada en ApplicationFrameHost) y, si no, la descripción de su ejecutable.
    /// </summary>
    public static string? FriendlyName(string? executablePath, string? aumid) =>
        (aumid is not null && aumid.Contains('!', StringComparison.Ordinal) ? PackagedName(aumid) : null) ?? FriendlyName(executablePath);

    /// <summary>Nombre de una app empaquetada en <c>shell:AppsFolder</c>, o <c>null</c>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Si el shell falla, se usa el nombre del ejecutable.")]
    public static string? PackagedName(string aumid)
    {
        object? shell = null, folder = null, item = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(type);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            item = folder is null ? null : ((dynamic)folder).ParseName(aumid);
            string? name = item is null ? null : (string)((dynamic)item).Name;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            foreach (var o in new[] { item, folder, shell })
            {
                if (o is not null && System.Runtime.InteropServices.Marshal.IsComObject(o))
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(o);
                }
            }
        }
    }

    /// <summary>Nombre amigable de un ejecutable (su descripción, p. ej. "Microsoft Edge"), o <c>null</c>.</summary>
    public static string? FriendlyName(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            string file = Path.GetFileName(executablePath);

            // Las apps Electron se describen como "Electron" y algunas como su propio archivo
            // ("Notepad.exe"): en esos casos manda el nombre del producto.
            static bool Useful(string? value, string file) =>
                !string.IsNullOrWhiteSpace(value)
                && !value.Trim().Equals("Electron", StringComparison.OrdinalIgnoreCase)
                && !value.Trim().Equals(file, StringComparison.OrdinalIgnoreCase)
                && !value.Contains("Operating System", StringComparison.OrdinalIgnoreCase);

            if (Useful(info.FileDescription, file))
            {
                return info.FileDescription!.Trim();
            }

            return Useful(info.ProductName, file) ? info.ProductName!.Trim() : Path.GetFileNameWithoutExtension(file);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
