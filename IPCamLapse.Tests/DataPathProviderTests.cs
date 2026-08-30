using IPCamLapse.Services;

namespace IPCamLapse.Tests;

public sealed class DataPathProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"ipcamlapse-path-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ResolveRootPath_UsesConfiguredAbsolutePath()
    {
        var configured = Path.Combine(_root, "configured");

        var result = DataPathProvider.ResolveRootPath(configured, Path.Combine(_root, "app"), string.Empty);

        Assert.Equal(Path.GetFullPath(configured), result);
    }

    [Fact]
    public void ResolveRootPath_ResolvesConfiguredRelativePathFromContentRoot()
    {
        var contentRoot = Path.Combine(_root, "app");

        var result = DataPathProvider.ResolveRootPath("captures", contentRoot, string.Empty);

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "captures")), result);
    }

    [Fact]
    public void ResolveRootPath_ReusesExistingLegacyDirectory()
    {
        var contentRoot = Path.Combine(_root, "app");
        var legacyPath = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(legacyPath);

        var result = DataPathProvider.ResolveRootPath(null, contentRoot, Path.Combine(_root, "local"));

        Assert.Equal(Path.GetFullPath(legacyPath), result);
    }

    [Fact]
    public void ResolveRootPath_UsesUserDataDirectoryForNewInstall()
    {
        var contentRoot = Path.Combine(_root, "app");
        var localApplicationData = Path.Combine(_root, "local");

        var result = DataPathProvider.ResolveRootPath(null, contentRoot, localApplicationData);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(localApplicationData, "KalyteraSystems", "IPCamLapse")),
            result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
