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
