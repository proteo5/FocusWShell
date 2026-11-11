using FocusWShell.Core.Windows;

namespace FocusWShell.Core.Tests.Windows;

public class UnmanageableTests
{
    [Theory]
    [InlineData(IntegrityLevel.Medium, IntegrityLevel.Medium)]
    [InlineData(IntegrityLevel.Low, IntegrityLevel.Medium)]
    [InlineData(IntegrityLevel.High, IntegrityLevel.High)]
    public void SameOrLowerIntegrityIsManageable(IntegrityLevel target, IntegrityLevel own) =>
        Assert.Null(Unmanageable.Reason(target, own));

    [Fact]
    public void ElevatedWindowFromANormalManager() =>
        Assert.Equal("corre como administrador", Unmanageable.Reason(IntegrityLevel.High, IntegrityLevel.Medium));

    [Fact]
    public void SystemWindow() =>
        Assert.Equal("es del sistema", Unmanageable.Reason(IntegrityLevel.System, IntegrityLevel.High));

    [Fact]
    public void UnknownIsNeverManageable() =>
        Assert.Contains("no se pudo leer", Unmanageable.Reason(IntegrityLevel.Unknown, IntegrityLevel.System));
}
