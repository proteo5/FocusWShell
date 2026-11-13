using System.IO;
using System.Text.Json;

namespace FocusWShell.App;

/// <summary>Preferencias del usuario: <c>%LOCALAPPDATA%\FocusWShell\settings.json</c> (03-arquitectura, Persistencia).</summary>
public sealed class AppSettings
{
    // Sin escapar "+" ni acentos: el archivo se lee a mano (p. ej. los atajos, "Ctrl+Alt+Shift+Up").
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusWShell",
        "settings.json");

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Idioma de la interfaz: <c>auto</c> (el de Windows), <c>es</c> o <c>en</c>. Se aplica al arrancar.</summary>
    public string Language { get; set; } = Localization.Auto;

    /// <summary>Tema de la ventana principal: <c>auto</c> (el de Windows), <c>light</c> o <c>dark</c>.</summary>
    public string Theme { get; set; } = "auto";

    /// <summary>Mantener la pantalla encendida (ADR 0010). Se recuerda entre sesiones.</summary>
    public bool KeepDisplayOn { get; set; }

    /// <summary>Alt+Tab a una ventana de un panel la lleva al foco (ADR 0003, enmienda).</summary>
    public bool FocusOnAltTab { get; set; } = true;

    /// <summary>Elegirla en la vista de tareas (Win+Tab) la lleva al foco.</summary>
    public bool FocusOnTaskView { get; set; } = true;

    /// <summary>Su botón en la barra de tareas la lleva al foco.</summary>
    public bool FocusOnTaskbar { get; set; } = true;

    /// <summary>
    /// Atajos cambiados por el usuario, por acción (p. ej. "al-foco" → "Win+Alt+Up"). Las que no están
    /// usan el de siempre (<see cref="FocusWShell.Core.Input.Hotkeys.Actions"/>).
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];

    /// <summary>Monitor donde está fijado el deck (ADR 0013, fase 4); <c>null</c>: la esquina del principal.</summary>
    public string? DeckDisplay { get; set; }

    /// <summary>Zona del deck fijado: LeftHalf, RightHalf o Whole.</summary>
    public string? DeckZone { get; set; }

    /// <summary>Al aplicar una escena, abrir las apps que falten y ponerlas en su panel (ADR 0011).</summary>
    public bool OpenMissingApps { get; set; } = true;

    /// <summary>
    /// Lista de exclusión (M2): apps o clases de ventana que FocusWShell no gestiona ni muestra en la lista.
    /// </summary>
    public List<FocusWShell.Core.Windows.ExclusionRule> Exclusions { get; set; } = [];

    /// <summary>Un clic en la propia ventana la lleva al foco. Desactivado: las ventanas saltarían a cada clic.</summary>
    public bool FocusOnClick { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings(); // instalación nueva: el idioma de Windows
            }

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();

            // Una configuración de antes de los idiomas: la app siempre fue en español, así que sigue en español
            // (si Windows está en inglés, "automático" la cambiaría de golpe sin que nadie lo pidiera).
            if (!json.Contains("\"Language\"", StringComparison.Ordinal))
            {
                settings.Language = Localization.Spanish;
            }

            return settings;
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
