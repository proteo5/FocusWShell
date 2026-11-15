using FocusWShell.Core.Displays;

namespace FocusWShell.Core.Tests.Displays;

/// <summary>Un monitor conocido que vuelve con otra clave (la tablet de Apollo estrena una en cada conexión).</summary>
public class DisplayAliasesTests
{
    private static string Key(string model, int uid) => $@"\\?\DISPLAY#{model}#1&28a6823a&0&UID{uid}#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}";

    private static readonly string Hp = Key("HPN3545", 280);

    [Fact]
    public void TheTabletWithANewNumberIsTheSameTablet()
    {
        var aliases = DisplayAliases.Resolve([Hp, Key("SMKD1CE", 259)], [Hp, Key("SMKD1CE", 256)]);

        Assert.Equal(Key("SMKD1CE", 256), aliases[Key("SMKD1CE", 259)]);
        Assert.Single(aliases);
    }

    [Fact]
    public void AKnownKeyNeedsNoAlias()
    {
        Assert.Empty(DisplayAliases.Resolve([Hp, Key("SMKD1CE", 256)], [Hp, Key("SMKD1CE", 256)]));
    }

    [Fact]
    public void AnotherModelIsNotTheMissingMonitor()
    {
        Assert.Empty(DisplayAliases.Resolve([Hp, Key("DELA0B1", 300)], [Hp, Key("SMKD1CE", 256)]));
    }

    [Fact]
    public void TwoMonitorsOfTheSameModelAreNotGuessed()
    {
        // Dos nuevos y uno ausente, o uno nuevo y dos ausentes: no se sabe cuál es cuál.
        Assert.Empty(DisplayAliases.Resolve([Key("HPN3545", 290), Key("HPN3545", 291)], [Key("HPN3545", 280)]));
        Assert.Empty(DisplayAliases.Resolve([Key("HPN3545", 290)], [Key("HPN3545", 280), Key("HPN3545", 281)]));
    }

    [Fact]
    public void AKeyThatIsNotADevicePathHasNoModel()
    {
        Assert.Null(DisplayAliases.Model(@"\\.\DISPLAY1"));
        Assert.Equal("SMKD1CE", DisplayAliases.Model(Key("SMKD1CE", 256)));
    }
}
