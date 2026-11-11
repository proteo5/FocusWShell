using System.Diagnostics;
using Windows.Win32;

namespace FocusWShell.Win32;

/// <summary>
/// Energía desde el deck: bloquear, suspender, reiniciar, apagar y cerrar sesión, como desde el menú Inicio.
/// Ninguna libera las ventanas: al volver, la sesión de FocusWShell las pone otra vez en sus paneles.
/// </summary>
public static class Power
{
    /// <summary>Bloquea el equipo (como Win+L).</summary>
    public static bool Lock() => PInvoke.LockWorkStation();

    /// <summary>Suspende el equipo (no hiberna).</summary>
    public static bool Sleep() => PInvoke.SetSuspendState(false, false, false);

    /// <summary>Reinicia el equipo.</summary>
    public static void Restart() => Shutdown("/r /t 0");

    /// <summary>
    /// Apaga el equipo. Con <c>/hybrid</c>, como el menú Inicio: respeta el inicio rápido si está activado.
    /// </summary>
    public static void ShutDown() => Shutdown("/s /hybrid /t 0");

    /// <summary>Cierra la sesión del usuario.</summary>
    public static void SignOut() => Shutdown("/l");

    private static void Shutdown(string arguments)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        using var process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true });
    }
}
