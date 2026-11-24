using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NcmNative;

/// <summary>
/// 内嵌浏览器登录。
/// 网易对裸 HTTP 客户端的登录请求会返回 8821「安全环境风险」并拒绝下发 MUSIC_U，
/// 所以登录这一步交给真实的 WebView2 完成——它带着完整的浏览器指纹和网易自己的前端
/// 逻辑去走扫码/验证码，登录成功后从 CookieManager 取出会话交给播放器。
/// 窗口只为登录而存在，登录完立即销毁，不占用常驻内存。
/// </summary>
public sealed class LoginWebForm : Form
{
    private readonly NcmApi _api;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 900 };
    private readonly Label _status;
    private bool _busy;
    private bool _finished;

    public string LoggedNickname { get; private set; }

    public LoginWebForm(NcmApi api)
    {
        _api = api;

        Text = "登录网易云 —— 完成后自动关闭";
        ClientSize = new Size(720, 830);
        MinimumSize = new Size(560, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.FontBase;
        TitleBar.UseLight(this);

        _status = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = "正在打开网易云登录页…",
            ForeColor = Theme.Dim,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Panel,
        };

        Controls.Add(_web);
        Controls.Add(_status);

        Load += async (_, _) => await InitAsync();
        FormClosed += (_, _) => _poll.Stop();
    }

    private async Task InitAsync()
    {
        try
        {
            var ud = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NcmLite", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(null, ud,
                new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                        "--disable-features=msEdgeSidebarV2,msEdgeAutofill,msEdgeShoppingAssistant"
                });

            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            // 每次打开都从干净的未登录状态开始，避免残留别的账号
            try { core.CookieManager.DeleteAllCookies(); } catch { }

            core.NavigationStarting += (_, _) => _status.Text = "在下方页面完成扫码或手机号登录…";

            core.Navigate("https://music.163.com/login?target=/discover");
            _status.Text = "在下方页面完成扫码或手机号登录，成功后自动关闭";
            _poll.Tick += async (_, _) => await CheckLoginAsync();
            _poll.Start();
        }
        catch (Exception ex)
        {
            _status.Text = "浏览器组件启动失败：" + ex.Message;
            Log.W("[loginweb] init failed: " + ex.Message);
        }
    }

    private int _probeCount;

    private async Task CheckLoginAsync()
    {
        if (_busy || _finished) return;
        _busy = true;
        try
        {
            var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://music.163.com");
            if (_probeCount < 3)
            {
                _probeCount++;
                Log.W("[loginweb] cookies(" + cookies.Count + "): " +
                      string.Join(",", cookies.Select(c => c.Name)));
            }
            var musicU = cookies.FirstOrDefault(c => c.Name == "MUSIC_U");
            if (musicU == null || string.IsNullOrEmpty(musicU.Value))
            {
                _busy = false;
                return;
            }

            // 拿到 MUSIC_U 说明浏览器里登录已完成，把整份会话搬过来
            _finished = true;
            _poll.Stop();
            _status.Text = "检测到登录凭证，正在校验…";
            Log.W("[loginweb] 抓到 MUSIC_U，共 " + cookies.Count + " 个 cookie");

            var raw = string.Join("; ", cookies
                .Where(c => !string.IsNullOrEmpty(c.Value))
                .Select(c => $"{c.Name}={c.Value}"));

            bool ok = await _api.ImportCookieAsync(raw);
            var nick = ok ? await _api.NicknameAsync() : null;

            if (!string.IsNullOrEmpty(nick))
            {
                _api.SaveCookie();
                LoggedNickname = nick;
                Log.W("[loginweb] 登录成功: " + nick);
                _status.Text = "登录成功：" + nick;
                await Task.Delay(300);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                Log.W("[loginweb] 有 MUSIC_U 但 account 为空");
                _status.Text = "拿到凭证但账号校验失败，请在页面里重新登录";
                _finished = false;
                _poll.Start();
            }
        }
        catch (Exception ex)
        {
            Log.W("[loginweb] poll error: " + ex.Message);
        }
        finally
        {
            if (!_finished) _busy = false;
        }
    }
}
