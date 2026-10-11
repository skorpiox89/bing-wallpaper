using Microsoft.Win32;

namespace BingWallpaper;

/// <summary>开机自启 = HKCU\...\Run 下的一条记录，注册表即唯一事实来源。</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BingWallpaper";

    private static string ExePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                if (key?.GetValue(ValueName) is not string value)
                {
                    return false;
                }

                return value.Trim('"').Equals(ExePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Error("读取开机自启状态失败", ex);
                return false;
            }
        }
    }

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, $"\"{ExePath}\"");
            Log.Info($"已设置开机自启：{ExePath}");
        }
        catch (Exception ex)
        {
            Log.Error("设置开机自启失败", ex);
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("已取消开机自启");
        }
        catch (Exception ex)
        {
            Log.Error("取消开机自启失败", ex);
        }
    }
}
