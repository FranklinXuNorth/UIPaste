# UIPaste

A tiny Windows tray tool for sketching UI ideas on top of screenshots: Snipaste-style capture and pin, plus a palette of real UI components you can drop in as stickers.

## Hotkeys

| Key | What it does |
| --- | --- |
| `F1` | Capture. Hover to pick a window or drag a region, then annotate. |
| `F2` | Widget palette: Material 3 and iOS-style components. |
| `F3` | Pin the clipboard (an image, or text rendered as an image) as a floating window. |

### In a capture (`F1`)

- **Tools:** rectangle, ellipse, arrow, pen, text, mosaic. Undo with `Ctrl+Z`.
- **Output:**
  - `Enter` / `Ctrl+C`: copy.
  - `Ctrl+S`: save, which also copies.
  - `Ctrl+T`: pin to the screen.
  - `Esc`: quit.
  - Right-click: reselect the region.
- **Stickers:** press `F2` (or 🧩 on the toolbar) and click a widget to drop it into the selection. `Ctrl+V` drops in the clipboard.
  - Drag a sticker to move it.
  - Corner handles scale it proportionally. Hold `Shift` to resize freely.
  - Edge handles squash or stretch one axis.
  - The mouse wheel zooms it.
  - Right-click or `Delete` removes it.

  Widgets are rendered at 3× resolution, so text stays sharp when you scale it up.

### In the palette (`F2`)

- Click: copy as a transparent PNG, or drop into the open capture.
- `Shift`+click: pin to the screen.
- Right-click: edit the widget's text (labels, values, placeholders, icon names). Edits are remembered. **Reset** restores the defaults.

### Pins

- Drag to move.
- Wheel: zoom.
- `Ctrl`+wheel: change opacity.
- Double-click or `Esc`: close.
- Right-click: menu.

## Build and run

You need the .NET 10 SDK and the WebView2 runtime (Windows 11 already has it).

```
dotnet run
dotnet publish -c Release -o dist   # then run dist/UIPaste.exe
```

Only one copy runs at a time. If a hotkey is already taken by another app (for example, Snipaste on F1/F3), the tray shows a warning, and the tray menu still works. The log is written to `%LOCALAPPDATA%\UIPaste\log.txt`.

`UIPaste.exe --dump <dir>` renders every palette widget to a PNG file.

## Widget sources

The palette loads these open-source libraries from jsDelivr / Google Fonts at runtime. None of them are bundled.

- [Material Web](https://github.com/material-components/material-web) (Apache-2.0), Material Symbols, and Roboto, all by Google.
- [Framework7](https://github.com/framework7io/framework7) and Framework7 Icons (MIT), for the iOS look. Apple doesn't publish an open-source UI kit.

## License

MIT
