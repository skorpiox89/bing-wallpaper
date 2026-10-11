using System.Windows.Forms;

namespace BingWallpaper;

internal static class Program
{
    private const string InstanceMutex = "BingWallpaper.SingleInstance";

    [STAThread]
    private static void Main()
    {
        // 单实例：已经在跑就直接退出，避免两份程序抢壁纸
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutex, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Info("已有实例在运行，本次启动退出");
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("界面线程异常", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("未处理异常", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("后台任务异常", e.Exception);
            e.SetObserved();
        };

        Log.Info("===== Bing 壁纸启动 =====");

        using var app = new TrayApp();
        Application.Run(app);
    }
}
