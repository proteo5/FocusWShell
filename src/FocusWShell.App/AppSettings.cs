using System.IO;
using System.Text.Json;

namespace FocusWShell.App;

/// <summary>Preferencias del usuario: <c>%LOCALAPPDATA%\FocusWShell\settings.json</c> (03-arquitectura, Persistencia).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "settings.json");

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Mantener la pantalla encendida (ADR 0010). Se recuerda entre sesiones.</summary>
    public bool KeepDisplayOn { get; set; }

    /// <summary>Alt+Tab a una ventana de un panel la lleva al foco (ADR 0003, enmienda).</summary>
    public bool FocusOnAltTab { get; set; } = true;

    /// <summary>Elegirla en la vista de tareas (Win+Tab) la lleva al foco.</summary>
    public bool FocusOnTaskView { get; set; } = true;

    /// <summary>Su botón en la barra de tareas la lleva al foco.</summary>
    public bool FocusOnTaskbar { get; set; } = true;

    /// <summary>Al aplicar una escena, abrir las apps que falten y ponerlas en su panel (ADR 0011).</summary>
    public bool OpenMissingApps { get; set; } = true;

    /// <summary>Un clic en la propia ventana la lleva al foco. Desactivado: las ventanas saltarían a cada clic.</summary>
    public bool FocusOnClick { get; set; }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            // Un archivo dañado no debe impedir arrancar: se vuelve a los valores por defecto.
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
