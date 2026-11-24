namespace NcmNative;

/// <summary>
/// 自检：把所有数据接口和解析路径跑一遍。
/// 用 `云村Lite.exe --selftest` 触发，结果写到 %LOCALAPPDATA%\NcmLite\selftest.txt。
/// </summary>
public static class SelfTest
{
    public static async Task RunAsync()
    {
        var api = new NcmApi();
        var lines = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        void Ok(string name, string detail = "") =>
            lines.Add($"OK    {name,-22} {detail}");
        void Fail(string name, string err) =>
            lines.Add($"FAIL  {name,-22} {err}");

        // 先尝试复用本机客户端会话
        var sess = ClientSession.TryExtract();
        if (sess.Ok)
        {
            Ok("client-session", $"{sess.Count} cookies");
            await api.ImportCookieAsync(sess.Cookie);
        }
        else
        {
            Fail("client-session", sess.Reason);
        }

        try
        {
            var acc = await api.AccountAsync();
            var nick = acc?["profile"]?["nickname"]?.GetValue<string>();
            if (nick == null) Fail("account", "profile 为空");
            else Ok("account", nick + " uid=" + acc?["profile"]?["userId"]);
        }
        catch (Exception ex) { Fail("account", ex.Message); }

        try
        {
            var s = await api.DailySongsAsync();
            if (s.Count == 0) Fail("dailySongs", "0 首");
            else Ok("dailySongs", s.Count + " 首 | " + s[0].Name);
        }
        catch (Exception ex) { Fail("dailySongs", ex.Message); }

        try
        {
            var p = await api.DailyPlaylistsAsync();
            Ok("dailyPlaylists", p.Count + " 个");
        }
        catch (Exception ex) { Fail("dailyPlaylists", ex.Message); }

        try
        {
            var p = await api.MyPlaylistsAsync();
            Ok("myPlaylists", p.Count + " 个" + (p.Count > 0 ? " | " + p[0].Name : ""));
        }
        catch (Exception ex) { Fail("myPlaylists", ex.Message); }

        foreach (var id in new long[] { 3778678, 2884035, 19723756, 3779629 })
        {
            try
            {
                var t = await api.PlaylistTracksAsync(id);
                Ok("playlist" + id, t.Count + " 首");
            }
            catch (Exception ex) { Fail("playlist" + id, ex.Message); }
        }

        try
        {
            var r = await api.SearchAsync("周杰伦");
            Ok("search", r.Count + " 首 | " + (r.Count > 0 ? r[0].Name : ""));
        }
        catch (Exception ex) { Fail("search", ex.Message); }

        try
        {
            var n = await api.NewsongAsync();
            Ok("newsong", n.Count + " 首");
        }
        catch (Exception ex) { Fail("newsong", ex.Message); }

        try
        {
            var s = await api.DailySongsAsync();
            var target = s.FirstOrDefault(x => x.Fee == 1) ?? s.FirstOrDefault();
            if (target == null) Fail("songUrl", "没有可测歌曲");
            else
            {
                foreach (var lv in new[] { "standard", "exhigh", "lossless", "hires", "jyeffect" })
                {
                    var (url, fee, code) = await api.SongUrlAsync(target.Id, lv);
                    if (string.IsNullOrEmpty(url)) Fail("songUrl/" + lv, $"null (code={code})");
                    else Ok("songUrl/" + lv, target.Name);
                }
            }
        }
        catch (Exception ex) { Fail("songUrl", ex.Message); }

        try
        {
            var s = await api.DailySongsAsync();
            var (lrc, tlrc) = await api.LyricAsync(s[0].Id);
            Ok("lyric", lrc.Length + " 字符");
        }
        catch (Exception ex) { Fail("lyric", ex.Message); }

        sw.Stop();
        lines.Add("");
        lines.Add($"耗时 {sw.ElapsedMilliseconds} ms，日志 {Log.FilePath}");

        var outFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NcmLite", "selftest.txt");
        File.WriteAllLines(outFile, lines);
        Log.W("self-test done");
    }
}
