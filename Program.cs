using System.Threading;

namespace NcmNative;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.RunAsync().GetAwaiter().GetResult();
            return;
        }

        using var mutex = new Mutex(true, "NcmNative.SingleInstance.v1", out bool created);
        if (!created) return;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, "UI");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, "AppDomain");

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void Report(Exception ex, string source)
    {
        Log.W($"!!! 未处理异常 [{source}]");
        Log.W(ex?.ToString() ?? "unknown");
        MessageBox.Show(
            "程序遇到一个问题，但不会退出：\n\n" +
            (ex?.Message ?? "未知错误") +
            "\n\n完整堆栈已写入日志：\n" + Log.FilePath,
            "云村 Lite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
