namespace LongevityWorldCup.Website.Business;

internal sealed class ApplicationSubmissionWorkspace : IDisposable
{
    private int _disposed;

    private ApplicationSubmissionWorkspace(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static ApplicationSubmissionWorkspace Create()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "LWC",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return new ApplicationSubmissionWorkspace(rootPath);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // The operating system can reclaim a best-effort temporary workspace.
        }
    }
}
