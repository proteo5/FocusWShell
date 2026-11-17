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
            rows.Children.Add(AppRow(app, panel, max));
        }

        ActivityContent.Children.Add(CardRow(rows));
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

    /// <summary>Una app: su nombre y panel, una barra azul (en el foco) sobre una verde (en uso) y los dos tiempos.</summary>
    private static WC.Grid AppRow(ActivityAppTotal app, string panel, long max)
    {
        var row = new WC.Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(150) });

        var name = new WC.StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(Text(app.Name));
        name.Children.Add(Text(panel, "TextFillColorSecondaryBrush", 12));
        row.Children.Add(name);

        var bars = new WC.StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        bars.Children.Add(Bar(app.InFocus.Ticks, max, FocusBlue));
        bars.Children.Add(Bar(app.InUse.Ticks, max, InUseGreen));
        WC.Grid.SetColumn(bars, 1);
        row.Children.Add(bars);

        var times = Text($"{Duration(app.InFocus)} · {Duration(app.InUse)}", "TextFillColorSecondaryBrush", 13);
        times.HorizontalAlignment = HorizontalAlignment.Right;
        times.ToolTip = $"{Strings.Activity_InFocus}: {Duration(app.InFocus)} · {Strings.Activity_InUse}: {Duration(app.InUse)}";
        WC.Grid.SetColumn(times, 2);
        row.Children.Add(times);
        return row;
    }

    /// <summary>Una barra proporcional al máximo del día.</summary>
    private static WC.Grid Bar(long value, long max, Brush color)
    {
        var bar = new WC.Grid { Height = 10, Margin = new Thickness(0, 1, 0, 1) };
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
