// UIPaste: a tray app with Snipaste's basics plus a widget palette.
//   F1     capture (window hover / region drag, annotate, Enter copies, Ctrl+S saves and copies, Ctrl+T pins)
//   F3     pin the clipboard (image, or text rendered as an image)
//   F2     widget palette (Material 3 / iOS components; click copies, Shift+click pins)
using System.Runtime.InteropServices;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["--dump", var dir])
        {
            var b = new WidgetBrowser();
            b.Shown += async (_, _) =>
            {
                try { await b.Ready; await b.DumpAll(dir); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                Application.Exit();
            };
            Application.Run(b);
            return;
        }
        // A second copy would fail to register hotkeys and lock WebView2's data folder.
        using var single = new Mutex(true, "UIPaste.SingleInstance", out bool first);
        if (!first) return;
        Application.Run(new HotkeyHost());
    }
}

class HotkeyHost : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    const uint MOD_NOREPEAT = 0x4000;

    static readonly (int Id, uint Mods, Keys Key, string Label)[] Hotkeys =
    [
        (1, MOD_NOREPEAT, Keys.F1, "F1 截图"),
        (2, MOD_NOREPEAT, Keys.F3, "F3 贴图"),
        (3, MOD_NOREPEAT, Keys.F2, "F2 控件"), // Alt+Q is commonly taken by other apps
    ];

    public static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
    public static WidgetBrowser? Widgets { get; private set; }
    readonly WidgetBrowser widgets = Widgets = new();
    readonly NotifyIcon tray;

    public HotkeyHost()
    {
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; Opacity = 0;
        var menu = new ContextMenuStrip();
        menu.Items.Add("截图\tF1", null, (_, _) => CaptureForm.Start());
        menu.Items.Add("贴图\tF3", null, (_, _) => PinClipboard());
        menu.Items.Add("控件\tAlt+Q", null, (_, _) => widgets.ShowAtCursor());
        menu.Items.Add("退出", null, (_, _) => Application.Exit());
        tray = new NotifyIcon { Icon = AppIcon, Text = "UIPaste", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) CaptureForm.Start(); };
        widgets.Notify += msg => tray.ShowBalloonTip(1000, "UIPaste", msg, ToolTipIcon.None);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var taken = Hotkeys.Where(k => !RegisterHotKey(Handle, k.Id, k.Mods, (uint)k.Key)).Select(k => k.Label).ToList();
        Log($"start; hotkeys taken: [{string.Join(", ", taken)}]");
        if (taken.Count > 0)
            tray.ShowBalloonTip(4000, "UIPaste：快捷键被占用",
                string.Join("、", taken) + " 已被别的程序占用（Snipaste 在运行？），可先用托盘菜单。", ToolTipIcon.Warning);
    }

    static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UIPaste", "log.txt");
    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); } catch { }
    }

    void PinClipboard()
    {
        var bmp = Clip.Get();
        if (bmp == null) { tray.ShowBalloonTip(1000, "UIPaste", "剪贴板里没有图片或文字", ToolTipIcon.None); return; }
        var p = Cursor.Position;
        new PinForm(bmp, new Point(p.X - bmp.Width / 2, p.Y - bmp.Height / 2)).Show();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY) Log($"hotkey {m.WParam}");
        if (m.Msg == WM_HOTKEY)
            switch ((int)m.WParam)
            {
                case 1: CaptureForm.Start(); break;
                case 2: PinClipboard(); break;
                case 3: widgets.ShowAtCursor(); break;
            }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var k in Hotkeys) UnregisterHotKey(Handle, k.Id);
        tray.Visible = false;
        base.OnFormClosed(e);
    }
}
