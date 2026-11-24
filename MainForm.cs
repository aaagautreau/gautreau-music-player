using System.Drawing.Drawing2D;
using System.Text.Json.Nodes;

namespace NcmNative;

public sealed class MainForm : Form
{
    private readonly NcmApi _api = new();
    private readonly AudioPlayer _player = new();
    private readonly List<Song> _songs = new();
    private readonly List<Playlist> _playlists = new();

    private int _index = -1;
    private string _level = "lossless";
    private string _view = "daily";
    private string _mode = "list";
    private string _nickname;
    private bool _seeking;
    private bool _suppress;
    private List<(TimeSpan t, string txt)> _lrc = new();
    private int _lrcIdx = -1;

    private Panel _side, _body, _footer, _queue;
    private Label _title, _nickText, _nowTitle, _nowArtist, _tCur, _tDur, _toast;
    private PictureBox _cover;
    private SongList _list, _queueList;
    private ProgBar _prog;
    private VolBar _vol;
    private TextBox _search;
    private FlatBtn _btnPlay, _btnPrev, _btnNext, _btnMode, _btnLevel, _btnRefresh, _btnLyric, _btnLogin, _btnGo, _btnQueue;
    private readonly List<NavBtn> _nav = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2400 };
    private LyricsForm _lyrics;

    private static readonly (string key, string label)[] Levels =
    {
        ("standard", "标准 128k"),
        ("higher", "较高 192k"),
        ("exhigh", "极高 320k"),
        ("lossless", "无损 FLAC"),
        ("hires", "Hi-Res 高解析"),
        ("jyeffect", "高清环绕声"),
    };

    public MainForm()
    {
        Text = "gautreau 的播放器";
        ClientSize = new Size(1240, 780);
        MinimumSize = new Size(960, 620);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = MakeIcon();
        TitleBar.UseLight(this);

        BuildSide();
        BuildBody();
        BuildQueue();
        BuildFooter();

        _body.Dock = DockStyle.Fill;
        _footer.Dock = DockStyle.Bottom;
        _queue.Dock = DockStyle.Right;
        Controls.Add(_body);
        Controls.Add(_queue);
        Controls.Add(_footer);
        Controls.Add(_side);

        _tick.Tick += (_, _) => OnTick();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.Visible = false; };
        _player.Finished += () => BeginInvoke(new Action(() => Advance(true)));

        SetNavActive("daily");

        FormClosing += (_, _) => { _tick.Stop(); _player.Dispose(); };

        Shown += async (_, _) =>
        {
            // 只用本程序自己登录的账号（存在 cookie.txt）。
            // 不自动读取客户端会话：本机客户端上登录的可能是别人的号。
            await RefreshUserAsync();
            await LoadDailyAsync();
            if (string.IsNullOrEmpty(_nickname)) await OpenLoginAsync();
            if (Environment.GetCommandLineArgs().Contains("--uitest")) await RunUiTestAsync();
        };
    }

    /* ================= 图标 ================= */

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            Theme.Smooth(g);
            g.Clear(Theme.Bg);
            using var ring = new Pen(Theme.Accent, 5);
            g.DrawEllipse(ring, 12, 12, 40, 40);
            using var dot = new SolidBrush(Theme.Accent);
            g.FillEllipse(dot, 26, 26, 12, 12);
            using var line = new Pen(Color.FromArgb(255, 255, 255, 120), 2);
            g.DrawLine(line, 12, 12, 52, 52);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /* ================= 侧栏 ================= */

    private void BuildSide()
    {
        _side = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Theme.Panel };

        var brand = new Label
        {
            Text = "gautreau",
            Font = Theme.Brand,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(24, 26),
        };
        var sub = new Label
        {
            Text = "播 放 器",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Theme.Faint,
            AutoSize = true,
            Location = new Point(26, 54),
        };
        _side.Controls.Add(brand);
        _side.Controls.Add(sub);

        var items = new (string key, string glyph, string label)[]
        {
            ("daily", "♫", "每日推荐"),
            ("playlists", "◈", "推荐歌单"),
            ("mine", "♥", "我的歌单"),
            ("search", "⌕", "搜索"),
        };

        int y = 96;
        foreach (var (key, glyph, label) in items)
        {
            var b = new NavBtn(glyph, label)
            {
                Location = new Point(16, y),
                Width = 168,
            };
            b.Click += async (_, _) => await SwitchViewAsync(key);
            _side.Controls.Add(b);
            _nav.Add(b);
            y += 46;
        }

        // 底部用户区
        var userPanel = new Panel
        {
            Height = 66,
            Width = 176,
            Location = new Point(12, _side.Height - 78),
            BackColor = Color.Transparent,
        };
        userPanel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            Draw.FillRound(g, new Rectangle(0, 0, userPanel.Width - 1, userPanel.Height - 1), 12, Theme.Card);
            // 头像圆
            var av = new Rectangle(12, 15, 36, 36);
            using var b = new SolidBrush(Theme.Accent);
            g.FillEllipse(b, av);
            var initial = string.IsNullOrEmpty(_nickname) ? "?" : _nickname.Substring(0, 1).ToUpper();
            TextRenderer.DrawText(g, initial, new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                av, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        _nickText = new Label
        {
            Text = "未登录",
            Font = Theme.Bold,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            AutoSize = false,
            Size = new Size(108, 20),
            Location = new Point(58, 15),
            AutoEllipsis = true,
        };
        _btnLogin = new FlatBtn("登录", Theme.Small)
        {
            BackNormal = Color.Transparent,
            BackHover = Theme.CardHover,
            TextColor = Theme.SubText,
            Radius = 6,
            Size = new Size(64, 20),
            Location = new Point(58, 36),
        };
        _btnLogin.Click += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(_nickname))
            {
                if (MessageBox.Show($"当前账号：{_nickname}\n\n要退出登录吗？", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _api.Logout();
                    _nickname = null;
                    await RefreshUserAsync();
                }
                return;
            }
            await OpenLoginAsync();
        };
        userPanel.Controls.Add(_nickText);
        userPanel.Controls.Add(_btnLogin);
        _side.Controls.Add(userPanel);

        _side.Resize += (_, _) =>
        {
            userPanel.Location = new Point(12, _side.Height - 78);
        };
    }

    /* ================= 主区 ================= */

    private void BuildBody()
    {
        _body = new Panel { BackColor = Theme.Bg };

        _title = new Label
        {
            Text = "每日推荐",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(30, 24),
        };

        _btnLevel = new FlatBtn("无损 FLAC", Theme.Small)
        {
            Size = new Size(112, 30),
            BackNormal = Theme.Card,
            BorderColor = Theme.Line,
            TextColor = Theme.SubText,
            Radius = 8,
        };
        _btnLevel.Click += (_, _) => ShowLevelMenu();

        _btnRefresh = new FlatBtn("刷新", Theme.Small)
        {
            Size = new Size(58, 30),
            BackNormal = Theme.Card,
            BorderColor = Theme.Line,
            TextColor = Theme.SubText,
        };
        _btnRefresh.Click += async (_, _) => await ReloadCurrentViewAsync();

        _search = new TextBox
        {
            Visible = false,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Base,
        };
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await DoSearchAsync(); }
        };

        _btnGo = new FlatBtn("搜索", Theme.Small)
        {
            Size = new Size(60, 28),
            Visible = false,
            BackNormal = Theme.Accent,
            BackHover = Theme.AccentSoft,
        };
        _btnGo.Click += async (_, _) => await DoSearchAsync();

        _list = new SongList();
        _list.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppress) return;
            int i = _list.SelectedIndex;
            if (i < 0) return;
            if (_view == "playlists" || _view == "mine")
            {
                if (i < _playlists.Count) await OpenPlaylistAsync(_playlists[i]);
                return;
            }
            await PlayAtAsync(i);
        };

        _toast = new Label
        {
            Visible = false,
            AutoSize = false,
            Size = new Size(400, 36),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Small,
        };

        _body.Controls.AddRange(new Control[] { _title, _btnLevel, _btnRefresh, _search, _btnGo, _list, _toast });
        _body.Resize += (_, _) => LayoutBody();
    }

    private void LayoutBody()
    {
        int w = _body.Width, h = _body.Height;
        _title.Location = new Point(30, 24);
        _btnLevel.Location = new Point(Math.Max(240, w - 190), 22);
        _btnRefresh.Location = new Point(Math.Max(340, w - 72), 22);

        _search.Location = new Point(30, 68);
        _search.Size = new Size(Math.Max(120, w - 142), 28);
        _btnGo.Location = new Point(Math.Max(160, w - 106), 68);

        int top = _search.Visible ? 110 : 70;
        _list.Location = new Point(14, top);
        _list.Size = new Size(w - 28, Math.Max(50, h - top - 12));

        _toast.Location = new Point((w - _toast.Width) / 2, Math.Max(10, h - 76));
    }

    private void ShowLevelMenu()
    {
        var menu = new DarkMenu();
        foreach (var (key, label) in Levels)
        {
            var mark = key == _level ? "●  " : "     ";
            var item = menu.Items.Add(mark + label);
            item.Tag = key;
            item.Font = key == _level ? Theme.Bold : Theme.Base;
            item.Click += async (_, _) =>
            {
                _level = key;
                _btnLevel.Text = Levels.First(x => x.key == key).label;
                _btnLevel.Invalidate();
                ShowToast("音质切换为 " + label);
                if (_index >= 0 && _player.HasAudio) await PlayAtAsync(_index);
            };
        }
        menu.Show(_btnLevel, new Point(0, _btnLevel.Height + 6));
    }

    /* ================= 播放列表 ================= */

    private void BuildQueue()
    {
        _queue = new Panel { Width = 316, BackColor = Theme.Panel, Visible = true };

        var head = new Label
        {
            Text = "当前播放",
            Font = Theme.Bold,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, 26),
        };
        var close = new FlatBtn("✕", Theme.Small)
        {
            Size = new Size(28, 28),
            BackNormal = Color.Transparent,
            BackHover = Theme.CardHover,
            TextColor = Theme.Faint,
            Location = new Point(268, 22),
        };
        close.Click += (_, _) => { _queue.Visible = false; };

        _queueList = new SongList { ShowIndex = false, ItemHeight = 52, ShowDur = false, ShowArtist = false };
        _queueList.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppress) return;
            int i = _queueList.SelectedIndex;
            if (i >= 0 && i < _songs.Count) await PlayAtAsync(i);
        };

        var hint = new Label
        {
            Text = "",
            Font = Theme.Tiny,
            ForeColor = Theme.Faint,
            AutoSize = false,
            Location = new Point(20, 54),
            Size = new Size(240, 18),
        };

        _queue.Controls.AddRange(new Control[] { head, close, _queueList, hint });
        _queue.Resize += (_, _) =>
        {
            _queueList.Location = new Point(10, 82);
            _queueList.Size = new Size(_queue.Width - 20, Math.Max(50, _queue.Height - 92));
            close.Location = new Point(_queue.Width - 40, 22);
        };
        _queue.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Line);
            e.Graphics.DrawLine(p, 0, 0, 0, _queue.Height);
        };
    }

    private void RefreshQueue()
    {
        _suppress = true;
        try
        {
            _queueList.Items.Clear();
            foreach (var s in _songs) _queueList.Items.Add(s);
            _queueList.PlayingIndex = _index;
            _queueList.SelectedIndex = -1;
            _queueList.Invalidate();
        }
        finally { _suppress = false; }
    }

    /* ================= 播放条 ================= */

    private void BuildFooter()
    {
        _footer = new Panel { Height = 104, BackColor = Theme.Panel };
        _footer.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Line);
            e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
        };

        _cover = new PictureBox
        {
            Size = new Size(60, 60),
            Location = new Point(24, 22),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.Card,
            Cursor = Cursors.Hand,
        };
        _cover.Paint += (_, e) =>
        {
            Theme.Smooth(e.Graphics);
            using var path = Draw.Round(new Rectangle(0, 0, _cover.Width - 1, _cover.Height - 1), 10);
            using var pen = new Pen(Theme.Line);
            e.Graphics.DrawPath(pen, path);
        };
        _cover.Click += (_, _) => ToggleLyrics();

        _nowTitle = new Label
        {
            Text = "未在播放",
            Font = Theme.Bold,
            ForeColor = Theme.Text,
            AutoSize = false,
            Size = new Size(220, 22),
        };
        _nowArtist = new Label
        {
            Text = "—",
            Font = Theme.Small,
            ForeColor = Theme.SubText,
            AutoSize = false,
            Size = new Size(220, 20),
        };

        _btnPrev = RoundIcon("⏮", 38);
        _btnPlay = RoundIcon("▶", 46, accent: true);
        _btnNext = RoundIcon("⏭", 38);
        _btnMode = RoundIcon("↻", 34, small: true);

        _btnPrev.Click += (_, _) => Advance(false, true);
        _btnPlay.Click += (_, _) => TogglePlay();
        _btnNext.Click += (_, _) => Advance(false);
        _btnMode.Click += (_, _) =>
        {
            _mode = _mode switch { "list" => "one", "one" => "shuffle", _ => "list" };
            _btnMode.Text = _mode switch { "one" => "🔂", "shuffle" => "🔀", _ => "↻" };
            _btnMode.Invalidate();
            ShowToast(_mode switch { "one" => "单曲循环", "shuffle" => "随机播放", _ => "列表循环" });
        };

        _tCur = new Label { Text = "00:00", Font = Theme.Small, ForeColor = Theme.Faint, AutoSize = false, Size = new Size(44, 16), TextAlign = ContentAlignment.MiddleLeft };
        _tDur = new Label { Text = "00:00", Font = Theme.Small, ForeColor = Theme.Faint, AutoSize = false, Size = new Size(44, 16), TextAlign = ContentAlignment.MiddleRight };
        _prog = new ProgBar();
        _prog.SeekRequested += ts =>
        {
            _seeking = true;
            _player.Seek(ts);
            _seeking = false;
        };

        _btnLyric = new FlatBtn("词", Theme.Small)
        {
            Size = new Size(32, 28),
            BackNormal = Theme.Card,
            TextColor = Theme.SubText,
            Radius = 7,
        };
        _btnLyric.Click += (_, _) => ToggleLyrics();

        _btnQueue = new FlatBtn("≡", Theme.Icon)
        {
            Size = new Size(32, 28),
            BackNormal = Theme.Card,
            TextColor = Theme.SubText,
            Radius = 7,
        };
        _btnQueue.Click += (_, _) =>
        {
            _queue.Visible = !_queue.Visible;
            _btnQueue.TextColor = _queue.Visible ? Theme.Accent : Theme.SubText;
            _btnQueue.Invalidate();
        };

        _vol = new VolBar { Value = AudioPlayer.LoadVolume() };
        _player.Volume = _vol.Value;
        _vol.ValueChanged += v => _player.Volume = v;

        _footer.Controls.AddRange(new Control[]
        {
            _cover, _nowTitle, _nowArtist,
            _btnPrev, _btnPlay, _btnNext, _btnMode,
            _tCur, _prog, _tDur, _btnLyric, _vol, _btnQueue
        });
        _footer.Resize += (_, _) => LayoutFooter();
    }

    private FlatBtn RoundIcon(string glyph, int size, bool accent = false, bool small = false)
    {
        var b = new FlatBtn(glyph, small ? Theme.Icon : (accent ? Theme.Play : Theme.IconBig))
        {
            Size = new Size(size, size),
            Radius = size / 2,
            BackNormal = accent ? Theme.Accent : Color.Transparent,
            BackHover = accent ? Theme.AccentSoft : Theme.CardHover,
            TextColor = accent ? Color.White : Theme.Text,
        };
        return b;
    }

    private void LayoutFooter()
    {
        int w = _footer.Width;
        _cover.Location = new Point(24, 22);
        _nowTitle.Location = new Point(96, 28);
        _nowArtist.Location = new Point(96, 50);

        int cx = Math.Max(400, w / 2);
        _btnMode.Location = new Point(cx - 140, 22);
        _btnPrev.Location = new Point(cx - 96, 20);
        _btnPlay.Location = new Point(cx - 27, 16);
        _btnNext.Location = new Point(cx + 40, 20);

        int barLeft = 300;
        int barRight = w - 300;
        if (barRight - barLeft < 220) barRight = w - 220;
        _tCur.Location = new Point(barLeft - 52, 70);
        _prog.Location = new Point(barLeft, 69);
        _prog.Size = new Size(Math.Max(140, barRight - barLeft), 18);
        _tDur.Location = new Point(barRight + 8, 70);

        _btnQueue.Location = new Point(w - 46, 64);
        _vol.Location = new Point(w - 138, 69);
        _btnLyric.Location = new Point(w - 182, 64);
    }

    /* ================= 数据 ================= */

    private async Task RunUiTestAsync()
    {
        Log.W("[uitest] 开始");
        foreach (var v in new[] { "playlists", "mine", "search", "daily" })
        {
            try
            {
                _search.Text = v == "search" ? "周杰伦" : _search.Text;
                await SwitchViewAsync(v);
                await Task.Delay(500);
                Log.W($"[uitest] {v} OK items={_list.Items.Count}");
            }
            catch (Exception ex) { Log.W($"[uitest] {v} FAIL: {ex}"); }
        }

        try
        {
            await SwitchViewAsync("playlists");
            await Task.Delay(400);
            if (_playlists.Count > 0)
            {
                await OpenPlaylistAsync(_playlists[0]);
                await Task.Delay(600);
                Log.W($"[uitest] openPlaylist OK tracks={_songs.Count} queue={_queueList.Items.Count}");
            }
        }
        catch (Exception ex) { Log.W("[uitest] openPlaylist FAIL: " + ex); }

        try
        {
            if (_songs.Count > 0)
            {
                var before = _player.Volume;
                await PlayAtAsync(0);
                await Task.Delay(2500);
                await PlayAtAsync(1);
                await Task.Delay(2000);
                Log.W($"[uitest] play OK state={_player.IsPlaying} fmt={_player.FormatInfo} vol={_player.Volume:0.00} (was {before:0.00})");
                _player.Pause();
            }
        }
        catch (Exception ex) { Log.W("[uitest] play FAIL: " + ex); }

        Log.W("[uitest] 结束");
    }

    public async Task<bool> TryClientSessionAsync()
    {
        var r = await Task.Run(ClientSession.TryExtract);
        if (!r.Ok) { Log.W("[client] 未取到客户端会话: " + r.Reason); return false; }
        Log.W($"[client] 取到 {r.Count} 个 cookie，开始验证");
        if (!await _api.ImportCookieAsync(r.Cookie)) { Log.W("[client] 客户端会话已失效"); return false; }

        var nick = await _api.NicknameAsync();
        if (string.IsNullOrEmpty(nick)) { Log.W("[client] 客户端会话校验失败"); return false; }

        // 客户端会话覆盖本地缓存，避免残留的旧账号串台
        _api.SaveCookie();
        _nickname = nick;
        Log.W("[client] 使用客户端账号: " + _nickname);
        await RefreshUserAsync();
        await LoadDailyAsync();
        ShowToast($"已连接客户端账号：{_nickname}");
        return true;
    }

    private async Task<bool> OpenLoginAsync()
    {
        using (var lf = new LoginWebForm(_api))
        {
            if (lf.ShowDialog(this) == DialogResult.OK)
            {
                _nickname = lf.LoggedNickname;
                await RefreshUserAsync();
                await LoadDailyAsync();
                return true;
            }
        }

        if (MessageBox.Show("内嵌登录没完成。要试一下备用登录方式（内置扫码 / 手机验证码）吗？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return false;

        using var alt = new LoginForm(_api);
        if (alt.ShowDialog(this) != DialogResult.OK) return false;
        _nickname = alt.LoggedNickname;
        await RefreshUserAsync();
        await LoadDailyAsync();
        return true;
    }

    private async Task RefreshUserAsync()
    {
        _nickname = await _api.NicknameAsync();
        if (string.IsNullOrEmpty(_nickname))
        {
            _nickname = null;
            _nickText.Text = "未登录";
            _btnLogin.Text = "点此登录";
        }
        else
        {
            _nickText.Text = _nickname;
            _btnLogin.Text = "退出";
        }
        _nickText.Invalidate();
        _btnLogin.Invalidate();
    }

    private void SetNavActive(string view)
    {
        for (int i = 0; i < _nav.Count; i++)
            _nav[i].Active = _nav[i].Text == ViewLabel(view);
        foreach (var b in _nav) b.Invalidate();
    }

    private async Task SwitchViewAsync(string view)
    {
        _view = view;
        SetNavActive(view);
        _search.Visible = view == "search";
        _btnGo.Visible = view == "search";
        LayoutBody();
        if (view == "search") { _search.Focus(); return; }
        await ReloadCurrentViewAsync();
    }

    private static string ViewLabel(string v) => v switch
    {
        "daily" => "每日推荐",
        "playlists" => "推荐歌单",
        "mine" => "我的歌单",
        "search" => "搜索",
        _ => "每日推荐"
    };

    private async Task ReloadCurrentViewAsync()
    {
        switch (_view)
        {
            case "daily": await LoadDailyAsync(); break;
            case "playlists": await LoadPlaylistsAsync(); break;
            case "mine": await LoadMineAsync(); break;
            case "search": await DoSearchAsync(); break;
            default: await LoadDailyAsync(); break;
        }
    }

    private void SetSongs(IEnumerable<Song> songs, string title)
    {
        _suppress = true;
        try
        {
            _songs.Clear();
            _songs.AddRange(songs);
            _index = -1;
            _list.PlayingIndex = -1;
            _list.Items.Clear();
            foreach (var s in _songs) _list.Items.Add(s);
            _list.SelectedIndex = -1;
            _list.ShowDur = true;
            _title.Text = title;
            _list.Invalidate();
        }
        finally { _suppress = false; }

        RefreshQueue();
        if (_songs.Count == 0) ShowToast("这里还没有内容");
    }

    private void SetPlaylists(IReadOnlyList<Playlist> pls, string title)
    {
        _suppress = true;
        try
        {
            _playlists.Clear();
            _playlists.AddRange(pls);
            _songs.Clear();
            _index = -1;
            _list.PlayingIndex = -1;
            _list.Items.Clear();
            foreach (var p in _playlists)
                _list.Items.Add(new Song
                {
                    Id = p.Id,
                    Name = p.Name,
                    Artist = PlayCountText(p),
                    Cover = p.Cover,
                    Fee = 0,
                });
            _list.SelectedIndex = -1;
            _list.ShowDur = false;
            _title.Text = title;
            _list.Invalidate();
        }
        finally { _suppress = false; }
        RefreshQueue();
    }

    private static string PlayCountText(Playlist p)
        => p.PlayCount > 10000 ? $"{p.PlayCount / 10000.0:0.#} 万次播放" : p.Count > 0 ? $"{p.Count} 首" : "";

    private async Task LoadDailyAsync()
    {
        _title.Text = "每日推荐 · 加载中…";
        var songs = await _api.DailySongsAsync();
        SetSongs(songs, "每日推荐 · " + DateTime.Now.ToString("M月d日"));
        if (string.IsNullOrEmpty(_nickname)) ShowToast("未登录：当前是基于网络环境的匿名推荐");
    }

    private async Task LoadPlaylistsAsync()
    {
        _title.Text = "推荐歌单 · 加载中…";
        var pls = await _api.DailyPlaylistsAsync();
        if (pls.Count == 0)
        {
            SetSongs(await _api.NewsongAsync(), "推荐新音乐");
            return;
        }
        SetPlaylists(pls, "推荐歌单 · 单击进入");
    }

    private async Task OpenPlaylistAsync(Playlist p)
    {
        _title.Text = p.Name + " · 加载中…";
        var songs = await _api.PlaylistTracksAsync(p.Id);
        _view = "playlist";
        SetSongs(songs, p.Name);
    }

    private async Task LoadMineAsync()
    {
        _title.Text = "我的歌单 · 加载中…";
        if (string.IsNullOrEmpty(_nickname))
        {
            SetSongs(Array.Empty<Song>(), "我的歌单");
            ShowToast("登录后显示你创建和收藏的歌单");
            return;
        }
        SetPlaylists(await _api.MyPlaylistsAsync(), "我的歌单 · 单击进入");
    }

    private async Task DoSearchAsync()
    {
        var kw = _search.Text.Trim();
        if (kw.Length == 0) return;
        _title.Text = "搜索：" + kw + " · 加载中…";
        SetSongs(await _api.SearchAsync(kw), "搜索：" + kw);
    }

    /* ================= 播放 ================= */

    private async Task PlayAtAsync(int i)
    {
        if (i < 0 || i >= _songs.Count) return;
        _index = i;
        _list.PlayingIndex = i;
        _list.Invalidate();
        _queueList.PlayingIndex = i;
        _queueList.Invalidate();

        var s = _songs[i];
        _nowTitle.Text = s.Name;
        _nowArtist.Text = s.Artist;

        try
        {
            if (!string.IsNullOrEmpty(s.Cover))
            {
                using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var bytes = await hc.GetByteArrayAsync(s.Cover + "?param=160y160");
                using var ms = new MemoryStream(bytes);
                using var raw = Image.FromStream(ms);
                var old = _cover.Image;
                _cover.Image = Draw.RoundImage(raw, 60, 10);
                old?.Dispose();
            }
        }
        catch { }

        var (url, fee, _) = await _api.SongUrlAsync(s.Id, _level);
        if (string.IsNullOrEmpty(url))
        {
            ShowToast(fee == 1 ? "该曲目需要会员音源，已跳过" : "暂无版权或已下架，已跳过");
            if (_songs.Count > 1)
            {
                await Task.Delay(600);
                Advance(true);
            }
            return;
        }

        if (!await _player.LoadAsync(url)) { ShowToast("音源加载失败"); return; }
        _player.Play();
        _btnPlay.Text = "⏸";
        _btnPlay.Invalidate();
        _tick.Start();
        await LoadLyricAsync(s.Id);
    }

    private async Task LoadLyricAsync(long id)
    {
        _lrc = new List<(TimeSpan, string)>();
        _lrcIdx = -1;
        var (lrc, tlrc) = await _api.LyricAsync(id);
        _lrc = Lrc.Parse(lrc, tlrc);
        _lyrics?.SetLines(_lrc);
    }

    private void TogglePlay()
    {
        if (!_player.HasAudio)
        {
            if (_songs.Count > 0) _ = PlayAtAsync(_index < 0 ? 0 : _index);
            return;
        }
        if (_player.IsPlaying)
        {
            _player.Pause();
            _btnPlay.Text = "▶";
        }
        else
        {
            _player.Play();
            _btnPlay.Text = "⏸";
        }
        _btnPlay.Invalidate();
    }

    private void Advance(bool auto, bool forcePrev = false)
    {
        if (_songs.Count == 0) return;
        if (_mode == "one") { _ = PlayAtAsync(_index); return; }

        int next;
        if (forcePrev) next = _index - 1 < 0 ? _songs.Count - 1 : _index - 1;
        else if (_mode == "shuffle" && auto)
        {
            var rnd = new Random();
            next = _songs.Count > 1 ? rnd.Next(_songs.Count) : 0;
            if (_songs.Count > 1 && next == _index) next = (next + 1) % _songs.Count;
        }
        else next = _index + 1 >= _songs.Count ? 0 : _index + 1;

        _ = PlayAtAsync(next);
    }

    private void OnTick()
    {
        if (!_player.HasAudio) return;
        var pos = _player.Position;
        var dur = _player.Duration;
        if (!_seeking)
        {
            _prog.Position = pos;
            _prog.Duration = dur;
            _prog.Invalidate();
        }
        _tCur.Text = pos.ToString(@"mm\:ss");
        _tDur.Text = dur.ToString(@"mm\:ss");

        if (_lrc.Count > 0)
        {
            int idx = -1;
            for (int i = 0; i < _lrc.Count; i++)
            {
                if (_lrc[i].t <= pos) idx = i;
                else break;
            }
            if (idx != _lrcIdx)
            {
                _lrcIdx = idx;
                _lyrics?.Update(pos);
            }
        }
    }

    private void ToggleLyrics()
    {
        if (_lyrics == null || _lyrics.IsDisposed)
        {
            _lyrics = new LyricsForm();
            _lyrics.SetLines(_lrc);
            _lyrics.Location = new Point(Right + 12, Top);
            _lyrics.Show(this);
        }
        else if (_lyrics.Visible) _lyrics.Hide();
        else _lyrics.Show();
    }

    private void ShowToast(string msg)
    {
        _toast.Text = msg;
        _toast.Visible = true;
        _toast.BringToFront();
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
