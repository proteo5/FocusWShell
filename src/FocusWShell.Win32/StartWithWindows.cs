using Microsoft.Win32;

namespace FocusWShell.Win32;

/// <summary>
/// "Iniciar con Windows": el valor <c>FocusWShell</c> en
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Sólo para el usuario actual y sin elevación.
/// </summary>
public static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FocusWShell";

    /// <summary>La ruta registrada, sin comillas, o <c>null</c> si no se inicia con Windows.</summary>
    public static string? RegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Trim().Trim('"') is { Length: > 0 } path ? path : null;
    }

    /// <summary>Si se inicia con Windows este ejecutable (y no otra copia).</summary>
    public static bool IsEnabledFor(string exePath) =>
        RegisteredPath() is { } path && string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase);

    public static void Enable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
