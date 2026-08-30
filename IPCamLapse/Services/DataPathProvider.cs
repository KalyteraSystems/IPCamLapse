namespace IPCamLapse.Services;

public interface IDataPathProvider
{
    string RootPath { get; }
    string SessionsPath { get; }
    string ProfilesPath { get; }
    string SettingsPath { get; }
}

public sealed class DataPathProvider : IDataPathProvider
{
    public DataPathProvider(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = configuration["Storage:DataPath"];
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootPath = ResolveRootPath(configuredPath, environment.ContentRootPath, localApplicationData);
        SessionsPath = Path.Combine(RootPath, "sessions");
        ProfilesPath = Path.Combine(RootPath, "camera-profiles.json");
        SettingsPath = Path.Combine(RootPath, "settings.json");
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(SessionsPath);
    }

    public string RootPath { get; }
    public string SessionsPath { get; }
    public string ProfilesPath { get; }
    public string SettingsPath { get; }

    internal static string ResolveRootPath(
        string? configuredPath,
        string contentRootPath,
        string localApplicationDataPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRootPath, configuredPath));
        }

        var legacyPath = Path.GetFullPath(Path.Combine(contentRootPath, "data"));
        if (Directory.Exists(legacyPath) || string.IsNullOrWhiteSpace(localApplicationDataPath))
            return legacyPath;

        return Path.GetFullPath(Path.Combine(localApplicationDataPath, "KalyteraSystems", "IPCamLapse"));
    }
}
