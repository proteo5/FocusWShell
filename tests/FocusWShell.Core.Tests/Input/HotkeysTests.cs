using FocusWShell.Core.Input;

namespace FocusWShell.Core.Tests.Input;

/// <summary>Atajos configurables (M1).</summary>
public class HotkeysTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+Up", "Ctrl+Alt+Mayús+↑")]
    [InlineData("ctrl + alt + Back", "Ctrl+Alt+Retroceso")]
    [InlineData("Win+Alt+F", "Alt+Win+F")]
    [InlineData("Ctrl+Mayús+F5", "Ctrl+Mayús+F5")]
    [InlineData("Alt+7", "Alt+7")]
    public void ChordsAreParsedAndShownInSpanish(string text, string display)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(display, chord.Display());
        Assert.True(KeyChord.TryParse(chord.ToString(), out var again));
        Assert.Equal(chord, again); // el texto guardado vuelve a dar lo mismo
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")] // sin tecla
    [InlineData("Ctrl+A+B")] // dos teclas
    [InlineData("Ctrl+Pausa")] // tecla desconocida
    public void InvalidTextIsRejected(string text) => Assert.False(KeyChord.TryParse(text, out _));

    [Theory]
    [InlineData("Shift+A", false)] // se quedaría con una letra que se escribe
    [InlineData("A", false)]
    [InlineData("F13", true)]
    [InlineData("Win+Up", true)]
    public void AChordNeedsCtrlAltOrWin(string text, bool valid)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(valid, chord.IsValid);
    }

    [Fact]
    public void WithoutChangesTheDefaultsAreTheUsualOnes()
    {
        var map = Hotkeys.Resolve(null);

        Assert.Empty(map.Problems);
        Assert.Equal("Ctrl+Alt+Mayús+Retroceso", map.Chords[Hotkeys.Emergency].Display());
        Assert.Equal("Ctrl+Alt+Mayús+↑", map.Chords["al-foco"].Display());
        Assert.Equal("Ctrl+Alt+Mayús+9", map.Chords["escena-9"].Display());
        Assert.Equal(Hotkeys.Actions.Count, map.Chords.Count);
    }

    [Fact]
    public void AUserChordReplacesTheDefault()
    {
        var map = Hotkeys.Resolve(new Dictionary<string, string> { ["al-foco"] = "Win+Alt+Up" });

        Assert.Empty(map.Problems);
        Assert.Equal("Alt+Win+↑", map.Chords["al-foco"].Display());
    }

    [Fact]
    public void AnInvalidUserChordFallsBackToTheDefault()
    {
        var map = Hotkeys.Resolve(new Dictionary<string, string> { [Hotkeys.Emergency] = "Shift+X" });

        Assert.Equal("Ctrl+Alt+Mayús+Retroceso", map.Chords[Hotkeys.Emergency].Display());
        Assert.Single(map.Problems);
    }

    [Fact]
    public void ADuplicateKeepsTheFirstActionAndTheOtherGoesBackToItsDefault()
    {
        // "Volver a la anterior" pide el atajo de "Al foco".
        var map = Hotkeys.Resolve(new Dictionary<string, string> { ["anterior"] = "Ctrl+Alt+Shift+Up" });

        Assert.Equal("Ctrl+Alt+Mayús+↑", map.Chords["al-foco"].Display());
        Assert.Equal("Ctrl+Alt+Mayús+←", map.Chords["anterior"].Display());
        Assert.Contains(map.Problems, p => p.Contains("ya es de Al foco", StringComparison.Ordinal));
    }

    [Fact]
    public void SwappingTwoChordsWorks()
    {
        var map = Hotkeys.Resolve(new Dictionary<string, string> { ["al-foco"] = "Ctrl+Alt+Shift+Left", ["anterior"] = "Ctrl+Alt+Shift+Up" });

        Assert.Empty(map.Problems);
        Assert.Equal("Ctrl+Alt+Mayús+←", map.Chords["al-foco"].Display());
        Assert.Equal("Ctrl+Alt+Mayús+↑", map.Chords["anterior"].Display());
    }
}
