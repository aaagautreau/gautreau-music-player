using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace NcmNative;

/// <summary>
/// 从本机已安装并登录的网易云客户端里取出会话。
///
/// 原理：客户端是 CEF（Chromium 内核），cookie 用 Chromium 标准的 v10 格式存
/// 在 SQLite 里，AES-256-GCM 加密；密钥放在 LocalPrefs.json 的 os_crypt.encrypted_key，
/// 由 Windows DPAPI 以当前用户身份保护。所以本机当前用户可以直接解开，不需要碰
/// cloudmusic.dll 里的任何加密算法。
///
/// 好处是这套 cookie 带着 clientSign / deviceId / os / appver 完整客户端指纹，
/// 用它请求接口等同于客户端本人在请求，音质档位也是全开的。
/// </summary>
public static class ClientSession
{
    private static readonly string Base =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netease", "CloudMusic");

    public sealed class Result
    {
        public bool Ok;
        public string Cookie = "";
        public string Reason = "";
        public int Count;
    }

    public static Result TryExtract()
    {
        var r = new Result();
        var profiles = FindProfiles();
        if (profiles.Count == 0)
        {
            r.Reason = "未找到客户端数据目录";
            return r;
        }

        string lastReason = "客户端没有登录数据";
        foreach (var profile in profiles)
        {
            try
            {
                var prefsPath = Path.Combine(profile, "LocalPrefs.json");
                var cookiesPath = Path.Combine(profile, "Cookies");
                if (!File.Exists(prefsPath) || !File.Exists(cookiesPath))
                {
                    lastReason = "profile 缺少文件: " + profile;
                    continue;
                }

                var key = ReadKey(prefsPath);
                if (key == null || key.Length != 32)
                {
                    lastReason = "密钥读取失败: " + profile;
                    continue;
                }

                var jar = ReadCookies(cookiesPath, key);
                if (!jar.TryGetValue("MUSIC_U", out var musicU) || string.IsNullOrEmpty(musicU))
                {
                    lastReason = $"该 profile 无 MUSIC_U（读到 {jar.Count} 个 cookie）: " + Path.GetFileName(profile);
                    Log.W("[client] " + lastReason);
                    continue;
                }

                r.Cookie = string.Join("; ", jar.Where(kv => !string.IsNullOrEmpty(kv.Value))
                                                .Select(kv => $"{kv.Key}={kv.Value}"));
                r.Count = jar.Count;
                r.Ok = true;
                Log.W($"[client] 使用 profile: {Path.GetFileName(profile)}（{jar.Count} 个 cookie）");
                return r;
            }
            catch (Exception ex)
            {
                lastReason = ex.Message;
                Log.W("[client] profile 解析失败 " + profile + " : " + ex.Message);
            }
        }

        r.Reason = lastReason;
        return r;
    }

    /// <summary>客户端可能有多个 profile 目录，按 Cookies 的写入时间倒序全部返回</summary>
    private static List<string> FindProfiles()
    {
        var list = new List<(string dir, DateTime t)>();
        if (!Directory.Exists(Base)) return new List<string>();
        foreach (var dir in Directory.GetDirectories(Base))
        {
            var c = Path.Combine(dir, "Cookies");
            var p = Path.Combine(dir, "LocalPrefs.json");
            if (!File.Exists(c) || !File.Exists(p)) continue;
            list.Add((dir, File.GetLastWriteTimeUtc(c)));
        }
        return list.OrderByDescending(x => x.t).Select(x => x.dir).ToList();
    }

    private static byte[] ReadKey(string prefsPath)
    {
        var json = JsonNode.Parse(File.ReadAllText(prefsPath));
        var b64 = json?["os_crypt"]?["encrypted_key"]?.GetValue<string>();
        if (string.IsNullOrEmpty(b64)) return null;

        var blob = Convert.FromBase64String(b64);
        // 前 5 字节是 "DPAPI" 标记
        if (blob.Length <= 5 || Encoding.ASCII.GetString(blob, 0, 5) != "DPAPI") return null;
        return ProtectedData.Unprotect(blob[5..], null, DataProtectionScope.CurrentUser);
    }

    private static Dictionary<string, string> ReadCookies(string dbPath, byte[] key)
    {
        var jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 复制一份再读，避免占用客户端正在使用的文件
        var tmp = Path.Combine(Path.GetTempPath(), "ncm_cookies_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(dbPath, tmp, true);

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = tmp,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var conn = new SqliteConnection(builder.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, encrypted_value, value FROM cookies";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var name = rd.GetString(0);
                var enc = rd.IsDBNull(1) ? null : (byte[])rd["encrypted_value"];
                var plain = rd.IsDBNull(2) ? null : rd.GetString(2);

                string val = null;
                if (!string.IsNullOrEmpty(plain)) val = plain;
                else if (enc != null && enc.Length > 15) val = DecryptV10(enc, key);

                if (!string.IsNullOrEmpty(val)) jar[name] = val;   // 同名取最后一个，与浏览器行为一致
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
        return jar;
    }

    private static string DecryptV10(byte[] enc, byte[] key)
    {
        try
        {
            // 格式: "v10" + 12 字节 nonce + 密文 + 16 字节 tag
            if (enc[0] != (byte)'v' || enc[1] != (byte)'1' || enc[2] != (byte)'0') return null;
            var nonce = enc.AsSpan(3, 12);
            var cipher = enc.AsSpan(15);
            var plain = new byte[cipher.Length - 16];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}
