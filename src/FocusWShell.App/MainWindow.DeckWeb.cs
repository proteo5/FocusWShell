using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using FocusWShell.App.DeckWeb;
using WC = System.Windows.Controls;

namespace FocusWShell.App;

/// <summary>La sección Deck en la red (ADR 0021): prenderlo, emparejar con un QR y revocar dispositivos.</summary>
public partial class MainWindow
{
    private bool _webHooked;
    private System.Windows.Threading.DispatcherTimer? _webCodeTimer;

    /// <summary>La promesa de privacidad de General: con el deck en la red prendido, ya no es «no se conecta a la red».</summary>
    private void ShowPrivacy() =>
        AboutPrivacy.Text = _settings.DeckWebEnabled ? Strings.Gen_AboutPrivacyWeb : Strings.Gen_AboutPrivacy;

    private void BuildDeckWeb()
    {
        ShowPrivacy();
        _loadingGestures = true;
        WebSwitch.IsChecked = _settings.DeckWebEnabled;
        WebPagesSwitch.IsChecked = _settings.DeckWebPageTitles;
        _loadingGestures = false;

        var server = Host.DeckWeb;
        WebAddress.Text = !_settings.DeckWebEnabled ? ""
            : server is null ? Strings.Web_Starting
            : server.Error is { } error ? Core.Text.F(Strings.Web_Error, error)
            : server.PublicOnly ? Strings.Web_PublicNetwork
            : server.Urls.Count == 0 ? Strings.Web_NoNetwork
            : string.Join(Environment.NewLine, server.Urls);
        WebNetworkButton.Visibility = server is { PublicOnly: true } && _settings.DeckWebEnabled ? Visibility.Visible : Visibility.Collapsed;
        WebPairButton.IsEnabled = server is { Error: null, Urls.Count: > 0 };
        if (server is null || server.Urls.Count == 0)
        {
            WebPairing.Visibility = Visibility.Collapsed;
        }

        if (!_webHooked && server is not null)
        {
            _webHooked = true;
            void Rebuild() => Dispatcher.BeginInvoke(() =>
            {
                if (DeckWebSection.Visibility == Visibility.Visible)
                {
                    BuildDeckWeb();
                }
            });
            server.Devices.Changed += () => Dispatcher.BeginInvoke(() =>
            {
                // Al emparejarse uno, el código ya se gastó: se esconde.
                WebPairing.Visibility = Visibility.Collapsed;
                Rebuild();
            });

            // Otra red (o la misma, que pasó a privada o a pública): otras direcciones, o ninguna.
            server.ListeningChanged += Rebuild;

            // Cinco intentos fallidos anulan el código: se esconde y se dice por qué.
            server.Devices.CodeBurned += () => Dispatcher.BeginInvoke(() =>
            {
                WebPairing.Visibility = Visibility.Collapsed;
                SetStatus(Strings.Web_CodeBurned);
            });
        }

        WebDevices.Children.Clear();
        var devices = server?.Devices.All ?? new DeckWebDevices(DeckWebDevices.DefaultPath).All;
        if (devices.Count == 0)
        {
            WebDevices.Children.Add(Text(Strings.Web_NoDevices, "TextFillColorSecondaryBrush"));
        }

        var culture = CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.Name);
        foreach (var device in devices)
        {
            var row = new WC.DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var revoke = new Wpf.Ui.Controls.Button { Content = Strings.Web_Revoke, Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Delete24) };
            var id = device.Id;
            revoke.Click += (_, _) =>
            {
                (Host.DeckWeb?.Devices ?? new DeckWebDevices(DeckWebDevices.DefaultPath)).Revoke(id);
                BuildDeckWeb();
            };
            WC.DockPanel.SetDock(revoke, WC.Dock.Right);
            row.Children.Add(revoke);
            var info = new WC.StackPanel();
            info.Children.Add(Text(device.Name));
            var seen = device.LastSeen is { } last ? Core.Text.F(Strings.Web_LastSeen, last.LocalDateTime.ToString("g", culture)) : Strings.Web_NeverSeen;
            info.Children.Add(Text(seen, "TextFillColorSecondaryBrush", 12));
            row.Children.Add(info);
            WebDevices.Children.Add(row);
        }
    }

    private async void Web_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingGestures)
        {
            return;
        }

        WebAddress.Text = WebSwitch.IsChecked == true ? Strings.Web_Starting : "";
        WebSwitch.IsEnabled = false;
        await Host.SetDeckWeb(WebSwitch.IsChecked == true);
        WebSwitch.IsEnabled = true;
        _webHooked = false;
        BuildDeckWeb();
    }

    /// <summary>Los ajustes de red de Windows, para marcar la red de casa como privada.</summary>
    private void WebNetwork_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true })?.Dispose();

    private void WebPages_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingGestures)
        {
            _settings.DeckWebPageTitles = WebPagesSwitch.IsChecked == true;
            _settings.Save();
        }
    }

    /// <summary>Un código nuevo con su QR: la tablet lo escanea y queda emparejada.</summary>
    private void WebPair_Click(object sender, RoutedEventArgs e)
    {
        if (Host.DeckWeb is not { } server)
        {
            return;
        }

        var code = server.Devices.NewCode();
        if (server.PairingUrl(code) is not { } url)
        {
            return;
        }

        WebCode.Text = $"{code[..3]} {code[3..]}";
        WebPairUrl.Text = url;
        WebQr.Source = Qr(url);
        WebPairing.Visibility = Visibility.Visible;

        // El código caduca: entonces se esconde, para no escanear uno que ya no vale.
        _webCodeTimer?.Stop();
        _webCodeTimer = new System.Windows.Threading.DispatcherTimer { Interval = DeckWebDevices.CodeLife };
        _webCodeTimer.Tick += (_, _) =>
        {
            _webCodeTimer.Stop();
            WebPairing.Visibility = Visibility.Collapsed;
        };
        _webCodeTimer.Start();
    }

    private static BitmapImage Qr(string text)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
