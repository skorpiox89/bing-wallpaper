namespace BingWallpaper;

/// <summary>极简文件日志，超过 1 MB 直接清空重来。</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            if (File.Exists(AppPaths.LogFile) && new FileInfo(AppPaths.LogFile).Length > MaxBytes)
            {
                File.Delete(AppPaths.LogFile);
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            if (ex is not null)
            {
                line += " :: " + ex.GetType().Name + ": " + ex.Message;
            }

            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志本身失败不影响主流程
        }
    }
}
