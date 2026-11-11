using System.Globalization;

namespace FocusWShell.Core;

/// <summary>Textos con valores: <c>Text.F(Strings.X, a, b)</c>, con la cultura de la interfaz.</summary>
public static class Text
{
    public static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
