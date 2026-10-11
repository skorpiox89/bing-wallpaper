using System.Runtime.InteropServices;

namespace BingWallpaper;

internal static class Wallpaper
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    /// <summary>把图片设为桌面壁纸。不修改 WallpaperStyle，避免覆盖用户的"填充/适应"设置。</summary>
    public static bool Set(string path)
    {
        if (!File.Exists(path))
        {
            Log.Error($"壁纸文件不存在：{path}");
            return false;
        }

        var ok = SystemParametersInfo(
            SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        if (!ok)
        {
            Log.Error($"设置壁纸失败（Win32 错误码 {Marshal.GetLastWin32Error()}）：{path}");
            return false;
        }

        Log.Info($"壁纸已更换：{Path.GetFileName(path)}");
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(int action, int param, string value, int winIni);
}
