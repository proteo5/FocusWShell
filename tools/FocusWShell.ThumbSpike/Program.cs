using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusWShell.ThumbSpike;

/// <summary>
/// Spike S10 (ADR 0015): ¿se ven en vivo las miniaturas DWM de ventanas tapadas por una ventana propia, o
/// minimizadas? ¿Sirve PrintWindow como alternativa? ¿Cuánto cuestan 20 miniaturas?
/// Escribe el resultado en la consola y en <c>s10-resultados.txt</c> junto al ejecutable.
/// Abre y cierra sus propias ventanas (y una ventana de Edge con un reloj); no minimiza ni tapa ventanas
/// del usuario: a ésas sólo les hace PrintWindow si están visibles.
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var screen = Screen.PrimaryScreen!.WorkingArea;
        if (args is ["coste"])
        {
            Cost(screen);
            return;
        }

        // Anfitrión de las miniaturas: arriba a la izquierda, siempre encima, para que nada lo tape.
        using var host = new Form
        {
            Text = "S10 anfitrión",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(screen.Left + 20, screen.Top + 20, 860, 540),
            BackColor = Color.FromArgb(10, 20, 30),
            TopMost = true,
            ShowInTaskbar = false,
        };
        host.Show();

        // Fuente propia: un contador que cambia de color diez veces por segundo.
        using var source = new AnimatedForm(new Rectangle(screen.Left + 1000, screen.Top + 120, 520, 400));
        source.Show();

        // Fuente Chromium: Edge con un reloj animado (archivo local).
        var clock = Path.Combine(AppContext.BaseDirectory, "s10-reloj.html");
        File.WriteAllText(clock, ClockHtml);
        var before = TopWindows().Select(w => w.Handle).ToHashSet();
        Process.Start(new ProcessStartInfo("msedge", $"--new-window \"{new Uri(clock).AbsoluteUri}\"") { UseShellExecute = true });
        nint edge = WaitFor(() => TopWindows().FirstOrDefault(w => !before.Contains(w.Handle) && w.Title.Contains("S10 reloj", StringComparison.Ordinal)).Handle, 15000);
        if (edge != 0)
        {
            Native.SetWindowPos(edge, 0, screen.Left + 1000, screen.Top + 560, 700, 420, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        Pump(1500);
        Write($"S10 · {DateTime.Now:yyyy-MM-dd HH:mm} · monitor principal {screen} · Edge {(edge == 0 ? "NO ENCONTRADO" : "ok")}");
        Write("");
        Write("1. Miniatura DWM en vivo (dos capturas separadas 900 ms: ¿cambia?; ¿está vacía?)");

        var dest = new Rectangle(10, 10, 400, 300);
        foreach (var (name, handle) in new[] { ("propia (WinForms)", source.Handle), ("Edge (Chromium)", edge) })
        {
            if (handle == 0)
            {
                continue;
            }

            Native.GetWindowRect(handle, out var r);
            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            Write($"   {name}");
            Write($"      visible:              {Thumb(host, handle, dest)}");

            using (var cover = Cover(bounds, opacity: 1.0))
            {
                Write($"      tapada (opaca):       {Thumb(host, handle, dest)}");
            }

            Pump(800);
            using (var cover = Cover(bounds, opacity: 0.99))
            {
                Write($"      tapada (semitransp.): {Thumb(host, handle, dest)}");
            }

            Pump(800);
            Native.ShowWindow(handle, Native.SW_SHOWMINNOACTIVE);
            Pump(800);
            Write($"      minimizada:           {Thumb(host, handle, dest)}");
            Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);
            Pump(800);
        }

        Write("");
        Write("2. PrintWindow (PW_RENDERFULLCONTENT): % de píxeles no negros y tiempo");
        foreach (var (name, handle) in new[] { ("propia", source.Handle), ("Edge reloj", edge) })
        {
            if (handle == 0)
            {
                continue;
            }

            Native.GetWindowRect(handle, out var r);
            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            Write($"   {name} visible:    {Print(handle)}");
            using (var cover = Cover(bounds, opacity: 1.0))
            {
                Write($"   {name} tapada:     {Print(handle)}");
            }

            Native.ShowWindow(handle, Native.SW_SHOWMINNOACTIVE);
            Pump(800);
            Write($"   {name} minimizada: {Print(handle)}");
            Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);
            Pump(800);
        }

        // Ventanas del usuario: sólo lectura, y sólo si están visibles.
        foreach (var w in TopWindows().Where(w => w.Handle != edge && w.Process is "msedge" or "chrome" or "slack" or "Code" && !Native.IsIconic(w.Handle)).Take(6))
        {
            Write($"   {w.Process} «{Short(w.Title)}»: {Print(w.Handle)}");
        }

        Write("");
        Write("3. Coste: 20 miniaturas registradas a la vez (CPU en 5 s)");
        var sources = TopWindows().Where(w => !Native.IsIconic(w.Handle) && w.Handle != host.Handle).Select(w => w.Handle).ToList();
        if (sources.Count > 0)
        {
            var idle = Cpu(5000);
            var thumbs = new List<nint>();
            for (int i = 0; i < 20; i++)
            {
                var cell = new Rectangle(10 + ((i % 5) * 168), 10 + ((i / 5) * 130), 160, 120);
                if (Native.DwmRegisterThumbnail(host.Handle, sources[i % sources.Count], out var t) == 0)
                {
                    Native.Show(t, cell);
                    thumbs.Add(t);
                }
            }

            var busy = Cpu(5000);
            foreach (var t in thumbs)
            {
                Native.DwmUnregisterThumbnail(t);
            }

            Write($"   sin miniaturas: {idle}");
            Write($"   con {thumbs.Count} (de {sources.Count} ventanas distintas): {busy}");
        }

        // Limpieza: cerrar sólo la ventana de Edge que abrimos.
        if (edge != 0)
        {
            Native.PostMessage(edge, Native.WM_CLOSE, 0, 0);
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "s10-resultados.txt"), Log.ToString());
        Console.WriteLine(Log.ToString());
    }

    /// <summary>
    /// Modo "coste": 15 s sin miniaturas y 15 s con 20 miniaturas de ventanas animadas (el peor caso: todas
    /// cambian sin parar). La CPU de dwm se mide desde fuera, con los contadores de rendimiento.
    /// </summary>
    private static void Cost(Rectangle screen)
    {
        using var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(screen.Left + 20, screen.Top + 20, 860, 540),
            BackColor = Color.FromArgb(10, 20, 30),
            TopMost = true,
            ShowInTaskbar = false,
        };
        host.Show();
        var sources = new List<AnimatedForm>();
        for (int i = 0; i < 4; i++)
        {
            var f = new AnimatedForm(new Rectangle(screen.Left + 1000 + (i * 60), screen.Top + 120 + (i * 60), 520, 400));
            f.Show();
            sources.Add(f);
        }

        Pump(15000); // fase 1: sin miniaturas
        var thumbs = new List<nint>();
        for (int i = 0; i < 20; i++)
        {
            var cell = new Rectangle(10 + ((i % 5) * 168), 10 + ((i / 5) * 130), 160, 120);
            if (Native.DwmRegisterThumbnail(host.Handle, sources[i % sources.Count].Handle, out var t) == 0)
            {
                Native.Show(t, cell);
                thumbs.Add(t);
            }
        }

        Pump(15000); // fase 2: con 20 miniaturas
        foreach (var t in thumbs)
        {
            Native.DwmUnregisterThumbnail(t);
        }

        foreach (var f in sources)
        {
            f.Close();
        }
    }

    private static string Thumb(Form host, nint source, Rectangle dest)
    {
        if (Native.DwmRegisterThumbnail(host.Handle, source, out var thumb) != 0)
        {
            return "no se pudo registrar";
        }

        try
        {
            Native.Show(thumb, dest);
            Pump(700);
            var screenRect = host.RectangleToScreen(dest);
            using var a = Capture(screenRect);
            Pump(900);
            using var b = Capture(screenRect);
            double changed = Diff(a, b);
            double empty = Fraction(a, c => Math.Abs(c.R - 10) < 4 && Math.Abs(c.G - 20) < 4 && Math.Abs(c.B - 30) < 4);
            string verdict = empty > 0.95 ? "VACÍA" : changed > 0.002 ? "EN VIVO" : "CONGELADA";
            return $"{verdict,-9} (cambió {changed:P1}, vacía {empty:P0})";
        }
        finally
        {
            Native.DwmUnregisterThumbnail(thumb);
        }
    }

    private static string Print(nint handle)
    {
        Native.GetWindowRect(handle, out var r);
        int w = Math.Max(1, r.Right - r.Left), h = Math.Max(1, r.Bottom - r.Top);
        using var bmp = new Bitmap(w, h);
        var clock = Stopwatch.StartNew();
        bool ok;
        using (var g = Graphics.FromImage(bmp))
        {
            var dc = g.GetHdc();
            ok = Native.PrintWindow(handle, dc, Native.PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
        }

        long ms = clock.ElapsedMilliseconds;
        double content = Fraction(bmp, c => c.R + c.G + c.B > 24);
        return $"{(ok ? "ok" : "FALLÓ")}, {content:P0} con contenido, {ms} ms, {w}x{h}";
    }

    private static Form Cover(Rectangle bounds, double opacity)
    {
        var cover = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = Rectangle.Inflate(bounds, 20, 20),
            BackColor = Color.FromArgb(60, 60, 70),
            ShowInTaskbar = false,
            Opacity = opacity,
        };
        cover.Show();
        Pump(1500); // Chromium decide la oclusión con algo de retraso
        return cover;
    }

    private static string Cpu(int ms)
    {
        var self = Process.GetCurrentProcess();
        var dwm = Process.GetProcessesByName("dwm").FirstOrDefault();
        TimeSpan? Read(Process? p)
        {
            try
            {
                p?.Refresh();
                return p?.TotalProcessorTime;
            }
            catch (Exception)
            {
                return null;
            }
        }

        var s0 = Read(self);
        var d0 = Read(dwm);
        Pump(ms);
        var s1 = Read(self);
        var d1 = Read(dwm);
        string Pct(TimeSpan? a, TimeSpan? b) => a is null || b is null ? "n/d" : $"{(b.Value - a.Value).TotalMilliseconds / ms / Environment.ProcessorCount:P1}";
        return $"anfitrión {Pct(s0, s1)}, dwm {Pct(d0, d1)} (de toda la CPU)";
    }

    private static Bitmap Capture(Rectangle r)
    {
        var bmp = new Bitmap(r.Width, r.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        return bmp;
    }

    private static double Diff(Bitmap a, Bitmap b)
    {
        int changed = 0, total = 0;
        for (int y = 0; y < a.Height; y += 3)
        {
            for (int x = 0; x < a.Width; x += 3)
            {
                total++;
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    changed++;
                }
            }
        }

        return total == 0 ? 0 : (double)changed / total;
    }

    private static double Fraction(Bitmap bmp, Func<Color, bool> test)
    {
        int hit = 0, total = 0;
        for (int y = 0; y < bmp.Height; y += 4)
        {
            for (int x = 0; x < bmp.Width; x += 4)
            {
                total++;
                if (test(bmp.GetPixel(x, y)))
                {
                    hit++;
                }
            }
        }

        return total == 0 ? 0 : (double)hit / total;
    }

    private static void Pump(int ms)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static nint WaitFor(Func<nint> find, int ms)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            var h = find();
            if (h != 0)
            {
                return h;
            }

            Pump(300);
        }

        return 0;
    }

    private static string Short(string s) => s.Length > 40 ? s[..40] + "…" : s;

    private static void Write(string line)
    {
        Log.AppendLine(line);
    }

    private record struct TopWindow(nint Handle, string Title, string Process);

    private static List<TopWindow> TopWindows()
    {
        var list = new List<TopWindow>();
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h))
            {
                return true;
            }

            var sb = new StringBuilder(256);
            Native.GetWindowText(h, sb, sb.Capacity);
            if (sb.Length == 0)
            {
                return true;
            }

            Native.GetWindowThreadProcessId(h, out uint pid);
            string process;
            try
            {
                process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch (ArgumentException)
            {
                return true;
            }

            list.Add(new TopWindow(h, sb.ToString(), process));
            return true;
        }, 0);
        return list;
    }

    private const string ClockHtml = """
        <!doctype html><html><head><meta charset="utf-8"><title>S10 reloj</title></head>
        <body style="margin:0;font:64px monospace;display:flex;align-items:center;justify-content:center;height:100vh">
        <div id="t"></div>
        <script>
        function tick(){const n=performance.now();document.getElementById('t').textContent=n.toFixed(0);
        document.body.style.background='hsl('+(n/10%360)+',70%,50%)';requestAnimationFrame(tick);}
        tick();
        </script></body></html>
        """;
}

/// <summary>Ventana fuente propia: cambia de color y número diez veces por segundo.</summary>
internal sealed class AnimatedForm : Form
{
    private int _n;

    public AnimatedForm(Rectangle bounds)
    {
        Text = "S10 fuente";
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        DoubleBuffered = true;
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            _n++;
            Invalidate();
        };
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb((_n * 37) % 256, (_n * 91) % 256, (_n * 53) % 256));
        using var font = new Font(FontFamily.GenericMonospace, 48);
        e.Graphics.DrawString(_n.ToString(System.Globalization.CultureInfo.InvariantCulture), font, Brushes.White, 40, 120);
    }
}

internal static class Native
{
    public const int SW_SHOWMINNOACTIVE = 7;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint PW_RENDERFULLCONTENT = 2;
    public const uint WM_CLOSE = 0x0010;

    public delegate bool EnumProc(nint hwnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    public static void Show(nint thumb, Rectangle dest)
    {
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = 0x1 | 0x4 | 0x8, // RECTDESTINATION | OPACITY | VISIBLE
            rcDestination = new RECT { Left = dest.Left, Top = dest.Top, Right = dest.Right, Bottom = dest.Bottom },
            opacity = 255,
            fVisible = true,
        };
        DwmUpdateThumbnailProperties(thumb, ref props);
    }

    [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(nint dest, nint source, out nint thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(nint thumb);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(nint thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("user32.dll")] public static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
