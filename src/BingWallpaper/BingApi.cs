using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingWallpaper;

/// <summary>必应每日一图。</summary>
internal sealed class BingImage
{
    /// <summary>这张图在本地日历上生效的那天（yyyyMMdd，取自接口 enddate）。</summary>
    public string Date { get; init; } = "";

    /// <summary>图片稳定标识（urlbase），用于判断"今天还是不是同一张图"。</summary>
    public string Key { get; init; } = "";

    public string Title { get; init; } = "";
    public string Copyright { get; init; } = "";
    public string Url { get; init; } = "";
}

internal static class BingApi
{
    private const string Endpoint = "https://www.bing.com/HPImageArchive.aspx";
    private const string Market = "zh-CN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>取最近 count 张（新 → 旧）。</summary>
    public static async Task<List<BingImage>> FetchAsync(int count, Size size)
    {
        // uhd=1 + 屏幕尺寸：直接拿到最合适的 UHD 图（2560x1440 约 1.2 MB，而不是 4K 的 3.5 MB）
        var url = $"{Endpoint}?format=js&idx=0&n={count}&mkt={Market}" +
                  $"&uhd=1&uhdwidth={size.Width}&uhdheight={size.Height}";

        using var response = await Http.Client.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<Archive>(JsonOptions).ConfigureAwait(false);
        var raw = payload?.Images;
        if (raw is null || raw.Count == 0)
        {
            throw new InvalidOperationException("必应接口没有返回任何图片");
        }

        var images = raw.Select(ToImage).Where(i => i.Key.Length > 0 && i.Url.Length > 0).ToList();
        if (images.Count == 0)
        {
            throw new InvalidOperationException("必应接口返回的数据无法解析");
        }

        return images;
    }

    private static BingImage ToImage(Raw r)
    {
        var key = r.UrlBase ?? "";
        var absolute = r.Url ?? "";

        if (absolute.StartsWith('/'))
        {
            absolute = "https://www.bing.com" + absolute;
        }
        if (absolute.Length == 0 || !absolute.Contains("_UHD", StringComparison.OrdinalIgnoreCase))
        {
            absolute = key.Length > 0 ? "https://www.bing.com" + key + "_UHD.jpg" : "";
        }

        return new BingImage
        {
            Date = r.EndDate ?? "",
            Key = key,
            Title = Decode(r.Title),
            Copyright = Decode(r.Copyright),
            Url = absolute,
        };
    }

    private static string Decode(string? value) => WebUtility.HtmlDecode(value ?? "").Trim();

    private sealed class Archive
    {
        [JsonPropertyName("images")] public List<Raw>? Images { get; set; }
    }

    private sealed class Raw
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("urlbase")] public string? UrlBase { get; set; }
        [JsonPropertyName("enddate")] public string? EndDate { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("copyright")] public string? Copyright { get; set; }
    }
}

/// <summary>全进程共用一个 HttpClient。</summary>
internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BingWallpaper/1.0");
        return client;
    }
}
