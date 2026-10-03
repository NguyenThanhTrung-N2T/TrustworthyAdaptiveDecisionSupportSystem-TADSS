namespace TADSS.UnitTests.TestData;

/// <summary>Tìm đường dẫn trong repo bằng cách đi ngược từ thư mục chạy test tới thư mục chứa <c>TADSS.slnx</c>.</summary>
internal static class RepositoryPaths
{
    public static string Resolve(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TADSS.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Không tìm thấy thư mục gốc chứa TADSS.slnx.");
        }

        return Path.Combine([directory.FullName, .. relativeParts]);
    }
}
