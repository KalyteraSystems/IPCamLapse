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
        RootPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "data"))
            : Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath));
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
}
