using RigForge.Host.Daemon.Tray;

namespace RigForge.UnitTests.Host;

public sealed class TrayIconPolicyTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldShow_only_for_an_interactive_windows_process(bool isWindows, bool isWindowsService, bool expected)
    {
        Assert.Equal(expected, TrayIconPolicy.ShouldShow(isWindows, isWindowsService));
    }

    [Fact]
    public void DashboardUrl_uses_the_first_loopback_listen_address_and_opens_the_dashboard()
    {
        var url = TrayIconPolicy.DashboardUrl(
        [
            "http://0.0.0.0:5757",
            "http://127.0.0.1:5757/",
            "http://localhost:9999",
        ]);

        Assert.Equal("http://127.0.0.1:5757/#/dashboard", url);
    }

    [Fact]
    public void DashboardUrl_keeps_localhost_and_falls_back_when_nothing_is_loopback()
    {
        Assert.Equal("http://localhost:5757/#/dashboard", TrayIconPolicy.DashboardUrl(["http://localhost:5757"]));
        Assert.Equal(TrayIconPolicy.FallbackDashboardUrl, TrayIconPolicy.DashboardUrl(["http://+:5757", "https://127.0.0.1:5757"]));
        Assert.Equal(TrayIconPolicy.FallbackDashboardUrl, TrayIconPolicy.DashboardUrl(null));
    }

    [Theory]
    [InlineData("http://evil.example/#/dashboard")]
    [InlineData("https://127.0.0.1:5757/#/dashboard")]
    [InlineData("http://127.0.0.1.evil.example/")]
    [InlineData("http://[::1]:5757/")]
    [InlineData("file:///C:/ui/index.html")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalizeDashboardUrl_rejects_anything_that_is_not_http_loopback(string? candidate)
    {
        Assert.Null(TrayIconPolicy.TryNormalizeDashboardUrl(candidate));
    }

    [Fact]
    public void FindShellExecutable_returns_the_first_candidate_that_exists()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\missing\later\RigForge.exe" };
        var found = TrayIconPolicy.FindShellExecutable(
            [@"C:\absent\RigForge.exe", @"D:\missing\later\RigForge.exe", @"E:\also\RigForge.exe"],
            existing.Contains);

        Assert.Equal(@"D:\missing\later\RigForge.exe", found);
        Assert.Null(TrayIconPolicy.FindShellExecutable([@"C:\absent\RigForge.exe"], static _ => false));
    }

    [Fact]
    public void ShellCandidates_prefer_the_daemon_directory_then_the_repository_unpack()
    {
        var candidates = TrayIconPolicy.ShellCandidates("daemon", "repo").ToArray();

        Assert.Equal(
            [
                Path.Combine("daemon", TrayIconPolicy.ShellFileName),
                Path.Combine("repo", "shell", "dist", "win-unpacked", TrayIconPolicy.ShellFileName),
            ],
            candidates);
    }

    [Fact]
    public void FindRepositoryRoot_walks_up_to_the_directory_that_contains_the_daemon_project()
    {
        var root = Directory.CreateTempSubdirectory("rigforge-repo-");
        try
        {
            var nested = Path.Combine(root.FullName, "src", "Host.Daemon", "bin");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), "<Project />");

            Assert.Equal(root.FullName, TrayIconPolicy.FindRepositoryRoot(nested));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
