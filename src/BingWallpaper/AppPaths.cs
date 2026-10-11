namespace BingWallpaper;

/// <summary>运行时的所有落盘路径，统一放在 %LOCALAPPDATA%\BingWallpaper。</summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BingWallpaper");

    public static string Images { get; } = Path.Combine(Root, "images");
    public static string IndexFile { get; } = Path.Combine(Root, "index.json");
    public static string LogFile { get; } = Path.Combine(Root, "log.txt");

    public static void EnsureCreated() => Directory.CreateDirectory(Images);
}
