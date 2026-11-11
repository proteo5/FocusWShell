using FocusWShell.Core.Domain;

namespace FocusWShell.Core.Tests.Domain;

public class ExecutablePathsTests
{
    private const string Drive = @"C:\Program Files\Google\Drive File Stream";

    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        $@"{Drive}\134.0.1.0\GoogleDriveFS.exe",
        $@"{Drive}\99.0.0.0\GoogleDriveFS.exe",
        @"C:\Users\x\AppData\Local\GitHubDesktop\app-3.6.11\GitHubDesktop.exe",
        @"C:\Apps\slack.exe",
    };

    private static readonly Dictionary<string, string[]> Dirs = new(StringComparer.OrdinalIgnoreCase)
    {
        [Drive] = [$@"{Drive}\99.0.0.0", $@"{Drive}\133.0.2.0", $@"{Drive}\134.0.1.0", $@"{Drive}\Drivers"],
        [@"C:\Users\x\AppData\Local\GitHubDesktop"] = [@"C:\Users\x\AppData\Local\GitHubDesktop\app-3.6.11", @"C:\Users\x\AppData\Local\GitHubDesktop\packages"],
    };

    private static string? Resolve(string path) =>
        ExecutablePaths.Resolve(path, Files.Contains, d => Dirs.TryGetValue(d, out var list) ? list : []);

    [Fact]
    public void ExistingPathIsKept() => Assert.Equal(@"C:\Apps\slack.exe", Resolve(@"C:\Apps\slack.exe"));

    [Fact]
    public void StaleVersionFolderFindsTheNewestSibling() =>
        Assert.Equal($@"{Drive}\134.0.1.0\GoogleDriveFS.exe", Resolve($@"{Drive}\133.0.2.0\GoogleDriveFS.exe"));

    [Fact]
    public void PrefixedVersionFolders() =>
        Assert.Equal(@"C:\Users\x\AppData\Local\GitHubDesktop\app-3.6.11\GitHubDesktop.exe",
            Resolve(@"C:\Users\x\AppData\Local\GitHubDesktop\app-3.6.10\GitHubDesktop.exe"));

    [Fact]
    public void NothingFound() => Assert.Null(Resolve(@"C:\Gone\Old\app.exe"));

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2607.14.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App")]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_4.23118.1.0_x64__pzs8sxrjxfjjc\app\claude.exe", "Claude_pzs8sxrjxfjjc!App")]
    [InlineData(@"C:\Program Files\Notepad++\notepad++.exe", null)]
    [InlineData(null, null)]
    public void GuessesThePackagedAumid(string? path, string? expected) =>
        Assert.Equal(expected, ExecutablePaths.GuessPackagedAumid(path));
}
