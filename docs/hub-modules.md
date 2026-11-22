# HUB modules: the PC side of an app

NEXDECK HUB (C# 5, .NET Framework 4, WinForms) is a core plus modules. A module is a class that extends
`HubApp`. The core gives it the serial connection while its app is open on the board and a page in the window.

Start from `hub/NexDeckHub.Sample.cs` (`HelloPcApp`, the PC side of `firmware/templates/hello_pc`).

## Lifecycle

```csharp
public class MyApp : HubApp {
  public override string Id    { get { return "my_app"; } }   // /apps/my_app.bin on the card
  public override string Hello { get { return "my_app"; } }   // name the board sends in EV HELLO
  public override string Title { get { return "My App"; } }

  public override void Start() { }                                // once, when the HUB opens (start feed threads here)
  public override void OnConnect(Link link, string[] hello) { }   // app opened on the board (or sent HELLO again)
  public override void Loop(Link link) { Thread.Sleep(20); }      // repeatedly while it is open
  public override bool OnEvent(Link link, string[] ev) { return false; }  // ev[1] = event name
  public override string CoalesceKey(string[] ev) { return null; } // same key in one batch → only the last runs

  public override bool HasSettings { get { return true; } }
  public override void BuildSettings(AppPage page) { }            // build the page (window thread)
  public override void Save() { }                                 // "Salvar" button (window thread)
  public override void Tick() { }                                 // every second while the page is visible
}
```

`OnConnect`, `Loop` and `OnEvent` run on the **board thread**; never touch WinForms controls there (store values
in fields and show them in `Tick`). The core already answers `HELLO`/`APPS`, pings every 5 s and sends queued
blocks; an exception (e.g. `Worker.Must(false)`) drops the connection and the core reconnects.

### Talking to the board

```csharp
Worker.Must(link.Send("MSG\t" + Txt.Clean(text, 120)));  // line + checksum, waits for K; Must throws on failure
link.SendBlob("PHOTO", 0, jpegBytes);                      // checksummed blocks (also W.QueueBlob from any thread)
W.PutFile("/my_app/data.txt", bytes, pct => { });          // file to the SD card (works in apps and in the launcher)
W.Request(() => { /* runs on the board thread; use W.Link here */ });
W.PumpEvents();                                            // inside long loops: read incoming events
W.ResyncRequested = true;                                  // OnConnect runs again (e.g. after Save)
W.RunningApp == Id                                         // is my app open on the board?
```

### Settings and storage

- Simple values: `Cfg.Get(Id, "key", "default")`, `Cfg.Set(Id, "key", value)` (saved in `config.json` when the
  page's *Salvar* runs, or call `Cfg.Save()`).
- Typed fields: add them to `public partial class Config { ... }` in your module file; set defaults in
  `partial void Defaults()` (one implementation per build).
- Your own files: `Path.Combine(Config.Dir, "my_app")`.

### Registering modules

```csharp
public static partial class HubApps {
  static partial void RegisterApps(List<HubApp> list) {
    list.Add(new MyApp());
  }
}
```

Build with your folder: `hub\build.ps1 -Apps C:\my\hub-apps -Icons C:\my\icons`. Icons: `<Id>.png` (88×88).
Files your module needs at runtime (a native DLL, a font, data) can be embedded with `-Resources file1,file2`;
read them back with `Assembly.GetExecutingAssembly().GetManifestResourceStream("res.<file name>")`.

## Building a page

`AppPage` gives you the frame (back button, icon, title, "Abrir na placa", section menu, *Salvar*). Add sections
and cards:

```csharp
public override void BuildSettings(AppPage page) {
  var flow = page.Section("Mensagem", "news");              // section title + menu glyph
  var card = AppPage.NewCard(flow, 100);                    // card 700 px wide
  AppPage.Text(card, "Texto", 18, 16, 400, 22, 10.5f, true);
  field = new Field(Cfg.Get(Id, "msg", ""), 664, "Placeholder") { Location = new Point(18, 46) };
  card.Controls.Add(field);
  var choice = AppPage.Choice(card, new[] { "A", "B", "C" }, 0, 100, 120);  // segmented buttons
  page.FolderButtons(card, folderField);                    // "Escolher..." + "Abrir pasta de imagens"
  flow.Controls.Add(new Expander("Como usar", AppPage.PW, 80) { Icon = "info" });  // collapsible help
}
```

Components (`hub/NexDeckHub.Ui.cs`): `Card`, `Field` (text box with placeholder), `FlatButton` (`Primary`,
`Danger`, icon-only with `Glyph`), `Toggle`, `ComboFrame`/`DarkCombo`, `DarkList` (items `"Code · Name"`),
`Chip`, `Expander`, `LiveText` (shows a shimmer skeleton while `Text` is empty), `Progress`, `Ask.YesNo(...)`,
`Glyphs.Draw(g, "home|grid|news|chart|calendar|photo|gear|sun|music|volume|clock|back|chevron|plus|play|close|search|hand|info|folder", rect, color)`,
`Theme` (colors, fonts, `Round`, `FillRound`), `Shimmer.Bar(g, rect, radius)`.

Loading states: never show an empty or "Carregando..." label; use `LiveText` (or `Shimmer.Bar`) until the data
arrives, a determinate `Progress` for long operations (installing), and keep the last known data visible.

## Utilities in the core

`Txt.Clean(s, maxBytes)` (fonts-safe text), `Web.Get/GetBytes(url, browserUserAgent)`, `Img.CoverJpeg/SquareJpeg/TrimBorders/FixOrientation`,
`PhotoSource.Next(web, folder)` (480×480 JPEG from a folder or the internet), `Icons.Get(path, size)` /
`Icons.ToRgb565A(bitmap)` (program icons for the board), `InstalledApps.List()` (Start menu apps),
`QrCode.Encode(text)` → `.Size`, `[row, col]`, `.ToHex()` (send to the board), `.ToBitmap(scale)`,
`AppPackage.Load(file)`, `Log.Write(text)`.

## Other entry points

- `NEXDECKHub.exe --shots <folder>` renders the window (apps home, settings, every module section) to PNGs:
  handy to review a page without a board.
- `NEXDECKHub.exe --pack <file.nexapp>` validates a package.
