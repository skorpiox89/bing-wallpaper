using System.Drawing;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace BingWallpaper;

/// <summary>托盘本体：图标 + 右键菜单 + 每日定时检查。</summary>
internal sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan DailyCheckInterval = TimeSpan.FromMinutes(10);

    private readonly WallpaperService _service = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Icon _icon;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _previousItem;
    private readonly ToolStripMenuItem _nextItem;
    private readonly ToolStripMenuItem _todayItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly Timer _dailyTimer;
    private readonly Timer _startupTimer;

    private bool _busy;

    public TrayApp()
    {
        _icon = LoadIcon();

        _previousItem = new ToolStripMenuItem("上一张", null,
            async (_, _) => await GuardAsync(() => _service.StepAsync(-1)));
        _nextItem = new ToolStripMenuItem("下一张", null,
            async (_, _) => await GuardAsync(() => _service.StepAsync(1)));
        _todayItem = new ToolStripMenuItem("换今天的", null,
            async (_, _) => await GuardAsync(() => _service.ApplyTodayAsync(withHistory: false)));
        _autostartItem = new ToolStripMenuItem("开机自启", null, (_, _) => ToggleAutostart());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => Quit());

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _previousItem,
            _nextItem,
            _todayItem,
            new ToolStripSeparator(),
            _autostartItem,
            new ToolStripSeparator(),
            exitItem,
        });
        _menu.Opening += (_, _) => RefreshUi();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "Bing 壁纸",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _tray.DoubleClick += async (_, _) => await GuardAsync(() => _service.ApplyTodayAsync(withHistory: false));

        _dailyTimer = new Timer { Interval = (int)DailyCheckInterval.TotalMilliseconds };
        _dailyTimer.Tick += async (_, _) => await GuardAsync(() => _service.AutoRolloverAsync());

        // 等消息循环起来再干活，避免启动时序问题
        _startupTimer = new Timer { Interval = 200 };
        _startupTimer.Tick += async (_, _) =>
        {
            _startupTimer.Stop();
            await StartupAsync();
        };
        _startupTimer.Start();
    }

    private async Task StartupAsync()
    {
        if (!_gate.Wait(0))
        {
            return;
        }

        SetBusy(true);
        try
        {
            _service.Load();

            var firstRun = !_service.State.Initialized;
            Log.Info(firstRun ? "首次运行" : "启动");

            await _service.ApplyTodayAsync(withHistory: true);

            if (firstRun)
            {
                Autostart.Enable(); // 不常驻就不会"每天自动换"，所以默认开启
                _service.State.Initialized = true;
                _service.Save();
            }

            var pending = _service.PendingDownloads();
            if (pending.Count > 0)
            {
                _ = Task.Run(() => _service.DownloadAllAsync(pending)); // 后台补齐历史，不阻塞托盘
            }
        }
        catch (Exception ex)
        {
            Log.Error("启动流程失败", ex);
        }
        finally
        {
            _gate.Release();
            _dailyTimer.Start();
            SetBusy(false);
        }
    }

    private async Task GuardAsync(Func<Task<bool>> work)
    {
        if (!_gate.Wait(0))
        {
            return; // 正忙，忽略这次点击
        }

        SetBusy(true);
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log.Error("操作失败", ex);
        }
        finally
        {
            _gate.Release();
            SetBusy(false);
        }
    }

    private void ToggleAutostart()
    {
        if (Autostart.IsEnabled)
        {
            Autostart.Disable();
        }
        else
        {
            Autostart.Enable();
        }

        RefreshUi();
    }

    private void RefreshUi()
    {
        _previousItem.Enabled = !_busy && _service.CanGoPrevious;
        _nextItem.Enabled = !_busy && _service.CanGoNext;
        _todayItem.Enabled = !_busy;
        _autostartItem.Checked = Autostart.IsEnabled;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SetTrayText(busy ? "Bing 壁纸 · 处理中…" : BuildTooltip());
        RefreshUi();
    }

    private string BuildTooltip()
    {
        var current = _service.Current;
        if (current is null)
        {
            return "Bing 壁纸";
        }

        var title = current.Title;
        if (title.Length > 40)
        {
            title = title[..40] + "…";
        }

        return string.IsNullOrEmpty(title)
            ? $"Bing 壁纸 · {FormatDate(current.Date)}"
            : $"Bing 壁纸 · {FormatDate(current.Date)} {title}";
    }

    private static string FormatDate(string yyyymmdd)
    {
        return yyyymmdd.Length == 8
            ? $"{yyyymmdd[..4]}-{yyyymmdd[4..6]}-{yyyymmdd[6..8]}"
            : yyyymmdd;
    }

    private void SetTrayText(string text)
    {
        try
        {
            _tray.Text = text.Length > 120 ? text[..120] : text;
        }
        catch (Exception ex)
        {
            Log.Error("设置托盘提示失败", ex);
        }
    }

    private void Quit()
    {
        _dailyTimer.Stop();
        _startupTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _icon.Dispose();
        Log.Info("退出");
        ExitThread();
    }

    private static Icon LoadIcon()
    {
        try
        {
            using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
            if (stream is not null)
            {
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Error("加载图标失败", ex);
        }

        return SystemIcons.Application;
    }
}
