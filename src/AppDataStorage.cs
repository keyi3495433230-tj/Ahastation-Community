namespace CardReview;

internal static class AppDataStorage
{
    private static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AhastationCommunity");
    public static string Root { get; } = DefaultRoot;

    public static bool CompleteFactoryResetAfterExit(string[] args)
    {
        if (args.Length < 3 || args[1] != "--factory-reset-after-exit") return false;
        if (!int.TryParse(args[2], out var processId)) throw new ArgumentException("恢复出厂设置请求无效。");
        try { using var process = System.Diagnostics.Process.GetProcessById(processId); process.WaitForExit(); }
        catch (ArgumentException) { }
        Thread.Sleep(750);
        ResetUserData();
        return true;
    }

    private static void ResetUserData()
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                    foreach (var entry in Directory.EnumerateFileSystemEntries(Root)) DeleteEntry(entry);
                Directory.CreateDirectory(Root);
                return;
            }
            catch (IOException ex) { lastError = ex; Thread.Sleep(500); }
            catch (UnauthorizedAccessException ex) { lastError = ex; Thread.Sleep(500); }
        }
        throw new IOException("无法完全删除应用数据，请关闭应用后重试。", lastError);
    }

    private static void DeleteEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            if (isDirectory) Directory.Delete(path); else File.Delete(path);
            return;
        }
        if (!isDirectory) { File.Delete(path); return; }
        foreach (var child in Directory.EnumerateFileSystemEntries(path)) DeleteEntry(child);
        Directory.Delete(path);
    }

}
