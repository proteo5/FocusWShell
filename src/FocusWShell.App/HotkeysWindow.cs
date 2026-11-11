using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusWShell.Core.Input;

namespace FocusWShell.App;

/// <summary>
/// Ver y cambiar los atajos globales (M1). Se pulsa la combinación nueva sobre cada acción. Mientras está
/// abierta, los atajos dejan de escucharse: si no, Windows se quedaría las teclas y no llegarían aquí.
/// </summary>
public sealed class HotkeysWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, string> _pending;
    private readonly Dictionary<string, TextBox> _boxes = [];
    private readonly TextBlock _problems = new() { Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public HotkeysWindow(AppSettings settings)
    {
        _settings = settings;
        _pending = new Dictionary<string, string>(settings.Hotkeys);
        Title = Strings.Hk_WindowTitle;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/FocusWShell.ico"));
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = Strings.Hk_Help,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        foreach (var action in Hotkeys.Actions)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = action.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            var box = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 3, 8, 3), Padding = new Thickness(4, 2, 4, 2), IsReadOnlyCaretVisible = false };
            System.Windows.Automation.AutomationProperties.SetName(box, Core.Text.F(Strings.Hk_BoxName, action.Name));
            box.PreviewKeyDown += (_, e) => Capture(action, e);
            var reset = new Button { Content = Strings.Hk_Default, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 3, 0, 3) };
            System.Windows.Automation.AutomationProperties.SetName(reset, Core.Text.F(Strings.Hk_DefaultName, action.Name));
            reset.Click += (_, _) =>
            {
                _pending.Remove(action.Id);
                Render();
            };
            Grid.SetRow(label, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            Grid.SetRow(reset, row);
            Grid.SetColumn(reset, 2);
            grid.Children.Add(label);
            grid.Children.Add(box);
            grid.Children.Add(reset);
            _boxes[action.Id] = box;
            row++;
        }

        root.Children.Add(grid);
        root.Children.Add(_problems);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = Strings.Hk_Save, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Strings.Hk_Cancel, Padding = new Thickness(14, 3, 14, 3), IsCancel = true };
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) => ((App)Application.Current).PauseHotkeys();
        Closed += (_, _) =>
        {
            // Al cerrar (guardando o no), los atajos vuelven a escucharse con la configuración guardada.
            if (!_saved)
            {
                ((App)Application.Current).StartHotkeys(announce: false);
            }
        };
        Render();
    }

    private bool _saved;

    /// <summary>Muestra los atajos que quedarían y los problemas (duplicados, no válidos).</summary>
    private void Render()
    {
        var map = Hotkeys.Resolve(_pending);
        foreach (var action in Hotkeys.Actions)
        {
            var box = _boxes[action.Id];
            box.Text = map.Chords.TryGetValue(action.Id, out var chord) ? chord.Display() : Strings.Hk_None;
            box.FontWeight = _pending.ContainsKey(action.Id) ? FontWeights.SemiBold : FontWeights.Normal;
        }

        _problems.Text = string.Join(Environment.NewLine, map.Problems);
    }

    private void Capture(HotkeyAction action, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
        {
            return; // falta la tecla; Tab sigue sirviendo para moverse
        }

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            mods |= HotkeyModifiers.Ctrl;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            mods |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            mods |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            mods |= HotkeyModifiers.Win;
        }

        var chord = new KeyChord(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!chord.IsValid)
        {
            _problems.Text = Core.Text.F(Strings.Hk_Invalid, chord.Display(), action.Name);
            return;
        }

        if (chord == action.Default)
        {
            _pending.Remove(action.Id);
        }
        else
        {
            _pending[action.Id] = chord.ToString();
        }

        Render();
    }

    private void Save()
    {
        _settings.Hotkeys = new Dictionary<string, string>(_pending);
        _settings.Save();
        _saved = true;
        var warnings = ((App)Application.Current).StartHotkeys(announce: false);
        if (warnings.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, warnings), Strings.Hk_WindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Close();
    }
}
