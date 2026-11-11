using System.Globalization;

namespace FocusWShell.App;

/// <summary>
/// Idioma de la interfaz: español o inglés. Se elige al arrancar, antes de crear ningún texto, porque el
/// deck, las tiras y las listas se construyen con los textos ya cargados; cambiarlo pide reiniciar.
/// </summary>
public static class Localization
{
    /// <summary>Según el idioma de Windows: español si Windows está en español; si no, inglés.</summary>
    public const string Auto = "auto";

    public const string Spanish = "es";

    public const string English = "en";

    /// <summary>El idioma que se usa con esa preferencia (<see cref="Auto"/>, <see cref="Spanish"/> o <see cref="English"/>).</summary>
    public static string Resolve(string? preference) => preference switch
    {
        Spanish => Spanish,
        English => English,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Spanish ? Spanish : English,
    };

    /// <summary>Aplica el idioma a toda la app (textos de la interfaz y del núcleo), en este hilo y en los nuevos.</summary>
    public static void Apply(string? preference)
    {
        var culture = CultureInfo.GetCultureInfo(Resolve(preference));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
