using System.Runtime.InteropServices;

namespace NcmNative;

/// <summary>把窗口标题栏切成浅色</summary>
public static class TitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void UseLight(Form f)
    {
        void Apply()
        {
            try
            {
                int off = 0;   // 0 = 浅色标题栏
                if (DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref off, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref off, 4);
            }
            catch { }
        }

        if (f.IsHandleCreated) Apply();
        else f.HandleCreated += (_, _) => Apply();
    }
}
