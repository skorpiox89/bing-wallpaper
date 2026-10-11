using System.Net;
using System.Text.Json;

namespace BingWallpaper;

internal sealed class WallpaperEntry
{
    /// <summary>yyyyMMdd，这张图生效的那天。</summary>
    public string Date { get; set; } = "";

    /// <summary>urlbase，稳定标识。</summary>
    public string Key { get; set; } = "";

    /// <summary>images/ 下的文件名。</summary>
    public string File { get; set; } = "";

    public string Title { get; set; } = "";
    public string Copyright { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>index.json 的内容，重启后靠它恢复"当前看到第几张"。</summary>
internal sealed class LibraryState
{
    /// <summary>按日期升序：末尾最新。</summary>
    public List<WallpaperEntry> Entries { get; set; } = new();

    public int CurrentIndex { get; set; } = -1;

    /// <summary>上一次"自动换"用的图片标识。</summary>
    public string LastAutoKey { get; set; } = "";

    /// <summary>上一次"自动换"发生在哪一天（yyyyMMdd）。</summary>
    public string LastAutoDate { get; set; } = "";

    /// <summary>是否已经完成过首次运行。</summary>
    public bool Initialized { get; set; }
}

/// <summary>本地图片库：索引的读写 + 图片下载缓存。</summary>
internal sealed class Library
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LibraryState State { get; private set; } = new();

    public void Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            CleanupTempFiles();

            if (!File.Exists(AppPaths.IndexFile))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(AppPaths.IndexFile), JsonOptions);
            if (state is not null)
            {
                State = state;
            }

            Normalize();
            Log.Info($"索引已加载：{State.Entries.Count} 条，当前 #{State.CurrentIndex}");
        }
        catch (Exception ex)
        {
            Log.Error("索引读取失败，已重建", ex);
            State = new LibraryState();
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var tmp = AppPaths.IndexFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(State, JsonOptions));
            File.Move(tmp, AppPaths.IndexFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("索引写入失败", ex);
        }
    }

    public string PathOf(WallpaperEntry entry) => Path.Combine(AppPaths.Images, entry.File);

    public bool HasFile(WallpaperEntry entry)
    {
        try
        {
            var path = PathOf(entry);
            return File.Exists(path) && new FileInfo(path).Length > 1024;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>确保图片已在本地，必要时下载（失败重试 1 次）。</summary>
    public async Task<bool> EnsureFileAsync(WallpaperEntry entry)
    {
        if (HasFile(entry))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(entry.Url))
        {
            Log.Error($"没有下载地址：{entry.Date}");
            return false;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var tmp = PathOf(entry) + "." + Path.GetRandomFileName() + ".tmp";
            try
            {
                AppPaths.EnsureCreated();

                using (var response = await Http.Client
                           .GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead)
                           .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var target = File.Create(tmp);
                    await source.CopyToAsync(target).ConfigureAwait(false);
                }

                var length = new FileInfo(tmp).Length;
                if (length < 1024)
                {
                    throw new IOException($"下载内容过小（{length} 字节）");
                }

                File.Move(tmp, PathOf(entry), overwrite: true);
                Log.Info($"已下载 {entry.Date}（{length / 1024} KB）：{entry.File}");
                return true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // 图片被必应彻底删掉了，标记为失效，下次启动时清理
                Log.Error($"图片已失效：{entry.Date}（{entry.Url}）", ex);
                entry.Url = "";
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"下载失败（第 {attempt} 次）：{entry.Date}", ex);
                TryDelete(tmp);

                if (attempt == 2)
                {
                    return false;
                }

                await Task.Delay(1500).ConfigureAwait(false);
            }
        }

        return false;
    }

    public static string FileNameFor(BingImage image)
    {
        // urlbase 形如 /th?id=OHR.XXX_ZH-CN1234，只取 id= 后面的部分
        var slug = image.Key;
        var marker = slug.LastIndexOf("id=", StringComparison.OrdinalIgnoreCase);
        slug = marker >= 0 ? slug[(marker + 3)..] : slug[(slug.LastIndexOf('/') + 1)..];

        var invalid = Path.GetInvalidFileNameChars();
        slug = new string(slug.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (slug.Length == 0)
        {
            slug = "bing";
        }
        if (slug.Length > 80)
        {
            slug = slug[..80];
        }

        return $"{image.Date}-{slug}.jpg";
    }

    private void Normalize()
    {
        State.Entries = State.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Date) && !string.IsNullOrWhiteSpace(e.Key))
            // 失效图（既没有文件也下不回来）直接清掉
            .Where(e => !string.IsNullOrEmpty(e.Url) || HasFile(e))
            .GroupBy(e => e.Key)
            .Select(g => g.First())
            .OrderBy(e => e.Date, StringComparer.Ordinal)
            .ToList();

        if (State.CurrentIndex < 0 || State.CurrentIndex >= State.Entries.Count)
        {
            State.CurrentIndex = State.Entries.Count - 1;
        }
    }

    private static void CleanupTempFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.Images, "*.tmp"))
            {
                TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Error("清理临时文件失败", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略
        }
    }
}
