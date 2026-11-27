using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusWShell.Core.Activity;
using WC = System.Windows.Controls;

namespace FocusWShell.App;

/// <summary>La sección Actividad (ADR 0019): el tiempo de cada app en el foco y en uso, día por día.</summary>
public partial class MainWindow
{
    /// <summary>El verde de «en uso», junto al azul del foco.</summary>
    private static readonly SolidColorBrush InUseGreen = new(Color.FromRgb(0x1D, 0x9E, 0x75));

    private DateOnly _activityDay = DateOnly.FromDateTime(DateTime.Now);
    private bool _activityHooked;

    /// <summary>Los navegadores con sus sitios desplegados.</summary>
    private readonly HashSet<string> _activityOpenSites = new(StringComparer.OrdinalIgnoreCase);

    private static App Host => (App)Application.Current;

    /// <summary>Muestra la sección: el interruptor, el día elegido y sus cifras.</summary>
    private void BuildActivity()
    {
        if (!_activityHooked && Host.Activity is { } recorder)
        {
            _activityHooked = true;
            recorder.Saved += () => Dispatcher.BeginInvoke(() =>
            {
                if (ActivitySection.Visibility == Visibility.Visible)
                {
                    BuildActivity();
                }
            });
        }

        _loadingGestures = true;
        ActivitySwitch.IsChecked = _settings.ActivityEnabled;
        ActivitySitesSwitch.IsChecked = _settings.ActivitySites;
        ActivitySitesCard.IsEnabled = _settings.ActivityEnabled;
        _loadingGestures = false;
        ActivityPrivacy.Text = Core.Text.F(Strings.Activity_MeasureHelp, _settings.ActivityKeepDays);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var culture = CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.Name); // la fecha en el idioma de la interfaz
        var date = _activityDay.ToString(Strings.Activity_DateFormat, culture);
        ActivityDate.Text = _activityDay == today ? Core.Text.F(Strings.Activity_Today, date)
            : _activityDay == today.AddDays(-1) ? Core.Text.F(Strings.Activity_Yesterday, date)
            : char.ToUpper(date[0], culture) + date[1..];
        ActivityNextButton.IsEnabled = _activityDay < today;

        ActivityContent.Children.Clear();
        var segments = Host.Activity?.Store.Load(_activityDay) ?? [];
        if (segments.Count == 0)
        {
            var empty = _settings.ActivityEnabled || _activityDay != today ? Strings.Activity_Empty : Strings.Activity_Off;
            ActivityContent.Children.Add(Text(empty, "TextFillColorSecondaryBrush"));
            return;
        }

        var day = ActivityDay.From(segments);
        var share = day.Active > TimeSpan.Zero ? (int)Math.Round(100 * day.SideBySide / day.Active) : 0;
        var figures = new WC.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
        figures.Children.Add(Figure(Strings.Activity_Active, Duration(day.Active), null));
        figures.Children.Add(Figure(Strings.Activity_SideBySide, Duration(day.SideBySide), Core.Text.F(Strings.Activity_Share, share)));
        figures.Children.Add(Figure(Strings.Activity_ToFocus, Core.Text.F(Strings.Activity_Times, day.ToFocus), null));
        figures.Children.Add(Figure(Strings.Activity_Longest, Duration(day.LongestFocus), day.LongestFocusApp));
        ActivityContent.Children.Add(figures);

        var legend = new WC.StackPanel { Orientation = WC.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        legend.Children.Add(Swatch(FocusBlue, Strings.Activity_InFocus));
        legend.Children.Add(Swatch(InUseGreen, Strings.Activity_InUse));
        ActivityContent.Children.Add(legend);

        var panels = _controller.Panels().ToDictionary(p => p.Id, p => p.Name);
        var max = day.Apps.Max(a => Math.Max(a.InFocus.Ticks, a.InUse.Ticks));
        var rows = new WC.StackPanel();
        foreach (var app in day.Apps.Where(a => a.InFocus + a.InUse >= TimeSpan.FromMinutes(1)))
        {
            var panel = app.PanelId is { } id && panels.TryGetValue(id, out var name) ? name : Strings.Activity_NoPanel;
            var row = AppRow(app.Name, panel, app.InFocus, app.InUse, max, site: false);
            rows.Children.Add(row);

            // Los sitios del navegador, debajo, con las mismas barras (ADR 0019, sitios). Plegados al
            // principio, para que un navegador con muchos sitios no tape las demás apps.
            var sites = app.Sites.Where(s => s.InFocus + s.InUse >= TimeSpan.FromMinutes(1)).ToList();
            if (sites.Count == 0)
            {
                continue;
            }

            var list = new WC.StackPanel { Visibility = _activityOpenSites.Contains(app.Key) ? Visibility.Visible : Visibility.Collapsed };
            foreach (var site in sites)
            {
                var siteName = site.Site == Core.Browsers.SiteHost.Private ? Strings.Activity_PrivateSite : site.Site;
                list.Children.Add(AppRow(siteName, null, site.InFocus, site.InUse, max, site: true));
            }

            // En la línea del panel, a su derecha: no agrega altura a la fila.
            var nameStack = (WC.StackPanel)row.Children[0];
            var panelText = (WC.TextBlock)nameStack.Children[1];
            nameStack.Children.Remove(panelText);
            var line = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
            panelText.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(panelText);
            line.Children.Add(SitesToggle(app.Key, sites.Count, list));
            nameStack.Children.Add(line);
            rows.Children.Add(list);
        }

        ActivityContent.Children.Add(CardRow(rows));
    }

    /// <summary>
    /// «▸ 7 sitios»: despliega o pliega los sitios de un navegador sin redibujar la sección. Se recuerda
    /// mientras la ventana está abierta, también al cambiar de día.
    /// </summary>
    private Wpf.Ui.Controls.Button SitesToggle(string key, int count, WC.StackPanel list)
    {
        // Un botón pequeño del tema (relleno, borde y estado al pasar el ratón), con contraste en claro y oscuro;
        // el chevron dice si está abierto.
        var text = count == 1 ? Strings.Activity_SitesOne : Core.Text.F(Strings.Activity_SitesCount, count);
        Wpf.Ui.Controls.SymbolIcon Chevron() => new(list.Visibility == Visibility.Visible ? Wpf.Ui.Controls.SymbolRegular.ChevronDown16 : Wpf.Ui.Controls.SymbolRegular.ChevronRight16) { FontSize = 12 };
        var button = new Wpf.Ui.Controls.Button
        {
            Content = text,
            Icon = Chevron(),
            Padding = new Thickness(6, 1, 8, 1),
            MinHeight = 0,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) =>
        {
            bool open = list.Visibility != Visibility.Visible;
            list.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open)
            {
                _activityOpenSites.Add(key);
            }
            else
            {
                _activityOpenSites.Remove(key);
            }

            button.Icon = Chevron();
        };
        return button;
    }

    /// <summary>Una cifra del día: su nombre arriba, el valor grande y, si hay, un detalle.</summary>
    private static WC.Border Figure(string label, string value, string? detail)
    {
        var panel = new WC.StackPanel();
        panel.Children.Add(Text(label, "TextFillColorSecondaryBrush", 12));
        var big = Text(value, size: 22);
        big.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(big);
        if (!string.IsNullOrEmpty(detail))
        {
            panel.Children.Add(Text(detail, "TextFillColorSecondaryBrush", 12));
        }

        var card = CardRow(panel);
        card.Margin = new Thickness(0, 0, 8, 0);
        return card;
    }

    private static WC.StackPanel Swatch(Brush color, string label)
    {
        var panel = new WC.StackPanel { Orientation = WC.Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        panel.Children.Add(new WC.Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = color, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(Text(label, "TextFillColorSecondaryBrush", 12));
        return panel;
    }

    /// <summary>
    /// Una app (o un sitio de un navegador, sangrado y más pequeño, debajo de su navegador): su nombre y
    /// panel, una barra azul (en el foco) sobre una verde (en uso) y los dos tiempos.
    /// </summary>
    private static WC.Grid AppRow(string title, string? panel, TimeSpan inFocus, TimeSpan inUse, long max, bool site)
    {
        var row = new WC.Grid { Margin = new Thickness(0, site ? 1 : 4, 0, site ? 1 : 4) };
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(150) });

        var name = new WC.StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(site ? 16 : 0, 0, 0, 0) };
        var label = site ? Text(title, "TextFillColorSecondaryBrush", 12) : Text(title);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.ToolTip = title;
        name.Children.Add(label);
        if (panel is not null)
        {
            name.Children.Add(Text(panel, "TextFillColorSecondaryBrush", 12));
        }

        row.Children.Add(name);

        var bars = new WC.StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Opacity = site ? 0.75 : 1 };
        bars.Children.Add(Bar(inFocus.Ticks, max, FocusBlue, site ? 6 : 10));
        bars.Children.Add(Bar(inUse.Ticks, max, InUseGreen, site ? 6 : 10));
        WC.Grid.SetColumn(bars, 1);
        row.Children.Add(bars);

        var times = Text($"{Duration(inFocus)} · {Duration(inUse)}", "TextFillColorSecondaryBrush", site ? 12 : 13);
        times.HorizontalAlignment = HorizontalAlignment.Right;
        times.VerticalAlignment = VerticalAlignment.Center;
        times.ToolTip = $"{Strings.Activity_InFocus}: {Duration(inFocus)} · {Strings.Activity_InUse}: {Duration(inUse)}";
        WC.Grid.SetColumn(times, 2);
        row.Children.Add(times);
        return row;
    }

    /// <summary>Una barra proporcional al máximo del día.</summary>
    private static WC.Grid Bar(long value, long max, Brush color, double height)
    {
        var bar = new WC.Grid { Height = height, Margin = new Thickness(0, 1, 0, 1) };
        bar.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(Math.Max(value, 0), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(Math.Max(max - value, 0), GridUnitType.Star) });
        bar.Children.Add(new WC.Border { Background = color, CornerRadius = new CornerRadius(2) });
        return bar;
    }

    /// <summary>«6 h 05», «25 min», «< 1 min» o «0 min».</summary>
    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00}"
        : t > TimeSpan.Zero && t < TimeSpan.FromMinutes(1) ? "< 1 min"
        : $"{(int)t.TotalMinutes} min";

    private void Activity_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingGestures)
        {
            Host.SetActivity(ActivitySwitch.IsChecked == true);
            BuildActivity();
        }
    }

    private void ActivitySites_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingGestures)
        {
            Host.SetActivitySites(ActivitySitesSwitch.IsChecked == true);
            BuildActivity();
        }
    }

    private void ActivityPrev_Click(object sender, RoutedEventArgs e)
    {
        _activityDay = _activityDay.AddDays(-1);
        BuildActivity();
    }

    private void ActivityNext_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _activityDay = _activityDay < today ? _activityDay.AddDays(1) : today;
        BuildActivity();
    }

    private void ActivityDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Host.Activity is not { } recorder
            || System.Windows.MessageBox.Show(this, Strings.Activity_DeleteAsk, "FocusWShell", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            recorder.Flush(); // lo abierto también se va
            recorder.Store.DeleteAll();
            SetStatus(Strings.Activity_Deleted);
        }
        catch (System.IO.IOException ex)
        {
            SetStatus(ex.Message);
        }

        BuildActivity();
    }
}
