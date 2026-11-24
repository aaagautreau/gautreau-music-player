using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace NcmNative;

public static class Lrc
{
    private static readonly Regex Tag = new(@"\[(\d+):(\d+(?:\.\d+)?)\](.*)", RegexOptions.Compiled);

    public static List<(TimeSpan t, string txt)> Parse(string lrc, string tlrc)
    {
        var main = ParseOne(lrc);
        // 用索引器而不是 Add：翻译歌词偶尔有重复时间戳，ToDictionary 会直接抛异常
        var trans = new Dictionary<double, string>();
        foreach (var x in ParseOne(tlrc)) trans[x.t.TotalMilliseconds] = x.txt;
        return main
            .Select(x => (x.t, trans.TryGetValue(x.t.TotalMilliseconds, out var tr) && tr.Length > 0
                ? x.txt + "   " + tr
                : x.txt))
            .ToList();
    }

    private static List<(TimeSpan t, string txt)> ParseOne(string text)
    {
        var list = new List<(TimeSpan, string)>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (var line in text.Split('\n'))
        {
            var m = Tag.Match(line.Trim());
            if (!m.Success) continue;
            var sec = int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value);
            var txt = m.Groups[3].Value.Trim();
            if (txt.Length > 0) list.Add((TimeSpan.FromSeconds(sec), txt));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }
}

public sealed class LyricView : Control
{
    private List<(TimeSpan t, string txt)> _lines = new();
    private int _index = -1;

    public LyricView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Bg;
        Font = new Font("Microsoft YaHei UI", 12f);
    }

    public void SetLines(List<(TimeSpan t, string txt)> lines)
    {
        _lines = lines ?? new();
        _index = -1;
        Invalidate();
    }

    public void SetIndex(int i)
    {
        if (i == _index) return;
        _index = i;
        Invalidate();
    }

    public void SetTime(TimeSpan pos)
    {
        if (_lines.Count == 0) return;
        int idx = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].t <= pos) idx = i;
            else break;
        }
        SetIndex(idx);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_lines.Count == 0)
        {
            TextRenderer.DrawText(g, "纯音乐，请欣赏", Theme.FontBase, ClientRectangle, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int lineH = 46;
        int cx = Width / 2;
        int cy = Height / 2;

        // 让当前行大致居中
        int offset = _index < 0 ? 0 : _index * lineH;

        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                int y = -offset + i * lineH + cy;
                if (y < -lineH || y > Height + lineH) continue;
                bool on = i == _index;
                if (pass == 0 && on) continue;
                if (pass == 1 && !on) continue;

                var f = on ? new Font("Microsoft YaHei UI", 14f, FontStyle.Bold) : Font;
                var c = on ? Theme.Accent : Theme.Faint;
                var rect = new RectangleF(20, y - lineH / 2f, Width - 40, lineH);
                using var br = new SolidBrush(c);
                g.DrawString(_lines[i].txt, f, br, rect, fmt);
                if (on) f.Dispose();
            }
        }
    }
}

public sealed class LyricsForm : Form
{
    private readonly LyricView _view = new() { Dock = DockStyle.Fill };

    public LyricsForm()
    {
        Text = "歌词";
        ClientSize = new Size(560, 460);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        Controls.Add(_view);
        TitleBar.UseLight(this);
    }

    public void SetLines(List<(TimeSpan t, string txt)> lines) => _view.SetLines(lines);

    public void Update(TimeSpan pos) => _view.SetTime(pos);
}
