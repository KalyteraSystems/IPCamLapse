namespace IPCamLapse.Services;

/// <summary>
/// The file operations retention needs, behind an interface so tests can make a
/// delete fail without depending on OS file locking or permissions.
/// </summary>
public interface ISessionFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void DeleteDirectory(string path);
    void DeleteFile(string path);
    IEnumerable<string> EnumerateFiles(string path);
    long GetFileLength(string path);
}

public sealed class SessionFileSystem : ISessionFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteDirectory(string path) => Directory.Delete(path, true);

    public void DeleteFile(string path) => File.Delete(path);

    public IEnumerable<string> EnumerateFiles(string path)
        => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);

    public long GetFileLength(string path) => new FileInfo(path).Length;
}
