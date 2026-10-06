using System.Drawing.Imaging;
using System.Runtime.InteropServices;

static class Clip
{
    // PNG keeps alpha (Snipaste and most design tools read it); the plain bitmap is the fallback.
    public static void Set(Bitmap bmp)
    {
        var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        var data = new DataObject();
        data.SetData("PNG", false, png);
        data.SetImage(bmp);
        Clipboard.SetDataObject(data, true);
    }

    public static Bitmap? Get()
    {
        var data = Clipboard.GetDataObject();
        if (data?.GetData("PNG") is MemoryStream png)
            using (var t = new Bitmap(png)) return new Bitmap(t);
        if (Clipboard.GetImage() is { } img)
            using (img) return new Bitmap(img);
        if (Clipboard.ContainsText()) return RenderText(Clipboard.GetText());
        return null;
    }

    static Bitmap RenderText(string s)
    {
        if (s.Length > 4000) s = s[..4000];
        using var font = new Font("Microsoft YaHei UI", 14f, GraphicsUnit.Pixel);
        var size = TextRenderer.MeasureText(s, font);
        var bmp = new Bitmap(size.Width + 20, size.Height + 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        TextRenderer.DrawText(g, s, font, new Point(10, 8), Color.FromArgb(30, 30, 30));
        return bmp;
    }

    // Saving also copies, so a saved shot can be pasted right away.
    public static void SaveAs(Bitmap bmp, IWin32Window? owner)
    {
        Set(bmp);
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp",
            FileName = $"UIPaste_{DateTime.Now:yyyyMMdd_HHmmss}.png",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        var fmt = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png,
        };
        bmp.Save(dlg.FileName, fmt);
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src,
        ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmInt(IntPtr h, int attr, out int v, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmRect(IntPtr h, int attr, out RECT v, int size);

    // Paints a per-pixel-alpha bitmap onto a WS_EX_LAYERED window (this is what lets pins be transparent).
    public static void Blit(IntPtr hwnd, Bitmap bmp, Point pos, byte alpha)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
        try
        {
            var dst = new POINT { X = pos.X, Y = pos.Y };
            var size = new SIZE { Cx = bmp.Width, Cy = bmp.Height };
            var src = new POINT();
            var blend = new BLENDFUNCTION { Alpha = alpha, Format = 1 }; // AC_SRC_ALPHA
            UpdateLayeredWindow(hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2); // ULW_ALPHA
        }
        finally
        {
            SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // Visible top-level window frames, topmost first: used for hover-to-select in capture.
    public static List<Rectangle> VisibleWindows()
    {
        var list = new List<Rectangle>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            if (DwmInt(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true; // DWMWA_CLOAKED
            if (DwmRect(h, 9, out var r, Marshal.SizeOf<RECT>()) != 0) return true;  // DWMWA_EXTENDED_FRAME_BOUNDS
            var rc = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rc.Width > 10 && rc.Height > 10) list.Add(rc);
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
