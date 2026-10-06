using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// A floating, always-on-top image (Snipaste's "贴图"). Drag to move, wheel to zoom,
// Ctrl+wheel for opacity, double-click / Esc to close, right-click for the menu.
class PinForm : Form
{
    readonly Bitmap img;
    float scale = 1;
    byte alpha = 255;
    Point grab;
    bool dragging;

    public PinForm(Bitmap img, Point at)
    {
        this.img = img;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        Bounds = new Rectangle(at, img.Size);
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制\tCtrl+C", null, (_, _) => Clip.Set(img));
        menu.Items.Add("另存为（并复制）\tCtrl+S", null, (_, _) => Clip.SaveAs(img, this));
        menu.Items.Add("原始大小", null, (_, _) => { scale = 1; alpha = 255; Redraw(Location); });
        menu.Items.Add("关闭\tEsc", null, (_, _) => Close());
        ContextMenuStrip = menu;
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80; return cp; } // WS_EX_LAYERED | WS_EX_TOOLWINDOW
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); Redraw(Location); Activate(); }

    void Redraw(Point pos)
    {
        int w = Math.Max(1, (int)(img.Width * scale)), h = Math.Max(1, (int)(img.Height * scale));
        using var frame = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame))
        {
            g.InterpolationMode = scale == 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, new Rectangle(0, 0, w, h));
        }
        Native.Blit(Handle, frame, pos, alpha);
        Bounds = new Rectangle(pos, new Size(w, h));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { dragging = true; grab = e.Location; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) Location = new Point(Left + e.X - grab.X, Top + e.Y - grab.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e) => dragging = false;
    protected override void OnMouseDoubleClick(MouseEventArgs e) => Close();

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            alpha = (byte)Math.Clamp(alpha + (e.Delta > 0 ? 25 : -25), 30, 255);
            Redraw(Location);
            return;
        }
        float next = Math.Clamp(scale * (e.Delta > 0 ? 1.1f : 1 / 1.1f), 0.1f, 8f), ratio = next / scale;
        scale = next;
        // Zoom around the cursor, not the top-left corner.
        Redraw(new Point((int)(Left + e.X - e.X * ratio), (int)(Top + e.Y - e.Y * ratio)));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Escape: Close(); break;
            case Keys.Control | Keys.C: Clip.Set(img); break;
            case Keys.Control | Keys.S: Clip.SaveAs(img, this); break;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { img.Dispose(); base.OnFormClosed(e); }
}
