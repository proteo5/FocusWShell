using FocusWShell.Core.Domain;

namespace FocusWShell.Core.Tests.Domain;

public class AppCatalogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewAppIsRegisteredWithHowToOpenIt()
    {
        var c = new AppCatalog();

        bool changed = c.Observe("slack.exe", "Slack", @"C:\Apps\slack.exe", null, T0);

        Assert.True(changed);
        var app = c.Find("SLACK.EXE")!; // sin distinguir mayúsculas
        Assert.Equal("Slack", app.Name);
        Assert.Equal(@"C:\Apps\slack.exe", app.ExecutablePath);
        Assert.True(app.CanLaunch);
        Assert.Null(app.LastPanelId);
    }

    [Fact]
    public void SeeingItAgainOnlyMovesLastSeenAndDoesNotAskToSave()
    {
        var c = new AppCatalog();
        c.Observe("slack.exe", "Slack", @"C:\Apps\slack.exe", null, T0);

        bool changed = c.Observe("slack.exe", "Slack", @"C:\Apps\slack.exe", null, T0.AddHours(1));

        Assert.False(changed);
        Assert.Equal(T0.AddHours(1), c.Find("slack.exe")!.LastSeen);
        Assert.Equal(T0, c.Find("slack.exe")!.FirstSeen);
    }

    [Fact]
    public void MissingLaunchInfoIsFilledLater()
    {
        var c = new AppCatalog();
        c.Observe("whatsapp", "WhatsApp", null, null, T0);
        Assert.False(c.Find("whatsapp")!.CanLaunch);

        Assert.True(c.Observe("whatsapp", "WhatsApp", null, "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", T0));
        Assert.True(c.Find("whatsapp")!.CanLaunch);
    }

    [Fact]
    public void AnUpdatedAppGetsItsNewPath()
    {
        // Las apps de la Store cambian de carpeta en cada versión; la vieja deja de existir.
        var c = new AppCatalog();
        c.Observe("claude.exe", "Claude", @"C:\Program Files\WindowsApps\Claude_4.1_x64pp\claude.exe", null, T0);

        Assert.True(c.Observe("claude.exe", "Claude", @"C:\Program Files\WindowsApps\Claude_4.2_x64pp\claude.exe", null, T0.AddDays(1)));
        Assert.Equal(@"C:\Program Files\WindowsApps\Claude_4.2_x64pp\claude.exe", c.Find("claude.exe")!.ExecutablePath);

        // Una ventana sin ruta legible no borra la que se sabía.
        Assert.False(c.Observe("claude.exe", "Claude", null, null, T0.AddDays(2)));
        Assert.NotNull(c.Find("claude.exe")!.ExecutablePath);
    }

    [Fact]
    public void RemembersTheLastPanel()
    {
        var c = new AppCatalog();
        c.Observe("slack.exe", "Slack", null, null, T0);

        Assert.True(c.RememberPanel("slack.exe", "contexto"));
        Assert.False(c.RememberPanel("slack.exe", "contexto"));
        Assert.False(c.RememberPanel("desconocida.exe", "contexto"));
        Assert.Equal("contexto", c.Find("slack.exe")!.LastPanelId);
    }

    [Fact]
    public void RenameHideAndForget()
    {
        var c = new AppCatalog();
        c.Observe("ssms.exe", "SSMS", null, null, T0);

        Assert.True(c.Rename("ssms.exe", " SQL Server "));
        Assert.True(c.SetHidden("ssms.exe", true));
        Assert.Equal("SQL Server", c.Find("ssms.exe")!.Name);
        Assert.True(c.Find("ssms.exe")!.Hidden);
        Assert.True(c.Forget("ssms.exe"));
        Assert.Null(c.Find("ssms.exe"));
    }

    [Fact]
    public void AppsAreListedByName()
    {
        var c = new AppCatalog();
        c.Observe("b", "Zoom", null, null, T0);
        c.Observe("a", "edge", null, null, T0);

        Assert.Equal(["edge", "Zoom"], c.Apps.Select(a => a.Name));
    }

    [Fact]
    public void AnAutomaticNameCanBeImprovedButNotOneTheUserChose()
    {
        const string Calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var c = new AppCatalog();
        c.Observe(Calc, "Application Frame Host", null, Calc, T0);
        c.Observe("slack.exe", "Slack", null, null, T0);
        c.Rename("slack.exe", "Chat del trabajo");

        Assert.True(c.ImproveName(Calc, "Application Frame Host", "Calculator"));
        Assert.Equal("Calculator", c.Find(Calc)!.Name);
        Assert.False(c.Find(Calc)!.Renamed);
        Assert.False(c.ImproveName(Calc, "Application Frame Host", "Otra")); // ya no es el nombre viejo
        Assert.True(c.Find("slack.exe")!.Renamed);
        Assert.False(c.ImproveName("slack.exe", "Chat del trabajo", "Slack"));
    }
}
