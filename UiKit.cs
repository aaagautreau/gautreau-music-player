using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace NcmNative;

public static class Theme
{
    // 浅色系
    public static readonly Color Bg = Color.FromArgb(246, 247, 249);        // 页面底色
    public static readonly Color Panel = Color.FromArgb(255, 255, 255);     // 侧栏 / 播放条
    public static readonly Color Card = Color.FromArgb(240, 242, 245);      // 按钮 / 卡片
    public static readonly Color CardHover = Color.FromArgb(230, 233, 239);
    public static readonly Color Line = Color.FromArgb(229, 232, 237);
    public static readonly Color Text = Color.FromArgb(26, 29, 33);
    public static readonly Color SubText = Color.FromArgb(107, 114, 128);
    public static readonly Color Faint = Color.FromArgb(156, 163, 175);
    public static readonly Color Accent = Color.FromArgb(245, 64, 90);
    public static readonly Color AccentSoft = Color.FromArgb(255, 92, 116);
    public static readonly Color Gold = Color.FromArgb(194, 120, 10);
    public static readonly Color RowHover = Color.FromArgb(242, 244, 248);
    public static readonly Color Track = Color.FromArgb(229, 232, 237);

    public static readonly Font Base = new("Microsoft YaHei UI", 9.5f);
    public static readonly Font Bold = new("Microsoft YaHei UI", 10f, FontStyle.Bold);
    public static readonly Font Title = new("Microsoft YaHei UI", 16f, FontStyle.Bold);
    public static readonly Font Small = new("Microsoft YaHei UI", 8.5f);
    public static readonly Font Tiny = new("Microsoft YaHei UI", 8f);
    public static readonly Font Brand = new("Segoe UI", 17f, FontStyle.Bold);
    public static readonly Font Icon = new("Segoe UI Symbol", 12f);
    public static readonly Font IconBig = new("Segoe UI Symbol", 16f);
    public static readonly Font Play = new("Segoe UI Symbol", 13f);

    // 兼容早期窗体使用的旧命名
    public static Font FontBase => Base;
    public static Font FontBold => Bold;
    public static Font FontTitle => Title;
    public static Font FontSmall => Small;
    public static Font FontTiny => Tiny;
    public static Font FontIcon => Icon;
    public static Font FontBig => IconBig;
    public static Color Dim => SubText;
    public static Color Panel2 => Card;

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}

public static class Draw
{
    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        if (r.Width <= d || r.Height <= d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
    {
        using var path = Round(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c)
    {
        using var path = Round(r, radius);
        using var pen = new Pen(c);
        g.DrawPath(pen, path);
    }

    /// <summary>圆角图片，避免直角封面</summary>
    public static Image RoundImage(Image src, int size, int radius)
    {
        if (src == null) return null;
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            Theme.Smooth(g);
            using var path = Round(new Rectangle(0, 0, size, size), radius);
            g.SetClip(path);
            g.DrawImage(src, new Rectangle(0, 0, size, size));
        }
        return bmp;
    }
}

/// <summary>圆角按钮</summary>
public sealed class FlatBtn : Control
{
    private bool _hover;
    private bool _down;

    public Color BackNormal { get; set; } = Theme.Card;
    public Color BackHover { get; set; } = Theme.CardHover;
    public Color TextColor { get; set; } = Theme.Text;
    public Color? BorderColor { get; set; }
    public int Radius { get; set; } = 8;

    // 兼容早期窗体
    public Color NormalColor { get => BackNormal; set => BackNormal = value; }
    public Color HoverColor { get => BackHover; set => BackHover = value; }

    public FlatBtn(string text, Font font = null)
    {
        Text = text;
        Font = font ?? Theme.Base;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var back = (_hover || _down) ? BackHover : BackNormal;
        if (back.A > 0) Draw.FillRound(g, r, Radius, back);
        if (BorderColor.HasValue) Draw.StrokeRound(g, r, Radius, BorderColor.Value);
        TextRenderer.DrawText(g, Text, Font, r,
            Enabled ? TextColor : Theme.Faint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>导航项：左侧色条 + 图标 + 文字</summary>
public sealed class NavBtn : Control
{
    private bool _hover;
    public bool Active { get; set; }
    public string Glyph { get; set; } = "";

    public NavBtn(string glyph, string text)
    {
        Glyph = glyph;
        Text = text;
        Font = Theme.Base;
        Height = 42;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var body = new Rectangle(0, 4, Width, Height - 8);
        if (Active) Draw.FillRound(g, body, 9, Theme.Card);
        else if (_hover) Draw.FillRound(g, body, 9, Theme.RowHover);

        if (Active) Draw.FillRound(g, new Rectangle(0, 12, 3, Height - 24), 2, Theme.Accent);

        var color = Active ? Theme.Text : Theme.SubText;
        var glyphRect = new Rectangle(20, 0, 24, Height);
        TextRenderer.DrawText(g, Glyph, Theme.Icon, glyphRect, Active ? Theme.Accent : Theme.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var textRect = new Rectangle(50, 0, Width - 58, Height);
        TextRenderer.DrawText(g, Text, Active ? Theme.Bold : Theme.Base, textRect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>进度条</summary>
public sealed class ProgBar : Control
{
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    public event Action<TimeSpan> SeekRequested;

    private bool _dragging;
    private bool _hover;
    private double _preview = -1;

    public ProgBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 18;
        Cursor = Cursors.Hand;
    }

    private double Ratio
    {
        get
        {
            if (Duration.TotalSeconds <= 0) return 0;
            if (_preview >= 0) return _preview;
            return Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _preview = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int cy = Height / 2;
        int h = _hover || _dragging ? 6 : 4;
        Draw.FillRound(g, new Rectangle(0, cy - h / 2, Width, h), h / 2, Theme.Track);

        int w = (int)(Width * Ratio);
        if (w > 2)
        {
            var fill = new Rectangle(0, cy - h / 2, w, h);
            using var path = Draw.Round(fill, h / 2);
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(2, Width), Height), Theme.Accent, Theme.AccentSoft, 0f);
            g.FillPath(brush, path);
        }

        if (_hover || _dragging)
        {
            int dx = Math.Clamp(w, 4, Math.Max(4, Width - 4));
            using var b = new SolidBrush(Theme.Accent);
            g.FillEllipse(b, dx - 5, cy - 5, 10, 10);
            using var pen = new Pen(Color.White, 2);
            g.DrawEllipse(pen, dx - 5, cy - 5, 10, 10);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || Duration.TotalSeconds <= 0) return;
        _dragging = true;
        _preview = Math.Clamp((double)e.X / Math.Max(1, Width), 0, 1);
        Invalidate();
        SeekRequested?.Invoke(TimeSpan.FromSeconds(Duration.TotalSeconds * _preview));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _preview = Math.Clamp((double)e.X / Math.Max(1, Width), 0, 1);
        Invalidate();
        SeekRequested?.Invoke(TimeSpan.FromSeconds(Duration.TotalSeconds * _preview));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        _preview = -1;
        Invalidate();
    }
}

/// <summary>音量条</summary>
public sealed class VolBar : Control
{
    private float _value = 0.4f;
    private bool _drag;
    private bool _hover;

    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
    }
    public event Action<float> ValueChanged;

    public VolBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 18;
        Width = 84;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int cy = Height / 2;
        int h = _hover || _drag ? 5 : 3;
        Draw.FillRound(g, new Rectangle(0, cy - h / 2, Width, h), h / 2, Theme.Track);
        int w = (int)(Width * _value);
        if (w > 2) Draw.FillRound(g, new Rectangle(0, cy - h / 2, w, h), h / 2, Theme.SubText);
        using var b = new SolidBrush(Theme.Text);
        int dx = Math.Clamp(w, 4, Math.Max(4, Width - 4));
        g.FillEllipse(b, dx - 4, cy - 4, 8, 8);
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; Set(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) Set(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Invalidate(); }

    private void Set(int x)
    {
        Value = Math.Clamp((float)x / Math.Max(1, Width), 0, 1);
        ValueChanged?.Invoke(Value);
    }
}

/// <summary>浅色下拉菜单</summary>
public sealed class DarkMenu : ContextMenuStrip
{
    public DarkMenu()
    {
        Renderer = new LightRenderer();
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        ShowImageMargin = false;
        Padding = new Padding(5);
        DropShadowEnabled = false;
    }

    private sealed class LightRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            => e.Graphics.Clear(Theme.Panel);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Line);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            Theme.Smooth(e.Graphics);
            Draw.FillRound(e.Graphics, new Rectangle(3, 2, e.Item.Width - 6, e.Item.Height - 4), 6, Theme.RowHover);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Theme.Text : Theme.SubText;
            base.OnRenderItemText(e);
        }
    }
}

/// <summary>歌曲列表</summary>
public sealed class SongList : ListBox
{
    private int _hover = -1;
    private readonly Dictionary<long, Image> _covers = new();
    private readonly HashSet<long> _loading = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public int PlayingIndex { get; set; } = -1;
    public bool ShowIndex { get; set; } = true;
    public bool ShowDur { get; set; } = true;
    public bool ShowCover { get; set; } = true;
    public bool ShowArtist { get; set; } = true;

    public SongList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 54;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = IndexFromPoint(e.Location);
        if (idx != _hover) { _hover = idx; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private Image Cover(Song s)
    {
        if (string.IsNullOrEmpty(s.Cover)) return null;
        if (_covers.TryGetValue(s.Id, out var img)) return img;
        if (!_loading.Add(s.Id)) return null;

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(s.Cover + "?param=80y80");
                using var ms = new MemoryStream(bytes);
                using var raw = Image.FromStream(ms);
                var rounded = Draw.RoundImage(raw, 38, 7);
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    _covers[s.Id] = rounded;
                    Invalidate();
                }));
            }
            catch { }
        });
        return null;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var g = e.Graphics;
        Theme.Smooth(g);

        bool playing = e.Index == PlayingIndex;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool hover = e.Index == _hover;

        var row = new Rectangle(8, e.Bounds.Y + 2, Width - 18, e.Bounds.Height - 4);
        if (selected || hover)
            Draw.FillRound(g, row, 10, selected ? Theme.RowHover : Color.FromArgb(250, 251, 252));

        var song = Items[e.Index] as Song;
        if (song == null) return;

        int sb = 20;
        int durW = ShowDur ? 52 : 0;
        int artW = ShowArtist ? Math.Clamp((Width - sb) / 4, 90, 180) : 0;
        int left = ShowIndex ? 44 : 20;

        // 封面
        if (ShowCover)
        {
            var box = new Rectangle(left, e.Bounds.Y + (e.Bounds.Height - 38) / 2, 38, 38);
            var img = Cover(song);
            if (img != null) g.DrawImage(img, box);
            else
            {
                Draw.FillRound(g, box, 7, Theme.Card);
                TextRenderer.DrawText(g, "♪", Theme.Icon, box, Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            left += 48;
        }

        if (playing)
            Draw.FillRound(g, new Rectangle(ShowIndex ? 10 : 6, e.Bounds.Y + 16, 3, e.Bounds.Height - 32), 2, Theme.Accent);

        if (ShowIndex)
        {
            var numRect = new Rectangle(16, e.Bounds.Y, 24, e.Bounds.Height);
            TextRenderer.DrawText(g, (e.Index + 1).ToString(), Theme.Small, numRect,
                playing ? Theme.Accent : Theme.Faint,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        int nameRight = Width - sb - durW - 22 - artW;
        var nameRect = new Rectangle(left, e.Bounds.Y, Math.Max(60, nameRight - left), e.Bounds.Height);
        var nameFont = playing ? Theme.Bold : Theme.Base;
        TextRenderer.DrawText(g, song.Name, nameFont, nameRect,
            playing ? Theme.Accent : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (song.Vip && nameRect.Width > 110)
        {
            var measured = TextRenderer.MeasureText(g, song.Name, nameFont,
                new Size(nameRect.Width, nameRect.Height), TextFormatFlags.NoPadding);
            int textW = Math.Min(measured.Width, nameRect.Width);
            var sz = TextRenderer.MeasureText("VIP", Theme.Tiny);
            int badgeX = left + textW + 8;
            if (badgeX + sz.Width + 6 < Width - sb - durW - 20)
            {
                var badge = new Rectangle(badgeX, e.Bounds.Y + (e.Bounds.Height - 15) / 2, sz.Width + 6, 15);
                Draw.StrokeRound(g, badge, 3, Color.FromArgb(228, 205, 160));
                TextRenderer.DrawText(g, "VIP", Theme.Tiny, badge, Theme.Gold,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        if (artW > 0)
        {
            var artRect = new Rectangle(Width - sb - durW - 18 - artW, e.Bounds.Y, artW, e.Bounds.Height);
            TextRenderer.DrawText(g, song.Artist, Theme.Small, artRect, Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        if (ShowDur)
        {
            var durRect = new Rectangle(Width - sb - durW, e.Bounds.Y, durW, e.Bounds.Height);
            TextRenderer.DrawText(g, song.Dur, Theme.Small, durRect, Theme.Faint,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
