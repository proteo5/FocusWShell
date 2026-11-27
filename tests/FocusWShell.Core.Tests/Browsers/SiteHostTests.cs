using FocusWShell.Core.Browsers;

namespace FocusWShell.Core.Tests.Browsers;

public class SiteHostTests
{
    [Theory]
    [InlineData("https://www.github.com/proteo5/FocusWShell/pulls?q=is%3Aopen", "github.com")] // Edge: con esquema
    [InlineData("github.com/proteo5/FocusWShell", "github.com")]                           // Chrome: sin esquema
    [InlineData("docs.google.com/document/d/abc/edit#heading=h.1", "docs.google.com")]
    [InlineData("http://Example.COM:8080/x", "example.com")]
    [InlineData("localhost:5000/portal/index.html#/", "localhost")]
    [InlineData("http://192.168.1.10/admin", "192.168.1.10")]
    public void KeepsOnlyTheHost(string address, string host) => Assert.Equal(host, SiteHost.From(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hola mundo")]            // una búsqueda que se escribe
    [InlineData("hola")]
    [InlineData("edge://settings/profiles")]
    [InlineData("chrome://newtab")]
    [InlineData("about:blank")]
    [InlineData("file:///C:/Users/ana/notas.html")]
    public void WhatIsNotAWebPageHasNoSite(string? address) => Assert.Null(SiteHost.From(address));
}
