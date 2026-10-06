using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

enum Tool { None, Rect, Ellipse, Arrow, Pen, Text, Mosaic, Sticker }

class Shape
{
    public static readonly Font TextFont = new("Microsoft YaHei UI", 18f, GraphicsUnit.Pixel);
    public Tool Tool;
    public Color Color;
    public List<Point> Pts = [];
    public string Text = "";
    public Bitmap? Image; // Sticker: drawn into Span(Pts[0], Pts[1])

    public static Rectangle Span(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public void Draw(Graphics g, Bitmap source)
    {
        using var pen = new Pen(Color, 3) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        var r = Span(Pts[0], Pts[^1]);
        switch (Tool)
        {
            case Tool.Rect: g.DrawRectangle(pen, r); break;
            case Tool.Ellipse: g.DrawEllipse(pen, r); break;
            case Tool.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap(4, 4);
                if (Pts[0] != Pts[^1]) g.DrawLine(pen, Pts[0], Pts[^1]);
                break;
            case Tool.Pen: if (Pts.Count > 1) g.DrawLines(pen, Pts.ToArray()); break;
            case Tool.Text: using (var b = new SolidBrush(Color)) g.DrawString(Text, TextFont, b, Pts[0]); break;
            case Tool.Mosaic: Pixelate(g, source, r); break;
            case Tool.Sticker:
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; // stickers are hi-res, shown downscaled
                g.DrawImage(Image!, r);
                g.InterpolationMode = mode;
                break;
        }
    }

    static void Pixelate(Graphics g, Bitmap src, Rectangle r)
    {
        r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
        if (r.Width < 2 || r.Height < 2) return;
        const int block = 10;
        using var small = new Bitmap(Math.Max(1, r.Width / block), Math.Max(1, r.Height / block));
        using (var sg = Graphics.FromImage(small))
            sg.DrawImage(src, new Rectangle(0, 0, small.Width, small.Height), r, GraphicsUnit.Pixel);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, r, new Rectangle(0, 0, small.Width, small.Height), GraphicsUnit.Pixel);
        g.Restore(state);
    }
}

// Full-screen frozen screenshot (Snipaste's F1): hover picks a window, drag picks a region,
// then annotate and copy / save / pin. Right-click resets, Esc quits.
class CaptureForm : Form
{
    static CaptureForm? current;

    public static void Start()
    {
        if (current != null) return;
        var f = current = new CaptureForm();
        f.FormClosed += (_, _) => current = null;
        f.Show();
        f.Activate();
    }

    readonly Rectangle vs = SystemInformation.VirtualScreen;
    readonly Bitmap shot, dimmed;
    readonly List<Rectangle> windows;
    readonly List<Shape> shapes = [];
    readonly FlowLayoutPanel bar = new();
    readonly Dictionary<Tool, Button> toolButtons = [];
    readonly ToolTip tips = new();
    Rectangle sel, hover;
    bool selected, selecting, moving;
    Shape? dragging; // sticker being moved
    Shape? active;   // sticker showing its bounding box
    int handle = -1; // handle being dragged on `active`
    Rectangle handleStart;
    Point down, moveOrigin;
    Tool tool;
    Color color = Color.FromArgb(230, 40, 40);
    Shape? cur;
    TextBox? editor;

    CaptureForm()
    {
        // PArgb draws several times faster than Argb, which matters when repainting a 4K frame on every mouse move.
        shot = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(vs.Location, Point.Empty, vs.Size);
        dimmed = new Bitmap(shot);
        using (var g = Graphics.FromImage(dimmed))
        using (var b = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillRectangle(b, 0, 0, vs.Width, vs.Height);

        // Snapshot window rects before our own overlay exists; whole monitors are the fallback.
        windows = [.. Native.VisibleWindows().Concat(Screen.AllScreens.Select(s => s.Bounds))
            .Select(r => { r.Offset(-vs.X, -vs.Y); return r; })];

        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        Bounds = vs; TopMost = true; ShowInTaskbar = false; DoubleBuffered = true; Cursor = Cursors.Cross;
        BuildBar();
    }

    void BuildBar()
    {
        bar.AutoSize = true; bar.AutoSizeMode = AutoSizeMode.GrowAndShrink; bar.WrapContents = false; // GrowOnly would keep the default 100px height bar.BackColor = Color.White; bar.Padding = new Padding(2);
        bar.Visible = false; bar.Cursor = Cursors.Default;
        Button Add(string text, string tip, Action click, Color? fore = null)
        {
            var b = new Button
            {
                Text = text, Width = 34, Height = 30, FlatStyle = FlatStyle.Flat, TabStop = false, Margin = new Padding(1),
                Font = new Font("Segoe UI Symbol", 11f), ForeColor = fore ?? Color.FromArgb(40, 40, 40),
            };
            b.FlatAppearance.BorderSize = 0;
            tips.SetToolTip(b, tip);
            b.Click += (_, _) => click();
            bar.Controls.Add(b);
            return b;
        }
        (Tool, string, string)[] tools =
        [
            (Tool.Rect, "▭", "矩形"), (Tool.Ellipse, "◯", "椭圆"), (Tool.Arrow, "➚", "箭头"),
            (Tool.Pen, "✎", "画笔"), (Tool.Text, "T", "文字"), (Tool.Mosaic, "▦", "马赛克"),
        ];
        foreach (var (t, s, tip) in tools) toolButtons[t] = Add(s, tip, () => SetTool(t));
        Add("↶", "撤销  Ctrl+Z", Undo);
        Color[] colors = [Color.FromArgb(230, 40, 40), Color.FromArgb(37, 99, 235), Color.FromArgb(22, 163, 74), Color.FromArgb(234, 179, 8), Color.Black];
        foreach (var c in colors) Add("●", "颜色", () => { color = c; if (editor != null) editor.ForeColor = c; }, c);
        Add("🧩", "控件  F2（点控件直接贴进截图）", () => HotkeyHost.Widgets?.ShowAtCursor());
        Add("📌", "贴到屏幕  Ctrl+T", Pin);
        Add("💾", "保存并复制  Ctrl+S", Save);
        Add("✓", "复制  Enter / Ctrl+C", CopyAndClose, Color.FromArgb(22, 163, 74));
        Add("✕", "退出  Esc", Close);
        Controls.Add(bar);
    }

    void PlaceBar()
    {
        bar.Visible = true;
        bar.PerformLayout();
        int x = Math.Clamp(sel.Right - bar.Width, 0, Math.Max(0, Width - bar.Width));
        int y = sel.Bottom + 6;
        if (y + bar.Height > Height) y = sel.Top - bar.Height - 6;
        if (y < 0) y = sel.Bottom - bar.Height - 6;
        bar.Location = new Point(x, y);
    }

    void SetTool(Tool t)
    {
        CommitText();
        tool = tool == t ? Tool.None : t;
        foreach (var (k, b) in toolButtons) b.BackColor = k == tool ? Color.FromArgb(219, 234, 254) : Color.White;
    }

    void Undo() { if (shapes.Count > 0) { shapes.RemoveAt(shapes.Count - 1); active = null; Invalidate(); } }

    void Reset()
    {
        CommitText();
        selected = false; sel = Rectangle.Empty; shapes.Clear(); active = null; bar.Visible = false;
        if (tool != Tool.None) SetTool(tool);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        CommitText();
        if (e.Button == MouseButtons.Right)
        {
            if (StickerAt(e.Location) is { } st) { shapes.Remove(st); if (st == active) active = null; Invalidate(); }
            else if (selected) Reset();
            else Close();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        down = e.Location;
        if (!selected) { selecting = true; sel = Rectangle.Empty; return; }
        if (tool == Tool.None)
        {
            if (active != null && (handle = HandleAt(e.Location)) >= 0) { handleStart = Box(active); return; }
            if (StickerAt(e.Location) is { } st) { active = dragging = st; Invalidate(); return; }
            if (active != null) { active = null; Invalidate(); }
            if (sel.Contains(e.Location) && shapes.Count == 0) { moving = true; moveOrigin = sel.Location; }
            return;
        }
        if (!sel.Contains(e.Location)) return;
        if (tool == Tool.Text) { StartText(e.Location); return; }
        cur = new Shape { Tool = tool, Color = color, Pts = { e.Location, e.Location } };
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (selecting) sel = Shape.Span(down, e.Location);
        else if (handle >= 0) ResizeActive(e.Location);
        else if (dragging != null)
        {
            var d = new Size(e.X - down.X, e.Y - down.Y);
            down = e.Location;
            for (int i = 0; i < dragging.Pts.Count; i++) dragging.Pts[i] += d;
        }
        else if (moving) { sel.Location = new Point(moveOrigin.X + e.X - down.X, moveOrigin.Y + e.Y - down.Y); PlaceBar(); }
        else if (cur != null) { if (tool == Tool.Pen) cur.Pts.Add(e.Location); else cur.Pts[^1] = e.Location; }
        else if (!selected) hover = windows.FirstOrDefault(r => r.Contains(e.Location));
        else
        {
            int h = tool == Tool.None && active != null ? HandleAt(e.Location) : -1;
            Cursor = h >= 0 ? HandleCursors[h]
                : tool == Tool.None && (StickerAt(e.Location) != null || shapes.Count == 0 && sel.Contains(e.Location)) ? Cursors.SizeAll
                : Cursors.Cross;
            return;
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (selecting)
        {
            selecting = false;
            if (sel.Width < 4 || sel.Height < 4) sel = hover; // a click picks the hovered window
            sel.Intersect(ClientRectangle);
            if (!sel.IsEmpty) { selected = true; PlaceBar(); }
        }
        moving = false;
        dragging = null;
        handle = -1;
        if (cur != null) { shapes.Add(cur); cur = null; }
        Invalidate();
    }

    Shape? StickerAt(Point p) =>
        shapes.LastOrDefault(s => s.Tool == Tool.Sticker && Shape.Span(s.Pts[0], s.Pts[1]).Contains(p));

    static Rectangle Box(Shape s) => Shape.Span(s.Pts[0], s.Pts[1]);

    // Handles clockwise from top-left: 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L.
    static readonly Cursor[] HandleCursors =
        [Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE];

    static Point[] Handles(Rectangle r)
    {
        int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        return [new(r.Left, r.Top), new(cx, r.Top), new(r.Right, r.Top), new(r.Right, cy),
                new(r.Right, r.Bottom), new(cx, r.Bottom), new(r.Left, r.Bottom), new(r.Left, cy)];
    }

    int HandleAt(Point p)
    {
        var hs = Handles(Box(active!));
        for (int i = 0; i < hs.Length; i++)
            if (Math.Abs(p.X - hs[i].X) <= 6 && Math.Abs(p.Y - hs[i].Y) <= 6) return i;
        return -1;
    }

    // Corners scale proportionally (Shift = free); edges squash/stretch one axis. The opposite side stays put.
    void ResizeActive(Point p)
    {
        var r = handleStart;
        int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
        bool moveL = handle is 0 or 6 or 7, moveR = handle is 2 or 3 or 4, moveT = handle is 0 or 1 or 2, moveB = handle is 4 or 5 or 6;
        if (moveL) left = Math.Min(p.X, right - 8);
        if (moveR) right = Math.Max(p.X, left + 8);
        if (moveT) top = Math.Min(p.Y, bottom - 8);
        if (moveB) bottom = Math.Max(p.Y, top + 8);
        bool corner = handle % 2 == 0;
        if (corner && !ModifierKeys.HasFlag(Keys.Shift))
        {
            // Follow whichever axis moved more, derive the other from the original aspect ratio.
            float sx = (right - left) / (float)r.Width, sy = (bottom - top) / (float)r.Height, k = Math.Max(sx, sy);
            int w = Math.Max(8, (int)(r.Width * k)), h = Math.Max(8, (int)(r.Height * k));
            if (moveL) left = right - w; else right = left + w;
            if (moveT) top = bottom - h; else bottom = top + h;
        }
        active!.Pts[0] = new Point(left, top);
        active.Pts[1] = new Point(right, bottom);
    }

    // Wheel over a sticker resizes it around its center.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // The hovered sticker, else the selected one, so the wheel works without precise aiming.
        if ((StickerAt(e.Location) ?? active) is not { } st) return;
        var r = Shape.Span(st.Pts[0], st.Pts[1]);
        float k = MathF.Pow(1.12f, e.Delta / 120f); // per notch; smooth on precision touchpads
        int w = Math.Max(8, (int)(r.Width * k)), h = Math.Max(8, (int)(r.Height * k));
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        st.Pts[0] = new Point(c.X - w / 2, c.Y - h / 2);
        st.Pts[1] = st.Pts[0] + new Size(w, h);
        Invalidate();
    }

    // Drops an image into the open capture as a movable sticker. False when there is no selection to drop into.
    public static bool CanTakeSticker => current is { selected: true };

    // `density` = image pixels per screen pixel (3 for palette widgets rendered hi-res).
    public static bool TryAddSticker(Bitmap img, float density = 1)
    {
        var f = current;
        if (f == null || !f.selected) return false;
        var s = f.sel;
        float w0 = img.Width / density, h0 = img.Height / density;
        float fit = Math.Min(1f, Math.Min(s.Width * 0.9f / w0, s.Height * 0.9f / h0));
        var size = new Size(Math.Max(1, (int)(w0 * fit)), Math.Max(1, (int)(h0 * fit)));
        var at = new Point(s.X + (s.Width - size.Width) / 2, s.Y + (s.Height - size.Height) / 2);
        f.shapes.Add(f.active = new Shape { Tool = Tool.Sticker, Image = img, Pts = { at, at + size } });
        if (f.tool != Tool.None) f.SetTool(f.tool); // back to "move" mode so the sticker can be dragged right away
        f.Activate();
        f.Invalidate();
        return true;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && selected && tool == Tool.None && sel.Contains(e.Location) && StickerAt(e.Location) == null)
            CopyAndClose();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (editor != null)
        {
            if (key == Keys.Enter) { CommitText(); return true; }
            if (key == Keys.Escape) { editor.Text = ""; CommitText(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }
        switch (key)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Enter: case Keys.Control | Keys.C: CopyAndClose(); return true;
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.Control | Keys.T: Pin(); return true;
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Delete: if (active != null) { shapes.Remove(active); active = null; Invalidate(); } return true;
            case Keys.Control | Keys.V: if (Clip.Get() is { } img && !TryAddSticker(img)) img.Dispose(); return true;
        }
        return base.ProcessCmdKey(ref msg, key);
    }

    void StartText(Point p)
    {
        editor = new TextBox { Location = p, Font = Shape.TextFont, ForeColor = color, BorderStyle = BorderStyle.FixedSingle, Width = 240 };
        Controls.Add(editor);
        editor.BringToFront();
        editor.Focus();
    }

    void CommitText()
    {
        if (editor == null) return;
        if (editor.Text.Length > 0)
            shapes.Add(new Shape { Tool = Tool.Text, Color = editor.ForeColor, Text = editor.Text, Pts = { editor.Location } });
        Controls.Remove(editor);
        editor.Dispose();
        editor = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var focus = selecting || selected ? sel : hover;
        g.DrawImage(dimmed, ClientRectangle, ClientRectangle, GraphicsUnit.Pixel);
        if (focus.IsEmpty) return;
        g.DrawImage(shot, focus, focus, GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.SetClip(focus);
        foreach (var s in cur == null ? shapes : shapes.Append(cur)) s.Draw(g, shot);
        g.ResetClip();
        if (active != null)
        {
            // On-screen only: Compose() never draws this, so it doesn't end up in the copied image.
            var box = Box(active);
            using var dash = new Pen(Color.FromArgb(37, 99, 235), 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dash, box);
            using var edge = new Pen(Color.FromArgb(37, 99, 235), 1.5f);
            foreach (var h in Handles(box))
            {
                var hr = new Rectangle(h.X - 4, h.Y - 4, 8, 8);
                g.FillRectangle(Brushes.White, hr);
                g.DrawRectangle(edge, hr);
            }
        }
        g.SmoothingMode = SmoothingMode.None;
        using (var p = new Pen(Color.FromArgb(37, 99, 235), 2)) g.DrawRectangle(p, focus);
        var label = $"{focus.Width} × {focus.Height}";
        var at = new Point(focus.X, Math.Max(0, focus.Y - 22));
        var size = TextRenderer.MeasureText(label, Font);
        using (var b = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.FillRectangle(b, new Rectangle(at, size + new Size(8, 4)));
        TextRenderer.DrawText(g, label, Font, new Point(at.X + 4, at.Y + 2), Color.White);
    }

    Bitmap Compose()
    {
        CommitText();
        var bmp = new Bitmap(sel.Width, sel.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(shot, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
        g.TranslateTransform(-sel.X, -sel.Y);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in shapes) s.Draw(g, shot);
        return bmp;
    }

    void CopyAndClose()
    {
        if (!selected) return;
        using (var b = Compose()) Clip.Set(b);
        Close();
    }

    void Pin()
    {
        if (!selected) return;
        var b = Compose();
        var at = new Point(sel.X + vs.X, sel.Y + vs.Y);
        Close();
        new PinForm(b, at).Show();
    }

    void Save()
    {
        if (!selected) return;
        using var b = Compose();
        Hide();
        Clip.SaveAs(b, null);
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        shot.Dispose(); dimmed.Dispose(); tips.Dispose();
        foreach (var sh in shapes) sh.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
