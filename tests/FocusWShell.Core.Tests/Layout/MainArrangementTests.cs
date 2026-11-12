using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Los botones de la disposición del principal (ADR 0012, enmienda 2026-11-12).</summary>
public class MainArrangementTests
{
    [Theory]
    [InlineData(3440, 1440, true)]
    [InlineData(5120, 1440, true)]
    [InlineData(2560, 1080, true)]
    [InlineData(1920, 1080, false)]
    [InlineData(1920, 1200, false)]
    [InlineData(3840, 2160, false)]
    public void UltrawideIsByProportion(int width, int height, bool expected) =>
        Assert.Equal(expected, MainArrangement.IsUltrawide(PixelRect.FromSize(0, 0, width, height)));

    [Theory]
    [InlineData(FocusPosition.Center, FocusPosition.WorkOnly)]
    [InlineData(FocusPosition.WorkOnly, FocusPosition.Center)]
    [InlineData(FocusPosition.ContextOnly, FocusPosition.FocusOnly)]
    [InlineData(FocusPosition.FocusOnly, FocusPosition.ContextOnly)]
    [InlineData(FocusPosition.Right, FocusPosition.FocusOnly)] // con Trabajo prestado aquí, se apagan las dos
    [InlineData(FocusPosition.Left, FocusPosition.Center)] // Contexto prestado vuelve a su lado
    public void ToggleLeft(FocusPosition from, FocusPosition to) => Assert.Equal(to, MainArrangement.ToggleLeft(from));

    [Theory]
    [InlineData(FocusPosition.Center, FocusPosition.ContextOnly)]
    [InlineData(FocusPosition.ContextOnly, FocusPosition.Center)]
    [InlineData(FocusPosition.WorkOnly, FocusPosition.FocusOnly)]
    [InlineData(FocusPosition.FocusOnly, FocusPosition.WorkOnly)]
    [InlineData(FocusPosition.Left, FocusPosition.FocusOnly)]
    [InlineData(FocusPosition.Right, FocusPosition.Center)]
    public void ToggleRight(FocusPosition from, FocusPosition to) => Assert.Equal(to, MainArrangement.ToggleRight(from));

    [Fact]
    public void TheExtraButtonOnlyExistsOnAnUltrawideWithTheOtherSideOff()
    {
        Assert.True(MainArrangement.CanBorrowToLeft(FocusPosition.ContextOnly, ultrawide: true));
        Assert.True(MainArrangement.CanBorrowToLeft(FocusPosition.Right, ultrawide: true));
        Assert.False(MainArrangement.CanBorrowToLeft(FocusPosition.Center, ultrawide: true));
        Assert.False(MainArrangement.CanBorrowToLeft(FocusPosition.FocusOnly, ultrawide: true));
        Assert.False(MainArrangement.CanBorrowToLeft(FocusPosition.ContextOnly, ultrawide: false));
        Assert.True(MainArrangement.CanBorrowToRight(FocusPosition.WorkOnly, ultrawide: true));
        Assert.False(MainArrangement.CanBorrowToRight(FocusPosition.ContextOnly, ultrawide: true));
    }

    [Fact]
    public void TheExtraButtonBringsTheOtherColumnAndTakesItBack()
    {
        Assert.Equal(FocusPosition.Right, MainArrangement.ToggleBorrowToLeft(FocusPosition.ContextOnly));
        Assert.Equal(FocusPosition.ContextOnly, MainArrangement.ToggleBorrowToLeft(FocusPosition.Right));
        Assert.Equal(FocusPosition.Left, MainArrangement.ToggleBorrowToRight(FocusPosition.WorkOnly));
        Assert.Equal(FocusPosition.WorkOnly, MainArrangement.ToggleBorrowToRight(FocusPosition.Left));
    }

    [Fact]
    public void WithoutAnUltrawideABorrowedColumnGoesBackToItsSide()
    {
        Assert.Equal(FocusPosition.Center, MainArrangement.Effective(FocusPosition.Left, ultrawide: false));
        Assert.Equal(FocusPosition.Center, MainArrangement.Effective(FocusPosition.Right, ultrawide: false));
        Assert.Equal(FocusPosition.Right, MainArrangement.Effective(FocusPosition.Right, ultrawide: true));
        Assert.Equal(FocusPosition.WorkOnly, MainArrangement.Effective(FocusPosition.WorkOnly, ultrawide: false));
    }

    [Fact]
    public void TheShortcutCyclesWhatTheMonitorOffers()
    {
        Assert.Equal(FocusPosition.ContextOnly, MainArrangement.Next(FocusPosition.Center, ultrawide: false));
        Assert.Equal(FocusPosition.Center, MainArrangement.Next(FocusPosition.FocusOnly, ultrawide: false));
        Assert.Equal(FocusPosition.Right, MainArrangement.Next(FocusPosition.Center, ultrawide: true));
        Assert.Equal(FocusPosition.ContextOnly, MainArrangement.Next(FocusPosition.Right, ultrawide: false)); // se ve como Centro
    }
}
