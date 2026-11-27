using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;
using FocusWShell.Win32;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FocusWShell.App.DeckWeb;

/// <summary>
/// El deck por la red (ADR 0021): FocusWShell sirve el deck como página web en la red local, por HTTP (opción A,
/// según el spike S13). La página recibe el estado por WebSocket y manda comandos, que hacen lo mismo que los
/// toques del deck de la ventana (<see cref="DeckActions"/>). Sólo responde a direcciones privadas y a
/// dispositivos emparejados con un código de un solo uso.
/// </summary>
/// <remarks>
/// Protocolo v1. Servidor → página: <c>{"type":"state","state":…}</c>, <c>{"type":"pages",…}</c>,
/// <c>{"type":"ack","seq":n,"ok":bool}</c>. Página → servidor: <c>{"cmd":…,"seq":n,…}</c> con <c>tap</c>,
/// <c>move</c>, <c>previous</c>, <c>toPanel</c>, <c>freeAll</c>, <c>pages</c>, <c>selectPage</c> y
/// <c>closePage</c>. Las apps y las páginas viajan con ids opacos que sólo el servidor sabe traducir.
/// </remarks>
public sealed class DeckWebServer : IAsyncDisposable
{
    public const int Port = 8765;
    public const int ProtocolVersion = 1;
    private const string Cookie = "fwsdeck";

    private readonly DeckActions _actions;
    private readonly Func<DeckState> _state;
    private readonly Func<CatalogApp, byte[]?> _icon;
    private readonly Func<Func<Task>, Task> _onUi;
    private readonly Action _freeAll;
    private readonly Func<bool> _pageTitles;
    private readonly ConcurrentDictionary<string, byte[]?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<WebSocket, string> _sockets = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _ids = [];       // identidad de la app → id opaco
    private Dictionary<string, DeckApp> _apps = [];              // id opaco → la app, en el último estado
    private Dictionary<string, (PageWindow Window, BrowserTab Tab)> _pages = [];
    private int _next;
    private WebApplication? _app;

    public DeckWebServer(
        DeckActions actions,
        Func<DeckState> state,
        Func<CatalogApp, byte[]?> icon,
        Func<Func<Task>, Task> onUi,
        Action freeAll,
        Func<bool> pageTitles,
        DeckWebDevices devices)
    {
        _actions = actions;
        _state = state;
        _icon = icon;
        _onUi = onUi;
        _freeAll = freeAll;
        _pageTitles = pageTitles;
        Devices = devices;
        Devices.Changed += DropRevoked;
    }

    public DeckWebDevices Devices { get; }

    /// <summary>Las direcciones del PC en la red local (una por red privada).</summary>
    public IReadOnlyList<string> Urls { get; private set; } = [];

    /// <summary>No escucha porque Windows marca como públicas todas las redes conectadas.</summary>
    public bool PublicOnly { get; private set; }

    /// <summary>Si no se pudo escuchar (puerto ocupado, sin permiso), por qué.</summary>
    public string? Error { get; private set; }

    /// <summary>Conexiones abiertas a la vez por dispositivo (una tablet con dos pestañas, más una que reconecta).</summary>
    private const int MaxSocketsPerDevice = 3;

    /// <summary>Las direcciones del PC en las que escucha: sólo las de redes que Windows marca como privadas o de dominio.</summary>
    private IReadOnlyList<IPAddress> _addresses = [];

    /// <summary>
    /// Escucha sólo en las redes privadas o de dominio de Windows (no en una pública, ni en adaptadores virtuales
    /// que Windows no reconoce como red), y vuelve a elegir cuando cambian las redes del PC.
    /// </summary>
    public async Task StartAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;

        // Marcar la red como privada (o pública) no cambia las direcciones ni avisa: se revisa cada 20 s.
        _recheck?.Dispose();
        _recheck = new Timer(_ => OnNetworkChanged(null, EventArgs.Empty), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        await ListenAsync();
    }

    private async Task ListenAsync()
    {
        await _listenGate.WaitAsync();
        try
        {
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            Error = null;
            var networks = PrivateNetworks.Read();
            _addresses = networks.Addresses;
            PublicOnly = networks.PublicOnly;
            Urls = [.. _addresses.Select(ip => $"http://{ip}:{Port}/")];
            if (_addresses.Count == 0)
            {
                ListeningChanged?.Invoke();
                return; // en una red pública (o sin red) no escucha
            }

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o =>
            {
                foreach (var address in _addresses)
                {
                    o.Listen(address, Port);
                }

                o.Limits.MaxConcurrentConnections = 32;
                o.Limits.MaxRequestBodySize = 4096;
            });
            var app = builder.Build();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
            app.Use(async (context, next) =>
            {
                if (!IsPrivate(context.Connection.RemoteIpAddress) || !IsOwnHost(context) || !IsOwnOrigin(context))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                await next(context);
            });
            app.MapGet("/", (Func<HttpContext, IResult>)Home);
            app.MapGet("/deck.js", () => Asset("deck.js", "text/javascript; charset=utf-8"));
            app.MapGet("/deck.css", () => Asset("deck.css", "text/css; charset=utf-8"));
            app.MapPost("/api/pair", (Func<HttpContext, Task<IResult>>)Pair);
            app.MapGet("/icon", (HttpContext c) => Authorized(c) is null ? Results.StatusCode(403) : Icon(c.Request.Query["key"].ToString()));
            app.Map("/ws", Socket);
            await app.StartAsync();
            _app = app;
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            _listenGate.Release();
        }

        ListeningChanged?.Invoke();
    }

    private readonly SemaphoreSlim _listenGate = new(1);
    private CancellationTokenSource? _networkDebounce;
    private Timer? _recheck;

    /// <summary>Cambió dónde escucha (otra red, o dejó de haber una privada): para la ventana principal.</summary>
    public event Action? ListeningChanged;

    /// <summary>Las redes cambian en ráfaga (wifi que se conecta, VPN): se espera un poco y se vuelve a elegir.</summary>
    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        _networkDebounce?.Cancel();
        var debounce = _networkDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), debounce.Token);
                var now = PrivateNetworks.Read().Addresses;
                if (!now.SequenceEqual(_addresses))
                {
                    await ListenAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>El <c>Host</c> es una dirección del PC en la que escucha: así no responde a un nombre ajeno (DNS rebinding).</summary>
    private bool IsOwnHost(HttpContext context)
    {
        var host = context.Request.Host;
        return host.Port is null or Port && IPAddress.TryParse(host.Host, out var ip) && _addresses.Contains(ip);
    }

    /// <summary>
    /// Lo que cambia algo (emparejar, el WebSocket) sólo desde la propia página: el navegador manda <c>Origin</c>
    /// y tiene que ser el del deck. Así una web abierta en la tablet no puede hablarle al PC.
    /// </summary>
    private static bool IsOwnOrigin(HttpContext context)
    {
        bool changes = HttpMethods.IsPost(context.Request.Method) || context.WebSockets.IsWebSocketRequest;
        if (!changes)
        {
            return true;
        }

        var origin = context.Request.Headers.Origin.ToString();
        return origin.Length > 0 && string.Equals(origin, $"http://{context.Request.Host}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>La dirección para emparejar con un código (la del QR).</summary>
    public string? PairingUrl(string code) => Urls.Count > 0 ? $"{Urls[0]}?code={code}" : null;

    // --- Páginas ----------------------------------------------------------------------------------------

    /// <summary>El deck si el dispositivo está emparejado; si no, la página para emparejarlo.</summary>
    private IResult Home(HttpContext context)
    {
        // Con «?d=token» (la dirección que queda en la pantalla de inicio: en iPad una app de inicio no
        // comparte las cookies de Safari) se recuerda también en una cookie.
        if (context.Request.Query["d"].ToString() is { Length: > 0 } fromUrl && Devices.Find(fromUrl) is not null)
        {
            SetCookie(context, fromUrl);
        }

        return Asset(Authorized(context) is null ? "pair.html" : "index.html", "text/html; charset=utf-8");
    }

    /// <summary>Intentos fallidos de emparejar por dirección, y hasta cuándo esa dirección no puede intentarlo.</summary>
    private readonly ConcurrentDictionary<IPAddress, (int Failures, DateTime Until)> _pairFailures = new();

    private async Task<IResult> Pair(HttpContext context)
    {
        // Además del límite por código (DeckWebDevices.MaxFailures), un freno por dirección: tras 5 fallos,
        // 10 minutos sin poder intentarlo, y mientras tanto cada intento tarda un poco.
        var ip = context.Connection.RemoteIpAddress ?? IPAddress.None;
        if (_pairFailures.TryGetValue(ip, out var f) && f.Failures >= 5 && f.Until > DateTime.UtcNow)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        await Task.Delay(300);
        try
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body);
            var code = doc.RootElement.GetProperty("code").GetString();
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (Devices.Pair(code, name) is not { } token)
            {
                _pairFailures.AddOrUpdate(ip, (1, DateTime.UtcNow.AddMinutes(10)), (_, old) => (old.Until > DateTime.UtcNow ? old.Failures + 1 : 1, DateTime.UtcNow.AddMinutes(10)));
                return Results.Json(new { ok = false });
            }

            _pairFailures.TryRemove(ip, out _);

            SetCookie(context, token);
            return Results.Json(new { ok = true, token });
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Results.BadRequest();
        }
    }

    private static void SetCookie(HttpContext context, string token) =>
        context.Response.Cookies.Append(Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromDays(3650),
            Path = "/",
        });

    /// <summary>El dispositivo de la petición (por su cookie o por «?d=»), o <c>null</c>.</summary>
    private DeckWebDevice? Authorized(HttpContext context) =>
        Devices.Find(context.Request.Cookies[Cookie]) ?? Devices.Find(context.Request.Query["d"].ToString());

    private static IResult Asset(string name, string type)
    {
        using var stream = typeof(DeckWebServer).Assembly.GetManifestResourceStream($"FocusWShell.App.DeckWeb.{name}");
        if (stream is null)
        {
            return Results.NotFound();
        }

        using var reader = new StreamReader(stream);
        return Results.Content(reader.ReadToEnd(), type);
    }

    private IResult Icon(string key)
    {
        var bytes = _icons.GetOrAdd(key, k =>
        {
            var app = AllApps(_state()).FirstOrDefault(a => string.Equals(a.AppKey, k, StringComparison.OrdinalIgnoreCase))?.App;
            return app is null ? null : _icon(app);
        });
        return bytes is null ? Results.NotFound() : Results.Bytes(bytes, "image/png");
    }

    // --- WebSocket -------------------------------------------------------------------------------------

    private async Task Socket(HttpContext context)
    {
        if (Authorized(context) is not { } device)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Pocas conexiones por dispositivo: uno emparejado no puede saturar el PC.
        if (_sockets.Values.Count(id => id == device.Id) >= MaxSocketsPerDevice)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        _sockets[socket] = device.Id;
        var lastPages = DateTime.MinValue;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var send = new SemaphoreSlim(1);
        string? last = null;

        async Task Send(object message)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            await send.WaitAsync(cancel.Token);
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancel.Token);
            }
            finally
            {
                send.Release();
            }
        }

        async Task PushIfChanged()
        {
            var dto = Dto(_state(), device);
            var json = JsonSerializer.Serialize(dto);
            if (json != last)
            {
                last = json;
                await Send(new { type = "state", state = dto });
            }
        }

        // El estado al conectar y, cada medio segundo, si cambió: el PC también cambia sin la tablet.
        var pusher = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await PushIfChanged();
                await Task.Delay(500, cancel.Token);
            }
        }, cancel.Token);

        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, cancel.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (!Devices.All.Any(d => d.Id == device.Id))
                {
                    break; // revocado mientras estaba conectado
                }

                using var doc = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                var root = doc.RootElement;
                int seq = root.TryGetProperty("seq", out var s) ? s.GetInt32() : 0;
                // Leer las páginas cuesta (UI Automation en cada navegador): como mucho cada 2 s por conexión.
                if (root.TryGetProperty("cmd", out var c) && c.GetString() == "pages")
                {
                    if (DateTime.UtcNow - lastPages < TimeSpan.FromSeconds(2))
                    {
                        continue;
                    }

                    lastPages = DateTime.UtcNow;
                }

                var reply = await Handle(root);
                await Send(reply ?? new { type = "ack", seq, ok = true });
                await PushIfChanged();
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
        }
        finally
        {
            _sockets.TryRemove(socket, out _);
            await cancel.CancelAsync();
            try
            {
                await pusher;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>Un comando de la página. Devuelve una respuesta propia o <c>null</c> (un «ack»).</summary>
    private async Task<object?> Handle(JsonElement root)
    {
        string cmd = root.GetProperty("cmd").GetString() ?? "";
        DeckApp? App() => root.TryGetProperty("id", out var id) ? Lookup(id.GetString()) : null;
        switch (cmd)
        {
            case "tap" when App() is { } app:
                await Tap(app);
                break;
            case "move" when App() is { Handle: { } h } app:
                var panel = root.GetProperty("panel").GetString();
                if (panel == "free")
                {
                    if (app.PanelId is not null)
                    {
                        await Run(new Release(h));
                    }
                }
                else if (panel is not null && panel != app.PanelId && _state().Panels.Any(p => p.Id == panel))
                {
                    await Run(app.PanelId is null ? new Adopt(h, app.Name, panel) : new SendTo(h, panel));
                }

                break;
            case "pin" when App() is { App: { } catalog, PanelId: not null, State: not ListItemState.Unmovable } app:
                // Como el modo Anclar del deck: alterna; una anclada se queda en su panel aunque se cierre.
                await _onUi(() =>
                {
                    _actions.SetPinned(app.AppKey, !catalog.Pinned);
                    return Task.CompletedTask;
                });
                break;
            case "presentation" when Panel(root) is { } target
                && Enum.TryParse<PanelTemplate>(root.GetProperty("template").GetString(), out var template)
                && (template != PanelTemplate.Fill || target.Display is not null):
                await Run(new SetPresentation(target.Id, template));
                break;
            case "tabPrev" or "tabNext" when Panel(root) is { } target && Tabs(target) is { Count: > 1 } tabs:
                // En pestañas, la visible es la primera: ▶ pone la segunda; ◀, la última (como el deck del PC).
                await Run(new MakeActiveInPanel((cmd == "tabNext" ? tabs[1] : tabs[^1]).Handle!.Value));
                break;
            case "view" when Layout(_state()).FirstOrDefault(b => b.Key == root.GetProperty("key").GetString()) is { } button:
                await Run(new SetView(button.Next));
                break;
            case "resetWidth":
                await Run(new SetProportions(_state().View, null));
                break;
            case "minimize" when App() is { Handle: { } h, PanelId: not null, State: not ListItemState.Unmovable } app:
                await Run(app.State == ListItemState.Parked ? new Restore(h) : new Park(h));
                break;
            case "close" when App() is { Handle: { } h, State: not ListItemState.Unmovable }:
                await _onUi(() => _actions.CloseApp(h));
                break;
            case "keepAwake":
                await _onUi(() =>
                {
                    _actions.ToggleKeepAwake();
                    return Task.CompletedTask;
                });
                break;
            case "mouseToFocus":
                await _onUi(() => _actions.MouseToFocus());
                break;
            case "rearrange" when _actions.CanRearrange():
                await _onUi(() => _actions.RearrangeAll());
                break;
            case "previous":
                await Run(new SwapWithPrevious());
                break;
            case "toPanel" when _state().Focus is { Handle: { } focus }:
                await Run(new ReturnToPanel(focus));
                break;
            case "freeAll":
                await _onUi(() =>
                {
                    _freeAll();
                    return Task.CompletedTask;
                });
                break;
            case "pages" when _pageTitles() && _actions.ReadPages is { } read:
                IReadOnlyList<PageWindow> windows = [];
                await _onUi(async () => windows = await read());
                return new { type = "pages", pages = PagesDto(windows) };
            case "selectPage" when _pageTitles() && Page(root) is { } page && _actions.SelectPage is { } select:
                await _onUi(async () =>
                {
                    if (page.Window.Managed && !page.Window.InFocus && !page.Window.OtherDesktop)
                    {
                        await _actions.Run([new Promote(page.Window.Handle)]);
                    }
                    else
                    {
                        await _actions.BringToFront(page.Window.Handle);
                    }

                    await select(page.Window.Handle, page.Tab);
                });
                break;
            case "closePage" when _pageTitles() && Page(root) is { } page && _actions.ClosePage is { } close:
                await _onUi(() => close(page.Window.Handle, page.Tab));
                break;
        }

        return null;
    }

    /// <summary>
    /// Tocar una app, como en el deck de la ventana (<c>DeckWindow.Choose</c>): cerrada o lejos, se abre en su
    /// panel; en el foco o libre, al frente; una pestaña oculta, se muestra en su panel; si no, al foco.
    /// </summary>
    private async Task Tap(DeckApp app)
    {
        if (app.State == ListItemState.Unmovable && app.Handle is { } elevated)
        {
            await _onUi(() => _actions.BringToFront(elevated));
            return;
        }

        if (app.State is ListItemState.Closed or ListItemState.InTray or ListItemState.OtherDesktop)
        {
            if (app.PanelId is { } panel)
            {
                await _onUi(() => _actions.OpenApp(app.AppKey, panel));
            }

            return;
        }

        if (app.Handle is not { } h)
        {
            return;
        }

        if (app.State == ListItemState.InFocus || app.PanelId is null)
        {
            await _onUi(() => _actions.BringToFront(h));
            return;
        }

        if (TabOf(app, _state()) == "hidden")
        {
            await Run(new MakeActiveInPanel(h));
            await _onUi(() => _actions.BringToFront(h));
            return;
        }

        await Run(new Promote(h));
    }

    private Task Run(params Command[] commands) => _onUi(() => _actions.Run(commands));

    /// <summary>El panel que nombra el comando, en el estado de ahora.</summary>
    private DeckPanel? Panel(JsonElement root) =>
        root.TryGetProperty("panel", out var id) && id.GetString() is { } key ? _state().Panels.FirstOrDefault(p => p.Id == key) : null;

    /// <summary>Las pestañas de un panel en pestañas o Llenar: sus apps en el panel, la visible primero.</summary>
    private static List<DeckApp> Tabs(DeckPanel panel) =>
        panel.Template is PanelTemplate.Stack or PanelTemplate.Fill
            ? [.. panel.Apps.Where(a => a.State == ListItemState.InPanel && a.Handle is not null)]
            : [];

    /// <summary>Un botón de la disposición del principal: su clave, su texto, si está prendido y la vista a la que lleva.</summary>
    private sealed record LayoutButton(string Key, string Label, bool On, FocusPosition Next);

    /// <summary>
    /// La disposición del principal, como en el deck del PC (<c>DeckWindow.ArrangementButtons</c>): prender y
    /// apagar cada columna y, en un ultrawide, traer una al otro lado.
    /// </summary>
    private static List<LayoutButton> Layout(DeckState s)
    {
        string Column(PanelSide side, string fallback) =>
            s.Panels.Where(p => p.Display is null && p.Side == side).OrderBy(p => p.Slot).FirstOrDefault()?.Name ?? fallback;
        var view = s.View;
        string left = Column(PanelSide.Left, Strings.Deck_LeftColumn), right = Column(PanelSide.Right, Strings.Deck_RightColumn);
        var buttons = new List<LayoutButton>();
        if (MainArrangement.CanBorrowToLeft(view, s.Ultrawide))
        {
            buttons.Add(new("borrowLeft", "⇠ " + right, MainArrangement.RightBorrowedLeft(view), MainArrangement.ToggleBorrowToLeft(view)));
        }

        buttons.Add(new("left", "◧ " + left, MainArrangement.LeftOn(view), MainArrangement.ToggleLeft(view)));
        buttons.Add(new("right", right + " ◨", MainArrangement.RightOn(view), MainArrangement.ToggleRight(view)));
        if (MainArrangement.CanBorrowToRight(view, s.Ultrawide))
        {
            buttons.Add(new("borrowRight", left + " ⇢", MainArrangement.LeftBorrowedRight(view), MainArrangement.ToggleBorrowToRight(view)));
        }

        return buttons;
    }

    /// <summary>En un panel en pestañas (o Llenar) con varias: «visible» la que se ve, «hidden» las demás.</summary>
    private static string? TabOf(DeckApp app, DeckState state)
    {
        if (app.Handle is not { } h || app.State != ListItemState.InPanel
            || state.Panels.FirstOrDefault(p => p.Id == app.PanelId) is not { Template: PanelTemplate.Stack or PanelTemplate.Fill } panel)
        {
            return null;
        }

        var tabs = panel.Apps.Where(a => a.State == ListItemState.InPanel && a.Handle is not null).ToList();
        return tabs.Count > 1 ? (tabs[0].Handle == h ? "visible" : "hidden") : null;
    }

    // --- Estado ----------------------------------------------------------------------------------------

    /// <summary>El estado para la página. Cada app con un id opaco, estable mientras corre el servidor.</summary>
    private object Dto(DeckState s, DeckWebDevice device)
    {
        lock (_gate)
        {
            var apps = new Dictionary<string, DeckApp>();
            string Id(DeckApp a)
            {
                var identity = a.Handle is { } h ? "w" + h : $"c{a.AppKey}|{a.PanelId}";
                if (!_ids.TryGetValue(identity, out var id))
                {
                    id = "a" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    _ids[identity] = id;
                }

                apps[id] = a;
                return id;
            }

            object App(DeckApp a) => new
            {
                id = Id(a),
                name = a.Name,
                key = a.AppKey,
                state = a.State.ToString(),
                tab = TabOf(a, s),
                pinned = a.App?.Pinned == true,
                canPin = a.App is not null && a.PanelId is not null && a.State != ListItemState.Unmovable,
                panel = a.PanelId,
            };

            var known = s.Displays.Select(d => d.Display ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var displays = s.Displays.Select(d => (Key: d.Display ?? "", d.Name, Connected: true))
                .Concat(s.Panels.Where(p => !known.Contains(p.Display ?? "")).Select(p => (Key: p.Display ?? "", Name: p.Where.Split(',')[0], Connected: false)))
                .DistinctBy(d => d.Key, StringComparer.OrdinalIgnoreCase);
            var monitors = displays
                .Select(d => new
                {
                    name = d.Name,
                    connected = d.Connected,
                    panels = s.Panels.Where(p => string.Equals(p.Display ?? "", d.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(p => new
                        {
                            id = p.Id,
                            name = p.Name,
                            side = p.Side.ToString(),
                            template = p.Template.ToString(),
                            canFill = p.Display is not null,
                            tabs = Tabs(p).Count,
                            off = p.Off,
                            apps = p.Apps.Select(App).ToList(),
                        }).ToList(),
                })
                .Where(m => m.panels.Count > 0)
                .ToList();
            var focus = s.Focus is { } f ? App(f) : null;
            var free = s.Free.Select(App).ToList();
            _apps = apps;
            return new
            {
                v = ProtocolVersion,
                device = device.Name,
                focus,
                hasPrevious = s.HasPrevious,
                foreground = s.Foreground is { } fg ? apps.FirstOrDefault(x => x.Value.Handle == fg).Key : null,
                monitors,
                free,
                pages = _pageTitles() && s.Browsers.Count > 0,
                layout = Layout(s).Select(b => new { key = b.Key, label = b.Label, on = b.On }).ToList(),
                keepAwake = _actions.KeepAwake(),
                canRearrange = _actions.CanRearrange(),
            };
        }
    }

    private DeckApp? Lookup(string? id)
    {
        lock (_gate)
        {
            return id is not null && _apps.TryGetValue(id, out var app) ? app : null;
        }
    }

    private static IEnumerable<DeckApp> AllApps(DeckState s) =>
        s.Panels.SelectMany(p => p.Apps).Concat(s.Free).Concat(s.Focus is { } f ? [f] : []);

    /// <summary>Las páginas agrupadas por sitio, como en el deck; las ventanas privadas, sin títulos.</summary>
    private object PagesDto(IReadOnlyList<PageWindow> windows)
    {
        lock (_gate)
        {
            _pages = [];
            int n = 0;
            var visible = windows.Where(w => !w.Private).SelectMany(w => w.Tabs.Select(t => (Window: w, Tab: t))).ToList();
            var sites = Core.Browsers.PageSites.Assign([.. visible.Select(x => x.Tab.Title)]);
            var list = visible.Select((x, i) =>
            {
                var id = "p" + (++n).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _pages[id] = (x.Window, x.Tab);
                return new { id, title = x.Tab.Title, site = sites[i], where = $"{x.Window.Browser} · {x.Window.Where}", active = x.Tab.Active };
            }).ToList();
            var privateWindows = windows.Where(w => w.Private).Select(w =>
            {
                var id = "p" + (++n).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _pages[id] = (w, w.Tabs.FirstOrDefault(t => t.Active) ?? w.Tabs[0]);
                return new { id, tabs = w.Tabs.Count, where = $"{w.Browser} · {w.Where}" };
            }).ToList();
            return new { list, privateWindows };
        }
    }

    private (PageWindow Window, BrowserTab Tab)? Page(JsonElement root)
    {
        lock (_gate)
        {
            return root.TryGetProperty("id", out var id) && id.GetString() is { } key && _pages.TryGetValue(key, out var page) ? page : null;
        }
    }

    // --- Red -------------------------------------------------------------------------------------------

    /// <summary>Al revocar un dispositivo, sus conexiones se cierran.</summary>
    private void DropRevoked()
    {
        var alive = Devices.All.Select(d => d.Id).ToHashSet();
        foreach (var (socket, id) in _sockets)
        {
            if (!alive.Contains(id))
            {
                socket.Abort();
                _sockets.TryRemove(socket, out _);
            }
        }
    }

    internal static bool IsPrivate(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }

    public async ValueTask DisposeAsync()
    {
        Devices.Changed -= DropRevoked;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _networkDebounce?.Cancel();
        _recheck?.Dispose();
        foreach (var socket in _sockets.Keys)
        {
            socket.Abort();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
