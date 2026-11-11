using System.Globalization;
using System.Runtime.CompilerServices;

namespace FocusWShell.Core.Tests;

/// <summary>Las pruebas comprueban textos en español (el idioma base): se fijan en español sea cual sea el de Windows.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Spanish()
    {
        var es = CultureInfo.GetCultureInfo("es");
        CultureInfo.DefaultThreadCurrentUICulture = es;
        CultureInfo.DefaultThreadCurrentCulture = es;
        CultureInfo.CurrentUICulture = es;
        CultureInfo.CurrentCulture = es;
    }
}
