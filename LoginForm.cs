using QRCoder;

namespace NcmNative;

public sealed class LoginForm : Form
{
    private readonly NcmApi _api;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1800 };
    private string _unikey;

    private readonly Panel _paneQr = new() { Dock = DockStyle.Fill, Visible = true };
    private readonly Panel _paneSms = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel _paneCookie = new() { Dock = DockStyle.Fill, Visible = false };
    private FlatBtn _tabQr, _tabSms, _tabCookie;

    private PictureBox _qrBox;
    private Label _qrState;
    private TextBox _phone, _code;
    private Label _smsState;
    private TextBox _cookieBox;
    private Label _cookieState;

    public string LoggedNickname { get; private set; }

    public LoginForm(NcmApi api)
    {
        _api = api;
        Text = "登录网易云";
        ClientSize = new Size(392, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.FontBase;

        var head = new Label
        {
            Text = "登录网易云账号",
            Font = Theme.FontTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(22, 16),
        };
        Controls.Add(head);

        _tabQr = MakeTab("扫码", 22);
        _tabSms = MakeTab("手机号", 100);
        _tabCookie = MakeTab("Cookie", 178);
        _tabQr.NormalColor = Theme.Panel2;

        var body = new Panel { Location = new Point(20, 96), Size = new Size(352, 380), BackColor = Theme.Panel };
        Controls.Add(body);

        BuildQrPane();
        BuildSmsPane();
        BuildCookiePane();
        body.Controls.AddRange(new Control[] { _paneQr, _paneSms, _paneCookie });

        _poll.Tick += async (_, _) => await PollAsync();
        FormClosed += (_, _) => _poll.Stop();
        Shown += async (_, _) => await NewQrAsync();
    }

    private FlatBtn MakeTab(string text, int x)
    {
        var b = new FlatBtn(text) { Location = new Point(x, 52), Size = new Size(70, 30) };
        b.NormalColor = Theme.Panel2;
        b.Click += (_, _) =>
        {
            _paneQr.Visible = b == _tabQr;
            _paneSms.Visible = b == _tabSms;
            _paneCookie.Visible = b == _tabCookie;
            _tabQr.NormalColor = b == _tabQr ? Theme.Accent : Theme.Panel2;
            _tabSms.NormalColor = b == _tabSms ? Theme.Accent : Theme.Panel2;
            _tabCookie.NormalColor = b == _tabCookie ? Theme.Accent : Theme.Panel2;
            _tabQr.Invalidate(); _tabSms.Invalidate(); _tabCookie.Invalidate();
            if (b == _tabQr) _ = NewQrAsync();
            else _poll.Stop();
        };
        Controls.Add(b);
        return b;
    }

    private void BuildQrPane()
    {
        _qrBox = new PictureBox
        {
            Location = new Point(66, 6),
            Size = new Size(220, 220),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
        };
        _qrState = new Label
        {
            Text = "正在获取二维码…",
            ForeColor = Theme.Dim,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 236),
            Size = new Size(352, 24),
        };
        var tip = new Label
        {
            Text = "用网易云音乐 App 扫码，手机确认后自动登录",
            ForeColor = Color.FromArgb(94, 98, 112),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 264),
            Size = new Size(352, 22),
        };
        _paneQr.Controls.AddRange(new Control[] { _qrBox, _qrState, tip });
    }

    private void BuildSmsPane()
    {
        _phone = MakeInput("手机号", new Point(0, 40), 352);
        _code = MakeInput("验证码", new Point(0, 96), 210);

        var send = new FlatBtn("发送") { Location = new Point(222, 96), Size = new Size(130, 34) };
        send.Click += async (_, _) =>
        {
            var p = _phone.Text.Trim();
            if (p.Length != 11) { _smsState.Text = "手机号格式不对"; return; }
            send.Enabled = false;
            _smsState.Text = "发送中…";
            var r = await _api.SmsSentAsync(p);
            var code = r?["code"]?.GetValue<int>() ?? -1;
            _smsState.Text = code == 200 ? "验证码已发送" : $"发送失败：{r?["message"] ?? r?["msg"] ?? code.ToString()}";
            await Task.Delay(3000);
            send.Enabled = true;
        };

        var login = new FlatBtn("登录") { Location = new Point(0, 152), Size = new Size(352, 36) };
        login.NormalColor = Theme.Accent;
        login.Click += async (_, _) =>
        {
            var p = _phone.Text.Trim();
            var c = _code.Text.Trim();
            if (p.Length != 11 || c.Length < 4) { _smsState.Text = "手机号或验证码不完整"; return; }
            _smsState.Text = "登录中…";
            Log.W($"=== 验证码登录 phone={p} ===");
            var r = await _api.SmsVerifyAsync(p, c);
            Log.W("verify 返回: " + (r?.ToJsonString() ?? "null") + " | jar=[" + _api.CookieKeys() + "]");
            var code = r?["code"]?.GetValue<int>() ?? 0;
            if (code != 200)
            {
                _smsState.Text = "登录失败：" + (r?["message"]?.GetValue<string>() ?? r?["msg"]?.GetValue<string>() ?? code.ToString());
                return;
            }
            var nick = await _api.NicknameAsync();
            if (!string.IsNullOrEmpty(nick))
            {
                _api.SaveCookie();
                LoggedNickname = nick;
                Log.W("验证码登录成功: " + nick);
                _smsState.Text = "登录成功：" + nick;
                await Task.Delay(400);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _smsState.Text = "验证通过但取不到账号信息";
                Log.W("!!! verify 200 但 account 为空");
            }
        };

        _smsState = new Label
        {
            Text = "",
            ForeColor = Theme.Dim,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 200),
            Size = new Size(352, 24),
        };

        _paneSms.Controls.AddRange(new Control[] { _phone, _code, send, login, _smsState });
    }

    private TextBox MakeInput(string placeholder, Point loc, int width)
    {
        var tb = new TextBox
        {
            Location = loc,
            Width = width,
            Height = 34,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel2,
            ForeColor = Color.FromArgb(120, 124, 138),
            Font = Theme.FontBase,
            Text = placeholder,
        };
        tb.GotFocus += (_, _) =>
        {
            if (tb.Text == placeholder) { tb.Text = ""; tb.ForeColor = Theme.Text; }
        };
        tb.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(tb.Text)) { tb.Text = placeholder; tb.ForeColor = Color.FromArgb(120, 124, 138); }
        };
        return tb;
    }

    private void BuildCookiePane()
    {
        var tip = new Label
        {
            Text = "浏览器登录 music.163.com，F12 → Network → 任意请求\n复制完整 Cookie，粘贴到下面",
            ForeColor = Theme.Dim,
            Location = new Point(0, 8),
            Size = new Size(352, 44),
        };
        _cookieBox = new TextBox
        {
            Multiline = true,
            Location = new Point(0, 58),
            Size = new Size(352, 150),
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
        };
        var go = new FlatBtn("导入并验证") { Location = new Point(0, 218), Size = new Size(352, 36) };
        go.NormalColor = Theme.Accent;
        go.Click += async (_, _) =>
        {
            var raw = _cookieBox.Text.Trim();
            if (raw.Length < 10) { _cookieState.Text = "内容太短"; return; }
            _cookieState.Text = "验证中…";
            var ok = await _api.ImportCookieAsync(raw);
            if (ok)
            {
                _api.SaveCookie();
                LoggedNickname = await _api.NicknameAsync();
                DialogResult = DialogResult.OK;
                Close();
            }
            else _cookieState.Text = "Cookie 无效，确认含 MUSIC_U 且未过期";
        };
        _cookieState = new Label
        {
            Text = "",
            ForeColor = Theme.Dim,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 262),
            Size = new Size(352, 24),
        };
        _paneCookie.Controls.AddRange(new Control[] { tip, _cookieBox, go, _cookieState });
    }

    private static Image QrImage(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(8);
        using var ms = new MemoryStream(bytes);
        return Image.FromStream(ms);
    }

    private async Task NewQrAsync()
    {
        _poll.Stop();
        Log.W("========= 二维码流程开始 =========");
        Log.W("初始 jar=[" + _api.CookieKeys() + "]");
        _qrState.Text = "正在获取二维码…";
        _unikey = await _api.QrCreateAsync();
        Log.W("unikey=" + (_unikey ?? "NULL"));
        if (string.IsNullOrEmpty(_unikey))
        {
            _qrState.Text = "获取二维码失败，检查网络后点「扫码」重试";
            return;
        }
        var img = QrImage($"https://music.163.com/login?codekey={_unikey}");
        _qrBox.Image?.Dispose();
        _qrBox.Image = img;
        _qrState.Text = "等待扫码…";
        _poll.Start();
    }

    private async Task PollAsync()
    {
        if (string.IsNullOrEmpty(_unikey)) return;
        var code = await _api.QrCheckAsync(_unikey);
        Log.W($"poll code={code} jar=[{_api.CookieKeys()}]");
        switch (code)
        {
            case 800:
                Log.W(">>> 二维码过期，自动换新");
                _qrState.Text = "二维码已过期，正在自动刷新…";
                await NewQrAsync();
                break;
            case 801: _qrState.Text = "等待扫码…"; break;
            case 802: _qrState.Text = "已扫码，请在手机上点确认"; break;
            case 803:
                _poll.Stop();
                Log.W(">>> 803 授权成功，开始验证账号");
                Log.W("803 后 jar=[" + _api.CookieKeys() + "] hasMusicU=" + _api.HasMusicU);
                var acc = await _api.AccountAsync();
                Log.W("account 原始: " + (acc?.ToJsonString() is string js ? (js.Length > 400 ? js[..400] : js) : "null"));
                var nick = acc?["profile"]?["nickname"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(nick))
                {
                    _api.SaveCookie();
                    LoggedNickname = nick;
                    _qrState.Text = "登录成功：" + nick;
                    await Task.Delay(500);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    _qrState.Text = "授权成功但未拿到票据，改用「手机号」登录一次";
                    Log.W("!!! 803 但取不到 profile，结束诊断");
                }
                break;
        }
    }
}
