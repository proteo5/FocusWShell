using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FocusWShell.Core.Tests;

/// <summary>
/// Los textos en español (Strings.resx) y en inglés (Strings.en.resx) de cada proyecto: las mismas claves y los
/// mismos huecos ({0}, {1}…). Un hueco de menos en una traducción se notaría al usarla, en tiempo de ejecución.
/// </summary>
public partial class TranslationTests
{
    public static TheoryData<string> Projects => ["FocusWShell.Core", "FocusWShell.App"];

    [Theory]
    [MemberData(nameof(Projects))]
    public void EnglishHasEveryKeyWithTheSamePlaceholders(string project)
    {
        var folder = Path.Combine(RepoRoot(), "src", project);
        var spanish = Read(Path.Combine(folder, "Strings.resx"));
        var english = Read(Path.Combine(folder, "Strings.en.resx"));

        Assert.NotEmpty(spanish);
        Assert.Equal(spanish.Keys.Order(), english.Keys.Order());
        foreach (var (key, es) in spanish)
        {
            Assert.False(string.IsNullOrWhiteSpace(english[key]), $"{project}: {key} sin traducir");
            Assert.True(Placeholders(es).SetEquals(Placeholders(english[key])), $"{project}: {key} no tiene los mismos huecos en inglés");
        }
    }

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static HashSet<string> Placeholders(string text) => [.. Hole().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Hole();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FocusWShell.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
