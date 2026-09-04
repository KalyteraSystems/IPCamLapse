using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Services;

internal sealed class DataProtectionKeyRingOptionsSetup(
    IConfiguration configuration,
    IDataPathProvider paths,
    ILoggerFactory loggerFactory) : IConfigureOptions<KeyManagementOptions>
{
    internal const string ConfigurationKey = "DataProtection:KeysPath";

    public void Configure(KeyManagementOptions options)
    {
        var configuredPath = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
            return;

        var keysPath = ResolvePath(configuredPath, paths.RootPath);
        var keysDirectory = Directory.CreateDirectory(keysPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                keysDirectory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        options.XmlRepository = new FileSystemXmlRepository(keysDirectory, loggerFactory);
    }

    internal static string ResolvePath(string configuredPath, string dataRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRootPath);
        return Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(dataRootPath, configuredPath));
    }
}
