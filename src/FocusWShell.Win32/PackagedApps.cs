using System.Collections.Concurrent;
using System.Xml;

namespace FocusWShell.Win32;

/// <summary>
/// Las apps de la Store (empaquetadas en <c>WindowsApps</c>) cuya ventana no dice su AUMID, como WhatsApp: su
/// ejecutable se describe con un nombre interno («WhatsApp.Root») y trae un icono genérico. Su AUMID sale del
/// paquete: la familia (nombre e id del editor, de la carpeta) y el id de la aplicación cuyo ejecutable es ese,
/// leído de su <c>AppxManifest.xml</c>. Con él, Windows da el nombre y el icono que muestra en Inicio.
/// </summary>
public static class PackagedApps
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>El AUMID de un ejecutable empaquetado, o <c>null</c> si no lo es o no se encuentra.</summary>
    public static string? AumidFor(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        return Cache.GetOrAdd(executablePath, Find);
    }

    private static string? Find(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int index = Array.FindIndex(parts, p => string.Equals(p, "WindowsApps", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 2 >= parts.Length)
        {
            return null;
        }

        // Nombre_Versión_Arquitectura_Recurso_Editor → familia Nombre_Editor.
        var fields = parts[index + 1].Split('_');
        if (fields.Length < 5 || fields[0].Length == 0 || fields[^1].Length == 0)
        {
            return null;
        }

        var packageFolder = string.Join(Path.DirectorySeparatorChar, parts.Take(index + 2));
        var relative = string.Join('\\', parts.Skip(index + 2));
        var appId = AppIdFor(Path.Combine(packageFolder, "AppxManifest.xml"), relative);
        return appId is null ? null : $"{fields[0]}_{fields[^1]}!{appId}";
    }

    /// <summary>El id de la aplicación del manifiesto cuyo <c>Executable</c> es ese archivo.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Un manifiesto ilegible: la app sigue con el nombre y el icono de su ejecutable.")]
    private static string? AppIdFor(string manifest, string relativeExe)
    {
        try
        {
            if (!File.Exists(manifest))
            {
                return null;
            }

            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                doc.Load(reader);
            }

            string Normal(string p) => p.Replace('/', '\\').TrimStart('\\');
            foreach (XmlElement app in doc.GetElementsByTagName("Application", "*"))
            {
                var exe = app.GetAttribute("Executable");
                if (exe.Length > 0 && string.Equals(Normal(exe), Normal(relativeExe), StringComparison.OrdinalIgnoreCase))
                {
                    var id = app.GetAttribute("Id");
                    return id.Length > 0 ? id : null;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
