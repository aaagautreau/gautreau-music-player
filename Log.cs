namespace NcmNative;

/// <summary>轻量落盘日志，用于排查登录链路</summary>
public static class Log
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NcmLite");

    public static readonly string FilePath = Path.Combine(Dir, "app.log");
    private static readonly object Gate = new();

    static Log()
    {
        try { Directory.CreateDirectory(Dir); } catch { }
    }

    public static void W(string msg)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".1", true);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
