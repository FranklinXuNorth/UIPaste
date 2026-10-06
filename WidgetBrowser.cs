using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Alt+Q palette: renders real open-source components (Material Web, Framework7 iOS) in WebView2
// and screenshots the clicked one with a transparent background via the DevTools protocol.
class WidgetBrowser : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly TaskCompletionSource ready = new();
    bool busy;

    public Task Ready => ready.Task;
    public event Action<string>? Notify;

    public WidgetBrowser()
    {
        Text = "UIPaste Widgets";
        Icon = HotkeyHost.AppIcon;
        FormBorderStyle = FormBorderStyle.SizableToolWindow; TopMost = true; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Controls.Add(web);
        Deactivate += (_, _) => { if (!busy) Hide(); };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Logical px (needs the handle's DPI): wide enough that 375px phone bars never touch the scrollbar.
        Size = LogicalToDeviceUnits(new Size(600, 680));
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UIPaste", "WebView2");
            await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, dataDir));
            var core = web.CoreWebView2;
            await core.CallDevToolsProtocolMethodAsync("Emulation.setDefaultBackgroundColorOverride",
                """{"color":{"r":0,"g":0,"b":0,"a":0}}""");
            core.WebMessageReceived += OnMessage;
            var nav = new TaskCompletionSource();
            core.NavigationCompleted += (_, _) => nav.TrySetResult();
            core.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "widgets.html")).AbsoluteUri);
            await nav.Task;
            // Components come from a CDN; wait until they are defined and fonts are in.
            for (int i = 0; i < 100 && await core.ExecuteScriptAsync("isReady()") != "true"; i++) await Task.Delay(150);
            ready.TrySetResult();
        }
        catch (Exception ex) { HotkeyHost.Log("webview init failed: " + ex.Message); ready.TrySetException(ex); }
    }

    public void ShowAtCursor()
    {
        HotkeyHost.Log("palette show");
        var p = Cursor.Position;
        var area = Screen.FromPoint(p).WorkingArea;
        Location = new Point(Math.Clamp(p.X + 12, area.Left, area.Right - Width), Math.Clamp(p.Y + 12, area.Top, area.Bottom - Height));
        Show();
        Activate();
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var m = JsonDocument.Parse(e.WebMessageAsJson).RootElement;
        if (m.TryGetProperty("close", out _)) { Hide(); return; }
        busy = true;
        try
        {
            // Stickers are rendered at 3x so text stays sharp when scaled up inside the capture.
            bool sticker = CaptureForm.CanTakeSticker;
            var bmp = await Shoot(m, sticker ? StickerScale : 1);
            var name = m.GetProperty("name").GetString();
            Hide();
            if (sticker && CaptureForm.TryAddSticker(bmp, StickerScale)) Notify?.Invoke($"{name} 已贴进截图");
            else if (m.GetProperty("pin").GetBoolean()) new PinForm(bmp, Cursor.Position).Show();
            else { using (bmp) Clip.Set(bmp); Notify?.Invoke($"{name} 已复制"); }
        }
        catch (Exception ex) { Notify?.Invoke("截取失败：" + ex.Message); }
        finally { busy = false; }
    }

    // rect is in CSS px relative to the viewport; the page itself never scrolls (only #scroll does),
    // so viewport coordinates are page coordinates.
    const int StickerScale = 3;

    public async Task<Bitmap> Shoot(JsonElement rect, double scale = 1)
    {
        const double pad = 8; // room for elevation shadows
        double x = Math.Max(0, rect.GetProperty("x").GetDouble() - pad), y = Math.Max(0, rect.GetProperty("y").GetDouble() - pad);
        double w = rect.GetProperty("w").GetDouble() + pad * 2, h = rect.GetProperty("h").GetDouble() + pad * 2;
        var core = web.CoreWebView2;
        await core.ExecuteScriptAsync("document.documentElement.classList.add('capturing')");
        try
        {
            var args = JsonSerializer.Serialize(new
            {
                format = "png",
                clip = new { x, y, width = w, height = h, scale }, // WebView2 already renders at the screen's DPI, so 1 = on-screen size
            });
            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", args);
            var png = Convert.FromBase64String(JsonDocument.Parse(json).RootElement.GetProperty("data").GetString()!);
            using var ms = new MemoryStream(png);
            using var t = new Bitmap(ms);
            return new Bitmap(t);
        }
        finally { await core.ExecuteScriptAsync("document.documentElement.classList.remove('capturing')"); }
    }

    // --dump: render every widget to <dir>/NN_name.png, to eyeball the library without clicking.
    public async Task DumpAll(string dir)
    {
        Directory.CreateDirectory(dir);
        var core = web.CoreWebView2;
        int n = int.Parse(await core.ExecuteScriptAsync("document.querySelectorAll('.w').length"));
        for (int i = 0; i < n; i++)
        {
            var rect = JsonDocument.Parse(await core.ExecuteScriptAsync($"rectOf({i})")).RootElement;
            await Task.Delay(120); // let scrollIntoView settle
            rect = JsonDocument.Parse(await core.ExecuteScriptAsync($"rectOf({i})")).RootElement;
            using var bmp = await Shoot(rect);
            bmp.Save(Path.Combine(dir, $"{i:00}_{rect.GetProperty("name").GetString()}.png"));
        }
    }
}
