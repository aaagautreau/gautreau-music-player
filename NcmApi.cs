using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NcmNative;

/// <summary>
/// 网易云 /api/* 明文接口客户端。
/// 用 CookieContainer 自动保持会话——扫码轮询过程中的临时 cookie 不会丢，
/// 这是扫码授权成功却拿不到 MUSIC_U 的常见原因。
/// </summary>
public sealed class NcmApi
{
    private const string Base = "https://music.163.com";
    private const string Ua =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/122.0.0.0 Safari/537.36";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NcmLite");

    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly string _cookieFile = Path.Combine(DataDir, "cookie.txt");

    public NcmApi()
    {
        Directory.CreateDirectory(DataDir);
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Ua);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        LoadCookie();
    }

    /* ---------------- cookie 持久化 ---------------- */

    public void SaveCookie()
    {
        try
        {
            var list = _cookies.GetCookies(new Uri(Base));
            var parts = list.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}");
            File.WriteAllText(_cookieFile, string.Join("; ", parts));
        }
        catch { }
    }

    private void LoadCookie()
    {
        try
        {
            if (!File.Exists(_cookieFile)) return;
            var raw = File.ReadAllText(_cookieFile).Trim();
            if (raw.Length == 0) return;
            foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = pair.IndexOf('=');
                if (i <= 0) continue;
                _cookies.Add(new Uri(Base), new Cookie(pair[..i].Trim(), pair[(i + 1)..].Trim()));
            }
        }
        catch { }
    }

    /// <summary>导入浏览器复制的 Cookie 串，返回是否登录成功</summary>
    public async Task<bool> ImportCookieAsync(string raw)
    {
        foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i <= 0) continue;
            try { _cookies.Add(new Uri(Base), new Cookie(pair[..i].Trim(), pair[(i + 1)..].Trim())); } catch { }
        }
        return await AccountAsync() != null;
    }

    public void ClearCookie()
    {
        _cookies.GetCookies(new Uri(Base)).Cast<Cookie>().ToList().ForEach(c => c.Expired = true);
        try { File.Delete(_cookieFile); } catch { }
    }

    public bool HasMusicU => _cookies.GetCookies(new Uri(Base))["MUSIC_U"] != null;

    /* ---------------- 底层请求 ---------------- */

    private static string Qs(Dictionary<string, string> q)
    {
        if (q == null || q.Count == 0) return "";
        return "?" + string.Join("&", q.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value ?? "")));
    }

    private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");

    /// <summary>
    /// 容错解析。
    /// JsonNode.Parse 是惰性的：解析阶段不建属性字典，直到第一次 GetItem 才构建，
    /// 那时撞到重复键才抛 ArgumentException，所以必须在返回前把整棵树强制物化，
    /// 否则异常会从业务代码里冒出来。
    /// </summary>
    private static JsonNode SafeParse(string txt, string ctx)
    {
        if (string.IsNullOrWhiteSpace(txt)) return null;

        try
        {
            var node = JsonNode.Parse(txt);
            Materialize(node);
            return node;
        }
        catch (Exception ex)
        {
            Log.W($"[json] {ctx} 直接解析失败（{ex.GetType().Name}: {ex.Message}），改用 JsonDocument 重建");
        }

        try
        {
            using var doc = JsonDocument.Parse(txt, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return ToNode(doc.RootElement);
        }
        catch (Exception ex)
        {
            Log.W($"[json] {ctx} 重建仍失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>递归访问每个属性，逼 JsonObject 提前建字典</summary>
    private static void Materialize(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var kv in obj)
                if (kv.Value != null) Materialize(kv.Value);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                if (item != null) Materialize(item);
        }
    }

    private static JsonNode ToNode(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var p in e.EnumerateObject())
                    obj[p.Name] = ToNode(p.Value);   // 索引器赋值 = 覆盖，不会抛重复键
                return obj;
            case JsonValueKind.Array:
                var arr = new JsonArray();
                foreach (var x in e.EnumerateArray()) arr.Add(ToNode(x));
                return arr;
            case JsonValueKind.String:
                return JsonValue.Create(e.GetString());
            case JsonValueKind.Number:
                if (e.TryGetInt64(out var l)) return JsonValue.Create(l);
                if (e.TryGetDouble(out var d)) return JsonValue.Create(d);
                return null;
            case JsonValueKind.True: return JsonValue.Create(true);
            case JsonValueKind.False: return JsonValue.Create(false);
            default: return null;
        }
    }

    /// <summary>当前 cookie 容器里的键，用于日志排查</summary>
    public string CookieKeys()
    {
        try
        {
            var list = _cookies.GetCookies(new Uri(Base));
            return string.Join(",", list.Cast<Cookie>().Select(c => c.Name));
        }
        catch { return "?"; }
    }

    public bool HasCookie(string name)
    {
        try { return _cookies.GetCookies(new Uri(Base))[name] != null; } catch { return false; }
    }

    public async Task<JsonNode> GetAsync(string path, Dictionary<string, string> q = null)
    {
        var full = path + Qs(q);
        try
        {
            using var res = await _http.GetAsync(Base + full);
            var txt = await res.Content.ReadAsStringAsync();
            string sc = res.Headers.TryGetValues("Set-Cookie", out var vals)
                ? string.Join(" || ", vals)
                : "";
            Log.W($"GET {Trunc(full, 140)} -> {(int)res.StatusCode} :: {Trunc(txt, 260)}");
            if (sc.Length > 0)
            {
                Log.W($"    Set-Cookie: {Trunc(sc, 500)}");
                Log.W($"    jar=[{CookieKeys()}]");
            }
            return SafeParse(txt, path);
        }
        catch (Exception ex)
        {
            Log.W($"GET {path} FAILED: {ex.Message}");
            return null;
        }
    }

    private async Task<JsonNode> PostAsync(string path, Dictionary<string, string> q = null)
    {
        try
        {
            var body = new FormUrlEncodedContent(q ?? new Dictionary<string, string>());
            var res = await _http.PostAsync(Base + path, body);
            var txt = await res.Content.ReadAsStringAsync();
            Log.W($"POST {path} -> {(int)res.StatusCode} :: {Trunc(txt, 200)}");
            return SafeParse(txt, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[api] {path} failed: {ex.Message}");
            return null;
        }
    }

    /* ---------------- 登录 ---------------- */

    public async Task<string> QrCreateAsync()
    {
        var r = await GetAsync("/api/login/qrcode/unikey", new() { ["type"] = "3" });
        return (r?["unikey"]).AsStr();
    }

    /// <returns>800 过期 / 801 待扫 / 802 待确认 / 803 成功</returns>
    public async Task<int> QrCheckAsync(string key)
    {
        var r = await GetAsync("/api/login/qrcode/client/login", new() { ["key"] = key, ["type"] = "3" });
        return (r?["code"]).AsInt();
    }

    public async Task<JsonNode> AccountAsync() => await GetAsync("/api/nuser/account/get");

    public async Task<string> NicknameAsync()
    {
        var a = await AccountAsync();
        var nick = (a?["profile"]?["nickname"]).AsStr();
        return string.IsNullOrEmpty(nick) ? null : nick;
    }

    public async Task<JsonNode> SmsSentAsync(string phone)
        => await GetAsync("/api/sms/captcha/sent", new() { ["cellphone"] = phone, ["ctcode"] = "86" });

    /// <summary>验证码登录：参数名必须是 cellphone，用 phone 会返回「参数错误」</summary>
    public async Task<JsonNode> SmsVerifyAsync(string phone, string code)
        => await GetAsync("/api/sms/captcha/verify", new() { ["cellphone"] = phone, ["captcha"] = code, ["ctcode"] = "86" });

    public void Logout()
    {
        ClearCookie();
        _ = GetAsync("/api/logout");
    }

    /* ---------------- 数据 ---------------- */

    public async Task<List<Song>> DailySongsAsync()
    {
        var r = await GetAsync("/api/v3/discovery/recommend/songs", new() { ["limit"] = "30", ["offset"] = "0" });
        return ParseSongs(r?["data"]?["dailySongs"]);
    }

    public async Task<List<Playlist>> DailyPlaylistsAsync()
    {
        var r = await GetAsync("/api/v1/discovery/recommend/resource");
        var outList = new List<Playlist>();
        if (r?["recommend"] is JsonArray arr)
            foreach (var x in arr)
                outList.Add(new Playlist
                {
                    Id = (x?["id"]).AsLong(),
                    Name = (x?["name"]).AsStr(),
                    Cover = (x?["picUrl"]).AsStr(),
                    PlayCount = (x?["playCount"]).AsLong(),
                });
        return outList;
    }

    public async Task<List<Playlist>> MyPlaylistsAsync()
    {
        var a = await AccountAsync();
        var uid = (a?["profile"]?["userId"]).AsLong();
        if (uid == 0) return new List<Playlist>();
        var r = await GetAsync("/api/user/playlist", new() { ["uid"] = uid.ToString(), ["limit"] = "1000", ["offset"] = "0" });
        var outList = new List<Playlist>();
        if (r?["playlist"] is JsonArray arr)
            foreach (var x in arr)
                outList.Add(new Playlist
                {
                    Id = (x?["id"]).AsLong(),
                    Name = (x?["name"]).AsStr(),
                    Cover = (x?["coverImgUrl"]).AsStr(),
                    Count = (x?["trackCount"]).AsInt(),
                });
        return outList;
    }

    public async Task<List<Song>> PlaylistTracksAsync(long id)
    {
        var r = await GetAsync("/api/v6/playlist/detail", new() { ["id"] = id.ToString(), ["n"] = "1000" });
        return ParseSongs(r?["playlist"]?["tracks"], (r?["playlist"]?["name"]).AsStr());
    }

    public async Task<List<Song>> SearchAsync(string kw)
    {
        var r = await GetAsync("/api/cloudsearch/pc", new() { ["s"] = kw, ["type"] = "1", ["limit"] = "30", ["offset"] = "0" });
        return ParseSongs(r?["result"]?["songs"]);
    }

    public async Task<List<Song>> NewsongAsync()
    {
        var r = await GetAsync("/api/personalized/newsong", new() { ["type"] = "recommend" });
        var outList = new List<Song>();
        if (r?["result"] is JsonArray arr)
            foreach (var x in arr)
            {
                var s = Song.FromJson(x?["song"] ?? x);
                if (s != null && s.Id != 0) outList.Add(s);
            }
        return outList;
    }

    private static List<Song> ParseSongs(JsonNode node, string _ = null)
    {
        var outList = new List<Song>();
        if (node is JsonArray arr)
            foreach (var x in arr)
            {
                var s = Song.FromJson(x);
                if (s != null && s.Id != 0) outList.Add(s);
            }
        return outList;
    }

    /* ---------------- 播放 ---------------- */

    /// <summary>level: standard | higher | exhigh | lossless | hires | jymaster</summary>
    public async Task<(string url, int fee, int code)> SongUrlAsync(long id, string level)
    {
        var r = await GetAsync("/api/song/enhance/player/url/v1",
            new() { ["ids"] = $"[{id}]", ["level"] = level, ["encodeType"] = "flac" });
        var item = (r?["data"] as JsonArray)?.FirstOrDefault();
        return (
            (item?["url"]).AsStr(),
            (item?["fee"]).AsInt(),
            (item?["code"]).AsInt()
        );
    }

    public async Task<(string lrc, string tlrc)> LyricAsync(long id)
    {
        var r = await GetAsync("/api/song/lyric", new() { ["id"] = id.ToString(), ["lv"] = "-1", ["kv"] = "-1", ["tv"] = "-1" });
        return (
            (r?["lrc"]?["lyric"]).AsStr(),
            (r?["tlyric"]?["lyric"]).AsStr()
        );
    }
}
