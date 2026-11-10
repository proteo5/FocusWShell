using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusWShell.TouchSpike;

/// <summary>
/// Spike S8 (ADR 0013, R12): una ventana tipo deck en WPF, sin foco (<c>WS_EX_NOACTIVATE</c>), con botones.
/// <list type="bullet">
/// <item><c>auto</c> (por defecto): abre una Paint como "app del Centro", la deja en primer plano y toca los
/// botones 50 veces con toques inyectados (<c>InjectTouchInput</c>) y 20 con clics de ratón. En cada uno mira si
/// el primer plano y el foco de teclado siguen en Paint, si el botón se enteró y cuánto tardó.</item>
/// <item><c>manual</c>: deja la ventana abierta en el monitor indicado y registra los toques de verdad (tablet
/// por streaming o monitor táctil), con el tipo de entrada y si el foco se movió.</item>
/// </list>
/// Escribe <c>s8-resultados.txt</c> junto al ejecutable.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var deck = new DeckWindow();
        deck.Show();
        if (args is ["manual", ..])
        {
            // manual [x y ancho alto]: en píxeles físicos, p. ej. la pantalla de la tablet.
            deck.Manual = true;
            deck.Closed += (_, _) => app.Shutdown();
            if (args.Length >= 5 && args[1..5].All(a => int.TryParse(a, out _)))
            {
                var r = args[1..5].Select(int.Parse).ToArray();
                Native.SetWindowPos(deck.Handle, -1, r[0], r[1], r[2], r[3], 0x0010); // HWND_TOPMOST, SWP_NOACTIVATE
            }
        }
        else
        {
            deck.Dispatcher.BeginInvoke(async () =>
            {
                await Auto.Run(deck);
                deck.Close();
                app.Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        app.Run();
    }
}

/// <summary>Ventana tipo deck: botones grandes, sin foco y fuera de la barra de tareas.</summary>
internal sealed class DeckWindow : Window
{
    public readonly Stopwatch Clock = Stopwatch.StartNew();
    public readonly List<(string Button, string Kind, long Ms, nint Foreground)> Presses = [];
    public readonly List<Button> Buttons = [];
    public bool Manual;
    public nint Handle;
    private readonly StringBuilder _manualLog = new();

    public DeckWindow()
    {
        Title = "S8 deck";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Width = 520;
        Height = 160;
        Left = 40;
        Top = 40;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x2B));
        var panel = new WrapPanel { Margin = new Thickness(8) };
        var note = new TextBlock
        {
            Text = "S8: toca los botones mientras escribes en una app del PC. Cada toque se anota en s8-manual.txt.",
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(6),
            TextWrapping = TextWrapping.Wrap,
            Width = 480,
        };
        panel.Children.Add(note);
        foreach (var name in new[] { "Slack", "Edge", "VS Code", "Teams", "Anterior", "Vista" })
        {
            var b = new Button { Content = name, Width = 150, Height = 60, Margin = new Thickness(6), Focusable = false, FontSize = 18 };
            b.Click += (_, _) => Record(name, "Click");
            b.PreviewTouchDown += (_, _) => Record(name, "TouchDown");
            b.PreviewStylusDown += (_, e) => Record(name, e.StylusDevice.TabletDevice?.Type == TabletDeviceType.Touch ? "StylusDown(touch)" : "StylusDown(pen)");
            Buttons.Add(b);
            panel.Children.Add(b);
        }

        Content = panel;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, (ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW);
        };
    }

    private void Record(string button, string kind)
    {
        var fg = Native.GetForegroundWindow();
        Presses.Add((button, kind, Clock.ElapsedMilliseconds, fg));
        if (Manual)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {button} {kind} · primer plano {(fg == Handle ? "¡EL DECK!" : Native.Title(fg))}";
            _manualLog.AppendLine(line);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "s8-manual.txt"), _manualLog.ToString());
        }
    }
}

/// <summary>La prueba automática.</summary>
internal static class Auto
{
    public static async Task Run(DeckWindow deck)
    {
        var log = new StringBuilder();
        void Write(string s) => log.AppendLine(s);
        Write($"S8 · {DateTime.Now:yyyy-MM-dd HH:mm}");

        // "App del Centro": una Paint propia, en primer plano.
        var before = Native.TopWindows();
        Process.Start(new ProcessStartInfo("mspaint") { UseShellExecute = true });
        nint paint = 0;
        for (int i = 0; i < 40 && paint == 0; i++)
        {
            await Task.Delay(300);
            paint = Native.TopWindows().FirstOrDefault(h => !before.Contains(h) && Native.Title(h).EndsWith("Paint", StringComparison.Ordinal));
        }

        if (paint == 0)
        {
            Write("No se encontró la ventana de Paint.");
            Save(log);
            return;
        }

        await Native.ForceForeground(paint);
        uint paintThread = Native.GetWindowThreadProcessId(paint, out _);
        nint focusBefore = Native.FocusOf(paintThread);
        Write($"App del Centro: Paint · primer plano={(Native.GetForegroundWindow() == paint)} · foco de teclado {focusBefore:X}");
        Write($"Deck: WS_EX_NOACTIVATE · Topmost · DPI {VisualTreeHelper.GetDpi(deck).PixelsPerDip:0.##}");
        Write("");

        Write($"Toques inyectados (InjectTouchInput): init {(Native.InitializeTouchInjection(1, Native.TOUCH_FEEDBACK_NONE) ? "ok" : $"FALLÓ {Marshal.GetLastWin32Error()}")}");
        await Measure(deck, paint, paintThread, focusBefore, 50, touch: true, Write);
        Write("");
        Write("Clics de ratón (SendInput)");
        await Measure(deck, paint, paintThread, focusBefore, 20, touch: false, Write);

        Native.PostMessage(paint, Native.WM_CLOSE, 0, 0);
        Save(log);
    }

    private static async Task Measure(DeckWindow deck, nint paint, uint paintThread, nint focusBefore, int count, bool touch, Action<string> write)
    {
        int registered = 0, stolen = 0, focusLost = 0;
        var latency = new List<long>();
        var kinds = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            var button = deck.Buttons[i % deck.Buttons.Count];
            var center = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            int x = (int)center.X, y = (int)center.Y;
            int seen = deck.Presses.Count;
            long t0 = deck.Clock.ElapsedMilliseconds;
            if (touch)
            {
                Native.Touch(x, y, down: true);
                await Task.Delay(30);
                t0 = deck.Clock.ElapsedMilliseconds;
                Native.Touch(x, y, down: false);
            }
            else
            {
                Native.Click(x, y);
            }

            // Espera a que llegue el Click (en un botón WPF, al soltar).
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 1000 && !deck.Presses.Skip(seen).Any(p => p.Kind == "Click"))
            {
                await Task.Delay(2);
            }

            var click = deck.Presses.Skip(seen).FirstOrDefault(p => p.Kind == "Click");
            foreach (var p in deck.Presses.Skip(seen))
            {
                kinds.Add(p.Kind);
            }

            if (click.Button is not null)
            {
                registered++;
                latency.Add(click.Ms - t0);
            }

            await Task.Delay(150);
            if (Native.GetForegroundWindow() != paint)
            {
                stolen++;
                await Native.ForceForeground(paint);
            }
            else if (Native.FocusOf(paintThread) != focusBefore)
            {
                focusLost++;
            }
        }

        latency.Sort();
        write($"   pulsaciones registradas: {registered} de {count} · eventos vistos: {string.Join(", ", kinds)}");
        write($"   el primer plano dejó Paint: {stolen} veces · Paint perdió el foco de teclado: {focusLost} veces");
        if (latency.Count > 0)
        {
            write($"   latencia hasta el Click (desde soltar): mediana {latency[latency.Count / 2]} ms · p95 {latency[Math.Max(0, (int)(latency.Count * 0.95) - 1)]} ms · máx {latency[^1]} ms");
        }
    }

    private static void Save(StringBuilder log) =>
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "s8-resultados.txt"), log.ToString());
}

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_APPWINDOW = 0x40000;
    public const uint WM_CLOSE = 0x0010;
    public const uint TOUCH_FEEDBACK_NONE = 3;

    private const uint PT_TOUCH = 2;
    private const uint POINTER_FLAG_INRANGE = 0x2, POINTER_FLAG_INCONTACT = 0x4, POINTER_FLAG_DOWN = 0x10000, POINTER_FLAG_UP = 0x40000;
    private const uint TOUCH_MASK_CONTACTAREA = 0x1, TOUCH_MASK_PRESSURE = 0x4;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public uint pointerType, pointerId, frameId, pointerFlags;
        public nint sourceDevice, hwndTarget;
        public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
        public uint dwTime, historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags, touchMask;
        public RECT rcContact, rcContactRaw;
        public uint orientation, pressure;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }

    public static void Touch(int x, int y, bool down)
    {
        var info = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = PT_TOUCH,
                pointerId = 0,
                ptPixelLocation = new POINT { X = x, Y = y },
                pointerFlags = down ? POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT : POINTER_FLAG_UP,
            },
            touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_PRESSURE,
            rcContact = new RECT { Left = x - 2, Top = y - 2, Right = x + 2, Bottom = y + 2 },
            pressure = 512,
        };
        InjectTouchInput(1, [info]);
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        var down = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0002 } };
        var up = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0004 } };
        SendInput(1, [down], Marshal.SizeOf<INPUT>());
        Thread.Sleep(30);
        SendInput(1, [up], Marshal.SizeOf<INPUT>());
    }

    /// <summary>Pone la ventana en primer plano (una pulsación de Alt desbloquea SetForegroundWindow).</summary>
    public static async Task ForceForeground(nint h)
    {
        keybd_event(0x12, 0, 0, 0);
        keybd_event(0x12, 0, 2, 0);
        await Task.Delay(200);
        SetForegroundWindow(h);
        await Task.Delay(400);
    }

    public static nint FocusOf(uint thread)
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : 0;
    }

    public static string Title(nint h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        return sb.ToString();
    }

    public static HashSet<nint> TopWindows()
    {
        var set = new HashSet<nint>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h))
            {
                set.Add(h);
            }

            return true;
        }, 0);
        return set;
    }

    public delegate bool EnumProc(nint h, nint l);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool InitializeTouchInjection(uint maxCount, uint mode);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool InjectTouchInput(uint count, POINTER_TOUCH_INFO[] contacts);
    [DllImport("user32.dll")] public static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(nint h, int index, int value);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, nint l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint h, StringBuilder s, int n);
}
