using System.Drawing;

namespace BingWallpaper;

/// <summary>把"必应图片 → 本地库 → 桌面壁纸"串起来。</summary>
internal sealed class WallpaperService
{
    /// <summary>必应归档只保留最近 8 天，这是能回填的历史上限。</summary>
    private const int HistoryDays = 8;

    private readonly Library _library = new();
    private readonly Size _screenSize = DetectScreenSize();

    public LibraryState State => _library.State;

    public WallpaperEntry? Current
    {
        get
        {
            var state = State;
            return state.CurrentIndex >= 0 && state.CurrentIndex < state.Entries.Count
                ? state.Entries[state.CurrentIndex]
                : null;
        }
    }

    public bool CanGoPrevious => State.CurrentIndex > 0;

    public bool CanGoNext => State.CurrentIndex >= 0 && State.CurrentIndex < State.Entries.Count - 1;

    public void Load() => _library.Load();

    public void Save() => _library.Save();

    /// <summary>取今天的必应图并应用；withHistory 时把最近 8 天的元数据一起并入本地库。</summary>
    public async Task<bool> ApplyTodayAsync(bool withHistory)
    {
        var images = await BingApi.FetchAsync(withHistory ? HistoryDays : 1, _screenSize);
        Merge(images);

        var today = Today;
        var target = State.Entries.LastOrDefault(e => e.Date == today) ?? State.Entries.LastOrDefault();
        if (target is null)
        {
            Log.Error("本地库为空，无法设置壁纸");
            return false;
        }

        if (!await _library.EnsureFileAsync(target))
        {
            return false;
        }

        Apply(target);

        if (target.Date == today)
        {
            State.LastAutoKey = target.Key;
            State.LastAutoDate = today;
        }
        else
        {
            Log.Info($"必应尚未发布 {today} 的图片（最新为 {target.Date}），稍后重试");
        }

        _library.Save();
        return true;
    }

    /// <summary>每日检查：只有必应真的换了图，才去动用户的壁纸。</summary>
    public async Task<bool> AutoRolloverAsync()
    {
        var today = Today;
        if (State.LastAutoDate == today)
        {
            return false; // 今天已经自动换过，省掉一次请求
        }

        var images = await BingApi.FetchAsync(1, _screenSize);
        Merge(images);

        var latest = State.Entries.LastOrDefault();
        if (latest is null)
        {
            return false;
        }

        if (latest.Key == State.LastAutoKey)
        {
            Log.Info("必应尚未换图，等下一轮检查");
            return false;
        }

        if (!await _library.EnsureFileAsync(latest))
        {
            return false;
        }

        Apply(latest);
        State.LastAutoKey = latest.Key;
        State.LastAutoDate = today;
        _library.Save();
        Log.Info("已自动更换为今日壁纸");
        return true;
    }

    /// <summary>delta = -1 上一张，+1 下一张。</summary>
    public async Task<bool> StepAsync(int delta)
    {
        if (State.Entries.Count == 0)
        {
            return await ApplyTodayAsync(withHistory: true);
        }

        var index = State.CurrentIndex + delta;
        while (index >= 0 && index < State.Entries.Count)
        {
            var candidate = State.Entries[index];
            if (string.IsNullOrEmpty(candidate.Url) && !_library.HasFile(candidate))
            {
                index += delta; // 失效图，跳过去
                continue;
            }

            break;
        }

        if (index < 0 || index >= State.Entries.Count)
        {
            return false; // 到头了
        }

        var entry = State.Entries[index];
        if (!await _library.EnsureFileAsync(entry))
        {
            return false;
        }

        Apply(entry);
        _library.Save();
        return true;
    }

    /// <summary>还没有本地文件的条目，供后台补齐（不要跨线程碰 State）。</summary>
    public List<WallpaperEntry> PendingDownloads()
    {
        return State.Entries
            .Where(e => !_library.HasFile(e))
            .OrderByDescending(e => e.Date, StringComparer.Ordinal)
            .ToList();
    }

    public async Task DownloadAllAsync(List<WallpaperEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!await _library.EnsureFileAsync(entry))
            {
                break; // 多半是断网，剩下的下一轮再说
            }
        }
    }

    private void Apply(WallpaperEntry entry)
    {
        Wallpaper.Set(_library.PathOf(entry));
        State.CurrentIndex = State.Entries.IndexOf(entry);
    }

    private void Merge(IEnumerable<BingImage> images)
    {
        foreach (var image in images)
        {
            var entry = State.Entries.FirstOrDefault(e => e.Key == image.Key)
                        ?? State.Entries.FirstOrDefault(e => e.Date == image.Date);

            if (entry is null)
            {
                entry = new WallpaperEntry
                {
                    Date = image.Date,
                    Key = image.Key,
                    File = Library.FileNameFor(image),
                };
                State.Entries.Add(entry);
            }

            entry.Date = image.Date;
            entry.Title = image.Title;
            entry.Copyright = image.Copyright;
            entry.Url = image.Url;

            if (string.IsNullOrEmpty(entry.File))
            {
                entry.File = Library.FileNameFor(image);
            }
        }

        State.Entries.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
    }

    private static string Today => DateTime.Now.ToString("yyyyMMdd");

    private static Size DetectScreenSize()
    {
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            return new Size(Math.Max(bounds.Width, 1920), Math.Max(bounds.Height, 1080));
        }
        catch
        {
            return new Size(1920, 1080);
        }
    }
}
