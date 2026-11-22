// NEXDECK HUB — janela, componentes visuais e ícone da bandeja (público).
// Visual próprio (tons de concreto, cartões arredondados) desenhado à mão em WinForms. A tela inicial
// mostra os apps do cartão da placa; clicar num app com módulo abre a página dele (AppPage). Enquanto
// um dado não chega, o lugar dele mostra um esqueleto com brilho (shimmer). Os componentes (Card,
// Field, Toggle, FlatButton, Expander, LiveText, Chip, DarkList...) ficam à disposição dos módulos.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexDeck {

// ============================================================================ cores e formas
public static class Theme {
  public static readonly Color Side = Color.FromArgb(27, 27, 26), Back = Color.FromArgb(36, 36, 35), Card = Color.FromArgb(46, 46, 44);
  public static readonly Color Border = Color.FromArgb(60, 60, 58), Field = Color.FromArgb(31, 31, 30), Hover = Color.FromArgb(54, 54, 52);
  public static readonly Color Sel = Color.FromArgb(64, 64, 61), Text = Color.FromArgb(236, 235, 232), Muted = Color.FromArgb(163, 161, 156);
  public static readonly Color Faint = Color.FromArgb(112, 110, 106), Accent = Color.FromArgb(226, 224, 218), AccentText = Color.FromArgb(30, 30, 29);
  public static readonly Color Good = Color.FromArgb(126, 196, 140), Bad = Color.FromArgb(222, 120, 108), Screen = Color.FromArgb(20, 20, 19);
  public static readonly Color Bone = Color.FromArgb(58, 58, 56);  // esqueleto do carregamento

  public static Font F(float size, bool bold = false) { return new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size); }

  public static GraphicsPath Round(RectangleF r, float rad) {
    var p = new GraphicsPath();
    float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
    if (d <= 0.5f) { p.AddRectangle(r); return p; }
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
  }

  public static void FillRound(Graphics g, Color c, RectangleF r, float rad) {
    using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
  }

  public static void Hq(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
  }

  // ícone do app (.ico embutido no .exe) no tamanho mais próximo
  public static Icon AppIcon(int size) {
    try {
      using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("NexDeck.ico"))
        if (s != null) return new Icon(s, new Size(size, size));
    } catch { }
    return SystemIcons.Application;
  }

  // ícone de um app da placa (88 px): os do pacote instalado ou os que vêm no .exe
  public static Bitmap AppImage(string name) {
    try {
      string f = Path.Combine(Config.Dir, "icones", name + ".png");
      if (File.Exists(f)) using (var b = new Bitmap(f)) return new Bitmap(b);
      using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app." + name + ".png"))
        if (s != null) return new Bitmap(s);
      foreach (var asm in HubApps.All.Select(a => a.GetType().Assembly).Distinct())  // ícone dentro do módulo .dll
        using (var s = asm.GetManifestResourceStream("app." + name + ".png"))
          if (s != null) return new Bitmap(s);
    } catch { }
    return null;
  }

  public static string AppLabel(string name) {
    var m = HubApps.ById(name);
    if (m != null) return m.Title;
    var parts = name.Replace('-', '_').Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
    return string.Join(" ", parts.Select(p => p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : p));
  }
}

// relógio único do brilho do carregamento: todos os esqueletos andam juntos
public static class Shimmer {
  public static readonly System.Windows.Forms.Timer Tick = new System.Windows.Forms.Timer { Interval = 33 };
  static Shimmer() { Tick.Start(); }
  public static float Phase { get { return (Environment.TickCount % 1500) / 1500f; } }

  // barra cinza com um brilho claro passando da esquerda para a direita
  public static void Bar(Graphics g, RectangleF r, float radius) {
    Theme.FillRound(g, Theme.Bone, r, radius);
    float w = Math.Max(80, r.Width * 0.45f), x = r.X - w + (r.Width + 2 * w) * Phase;
    using (var clip = Theme.Round(r, radius)) {
      var old = g.Clip;
      g.SetClip(clip, CombineMode.Intersect);
      using (var br = new LinearGradientBrush(new RectangleF(x - 1, r.Y, w + 2, r.Height), Color.Transparent, Color.Transparent, 0f)) {
        br.InterpolationColors = new ColorBlend {
          Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
          Positions = new[] { 0f, 0.5f, 1f }
        };
        g.FillRectangle(br, x, r.Y, w, r.Height);
      }
      g.Clip = old;
    }
  }
}

// pequenos ícones vetoriais (sem depender de fontes de símbolos)
public static class Glyphs {
  public static void Draw(Graphics g, string kind, RectangleF r, Color c) {
    float s = Math.Min(r.Width, r.Height), x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
    Func<float, float, PointF> P = (u, v) => new PointF(x + u * s, y + v * s);
    using (var pen = new Pen(c, Math.Max(1.4f, s / 11)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
    using (var br = new SolidBrush(c)) {
      switch (kind) {
        case "home":
          g.DrawLines(pen, new[] { P(.12f, .5f), P(.5f, .14f), P(.88f, .5f) });
          g.DrawLines(pen, new[] { P(.24f, .4f), P(.24f, .86f), P(.76f, .86f), P(.76f, .4f) });
          break;
        case "grid":
          foreach (var o in new[] { P(.14f, .14f), P(.56f, .14f), P(.14f, .56f), P(.56f, .56f) })
            using (var pa = Theme.Round(new RectangleF(o.X, o.Y, s * .3f, s * .3f), s * .07f)) g.DrawPath(pen, pa);
          break;
        case "news":
          using (var pa = Theme.Round(new RectangleF(x + s * .1f, y + s * .16f, s * .8f, s * .68f), s * .1f)) g.DrawPath(pen, pa);
          g.FillRectangle(br, x + s * .22f, y + s * .3f, s * .22f, s * .18f);
          g.DrawLine(pen, P(.54f, .33f), P(.78f, .33f));
          g.DrawLine(pen, P(.54f, .46f), P(.78f, .46f));
          g.DrawLine(pen, P(.22f, .62f), P(.78f, .62f));
          break;
        case "chart":
          g.DrawLines(pen, new[] { P(.12f, .72f), P(.36f, .48f), P(.54f, .6f), P(.86f, .26f) });
          g.DrawLines(pen, new[] { P(.66f, .26f), P(.86f, .26f), P(.86f, .46f) });
          g.DrawLine(pen, P(.12f, .86f), P(.88f, .86f));
          break;
        case "calendar":
          using (var pa = Theme.Round(new RectangleF(x + s * .12f, y + s * .2f, s * .76f, s * .66f), s * .1f)) g.DrawPath(pen, pa);
          g.DrawLine(pen, P(.12f, .4f), P(.88f, .4f));
          g.DrawLine(pen, P(.34f, .12f), P(.34f, .26f));
          g.DrawLine(pen, P(.66f, .12f), P(.66f, .26f));
          g.FillEllipse(br, x + s * .3f, y + s * .56f, s * .1f, s * .1f);
          g.FillEllipse(br, x + s * .52f, y + s * .56f, s * .1f, s * .1f);
          break;
        case "photo":
          using (var pa = Theme.Round(new RectangleF(x + s * .1f, y + s * .18f, s * .8f, s * .64f), s * .1f)) g.DrawPath(pen, pa);
          g.DrawLines(pen, new[] { P(.18f, .74f), P(.42f, .48f), P(.6f, .66f), P(.7f, .56f), P(.84f, .72f) });
          g.FillEllipse(br, x + s * .62f, y + s * .3f, s * .12f, s * .12f);
          break;
        case "gear": {  // engrenagem cheia com 8 dentes e furo no meio
          var pts = new List<PointF>();
          for (int i = 0; i < 8; i++) {
            double a0 = i * Math.PI / 4;
            foreach (var t in new[] { new[] { -0.30, 0.31 }, new[] { -0.17, 0.44 }, new[] { 0.17, 0.44 }, new[] { 0.30, 0.31 } })
              pts.Add(P(.5f + (float)(Math.Cos(a0 + t[0]) * t[1]), .5f + (float)(Math.Sin(a0 + t[0]) * t[1])));
          }
          using (var path = new GraphicsPath(FillMode.Alternate)) {
            path.AddPolygon(pts.ToArray());
            path.AddEllipse(x + s * .36f, y + s * .36f, s * .28f, s * .28f);
            g.FillPath(br, path);
          }
          break;
        }
        case "info":
          g.DrawEllipse(pen, x + s * .1f, y + s * .1f, s * .8f, s * .8f);
          g.FillEllipse(br, x + s * .45f, y + s * .26f, s * .1f, s * .1f);
          g.DrawLine(pen, P(.5f, .44f), P(.5f, .72f));
          break;
        case "folder":
          g.DrawLines(pen, new[] { P(.12f, .78f), P(.12f, .24f), P(.4f, .24f), P(.48f, .34f), P(.88f, .34f), P(.88f, .78f), P(.12f, .78f) });
          break;
        case "sun":
          g.DrawEllipse(pen, x + s * .32f, y + s * .32f, s * .36f, s * .36f);
          for (int i = 0; i < 8; i++) {
            double a = i * Math.PI / 4;
            g.DrawLine(pen, P(.5f + (float)Math.Cos(a) * .3f, .5f + (float)Math.Sin(a) * .3f), P(.5f + (float)Math.Cos(a) * .42f, .5f + (float)Math.Sin(a) * .42f));
          }
          break;
        case "music":
          g.FillEllipse(br, x + s * .2f, y + s * .62f, s * .24f, s * .2f);
          g.DrawLine(pen, P(.42f, .72f), P(.42f, .16f));
          g.DrawLines(pen, new[] { P(.42f, .16f), P(.74f, .26f), P(.74f, .4f), P(.42f, .3f) });
          break;
        case "volume":
          g.FillPolygon(br, new[] { P(.14f, .4f), P(.3f, .4f), P(.5f, .2f), P(.5f, .8f), P(.3f, .6f), P(.14f, .6f) });
          g.DrawArc(pen, x + s * .44f, y + s * .34f, s * .2f, s * .32f, -60, 120);
          g.DrawArc(pen, x + s * .44f, y + s * .2f, s * .4f, s * .6f, -60, 120);
          break;
        case "clock":
          g.DrawEllipse(pen, x + s * .14f, y + s * .14f, s * .72f, s * .72f);
          g.DrawLines(pen, new[] { P(.5f, .3f), P(.5f, .5f), P(.64f, .6f) });
          break;
        case "back":
          g.DrawLines(pen, new[] { P(.6f, .2f), P(.3f, .5f), P(.6f, .8f) });
          break;
        case "chevron":
          g.DrawLines(pen, new[] { P(.3f, .38f), P(.5f, .6f), P(.7f, .38f) });
          break;
        case "plus":
          g.DrawLine(pen, P(.5f, .2f), P(.5f, .8f));
          g.DrawLine(pen, P(.2f, .5f), P(.8f, .5f));
          break;
        case "play":
          g.FillPolygon(br, new[] { P(.32f, .2f), P(.32f, .8f), P(.8f, .5f) });
          break;
        case "stop":
          Theme.FillRound(g, c, new RectangleF(P(.26f, .26f), new SizeF(s * .48f, s * .48f)), s * .09f);
          break;
        case "close":
          g.DrawLine(pen, P(.28f, .28f), P(.72f, .72f));
          g.DrawLine(pen, P(.72f, .28f), P(.28f, .72f));
          break;
        case "search":
          g.DrawEllipse(pen, x + s * .16f, y + s * .16f, s * .48f, s * .48f);
          g.DrawLine(pen, P(.58f, .58f), P(.84f, .84f));
          break;
        case "hand":
          using (var pa = Theme.Round(new RectangleF(x + s * .14f, y + s * .14f, s * .72f, s * .72f), s * .1f)) g.DrawPath(pen, pa);
          g.FillRectangle(br, x + s * .2f, y + s * .2f, s * .22f, s * .6f);
          break;
      }
    }
  }
}

// ============================================================================ controles
public class FlatButton : Control {
  public bool Primary, Danger;
  public string Glyph;  // botão só de ícone quando não há texto
  bool hover, down;
  public FlatButton(string text, bool primary = false) {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    Text = text;
    Primary = primary;
    Font = Theme.F(9.5f, true);
    Cursor = Cursors.Hand;
    Size = new Size(140, 38);
  }
  protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
  protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
  protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
  protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
  // um rótulo vizinho com caixa mais larga que o texto não pode cobrir o canto do botão
  protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); if (Parent != null) BringToFront(); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
    Color fill = Primary ? Theme.Accent : (Danger ? Color.FromArgb(70, 44, 40) : Theme.Hover);
    if (!Enabled) fill = Theme.Hover;
    else if (down) fill = Primary ? Color.FromArgb(200, 198, 192) : Theme.Sel;
    else if (hover) fill = Primary ? Color.FromArgb(244, 242, 236) : Color.FromArgb(Math.Min(255, fill.R + 12), Math.Min(255, fill.G + 12), Math.Min(255, fill.B + 12));
    var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
    Theme.FillRound(g, fill, r, 9);
    if (!Primary || !Enabled) using (var p = Theme.Round(r, 9)) using (var pen = new Pen(Danger ? Color.FromArgb(120, 64, 56) : Theme.Border)) g.DrawPath(pen, p);
    Color fg = !Enabled ? Theme.Faint : Primary ? Theme.AccentText : (Danger ? Color.FromArgb(240, 170, 160) : Theme.Text);
    if (string.IsNullOrEmpty(Text) && Glyph != null) { Glyphs.Draw(g, Glyph, new RectangleF(Width / 2f - 9, Height / 2f - 9, 18, 18), fg); return; }
    TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
  }
}

public class Toggle : Control {
  bool on;
  public event EventHandler CheckedChanged;
  public bool Checked { get { return on; } set { if (on == value) return; on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
  public Toggle() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    Size = new Size(46, 26);
    Cursor = Cursors.Hand;
  }
  protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
    Theme.FillRound(g, on ? Theme.Accent : Color.FromArgb(78, 78, 75), new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f);
    float d = Height - 8, kx = on ? Width - d - 4 : 4;
    using (var b = new SolidBrush(on ? Theme.AccentText : Color.FromArgb(206, 204, 198))) g.FillEllipse(b, kx, 4, d, d);
  }
}

// cartão arredondado; os filhos ficam com a cor dele
public class Card : Panel {
  public bool Dashed, Hot;
  public Card() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    BackColor = Theme.Card;
    Margin = new Padding(0, 0, 0, 12);
  }
  protected override void OnPaintBackground(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
    var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
    if (!Dashed) Theme.FillRound(g, Theme.Card, r, 12);
    using (var p = Theme.Round(r, 12)) using (var pen = new Pen(Hot ? Theme.Accent : Theme.Border, Hot ? 1.5f : 1f)) {
      if (Dashed) pen.DashPattern = new[] { 4f, 3f };
      g.DrawPath(pen, p);
    }
  }
}

// cartão que abre e fecha ao clicar no título (informação que não precisa ficar sempre à vista)
public class Expander : Card {
  public readonly Panel Body = new Panel();
  public string Icon;  // ícone antes do título (ex.: "info")
  readonly string title;
  bool open;
  const int HEAD = 48;
  public Expander(string title, int w, int bodyH) {
    this.title = title;
    Size = new Size(w, HEAD);
    Body.SetBounds(18, HEAD - 4, w - 36, bodyH);
    Body.BackColor = Theme.Card;
    Body.Visible = false;
    Controls.Add(Body);
    Cursor = Cursors.Hand;
  }
  protected override void OnClick(EventArgs e) {
    open = !open;
    Body.Visible = open;
    Height = open ? HEAD + Body.Height + 10 : HEAD;
    Invalidate();
    base.OnClick(e);
  }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    int tx = 18;
    if (Icon != null) { Glyphs.Draw(g, Icon, new RectangleF(16, 14, 20, 20), Theme.Muted); tx = 44; }
    TextRenderer.DrawText(g, title, Theme.F(10.5f, true), new Point(tx, 14), Theme.Text);
    var r = new RectangleF(Width - 38, 14, 20, 20);
    var st = g.Save();
    if (open) { g.TranslateTransform(r.X + 10, r.Y + 10); g.RotateTransform(180); g.TranslateTransform(-r.X - 10, -r.Y - 10); }
    Glyphs.Draw(g, "chevron", r, Theme.Muted);
    g.Restore(st);
  }
}

// texto que, enquanto não chegou, aparece como esqueleto com brilho
public class LiveText : Control {
  public int Lines = 1;
  public LiveText() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    Font = Theme.F(9.5f);
    ForeColor = Theme.Text;
    Shimmer.Tick.Tick += (s, e) => { if (string.IsNullOrEmpty(Text) && Visible) Invalidate(); };
  }
  protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
    if (string.IsNullOrEmpty(Text)) {
      float h = Math.Min(12, Font.Height * 0.6f), gap = Font.Height;
      for (int i = 0; i < Lines; i++)
        Shimmer.Bar(g, new RectangleF(0, 3 + i * gap, (Width - 2) * (i == Lines - 1 && Lines > 1 ? 0.6f : 0.92f), h), h / 2);
      return;
    }
    TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
  }
}

// caixa de texto com borda arredondada
public class Field : Panel {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
  public readonly TextBox Box = new TextBox();
  string cue;
  public Field(string text, int w, string placeholder = null) {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    BackColor = Theme.Field;
    Size = new Size(w, 38);
    Margin = new Padding(0);
    Box.BorderStyle = BorderStyle.None;
    Box.BackColor = Theme.Field;
    Box.ForeColor = Theme.Text;
    Box.Font = Theme.F(10);
    Box.Text = text ?? "";
    Box.SetBounds(12, 10, w - 24, 20);
    Box.GotFocus += (s, e) => Invalidate();
    Box.LostFocus += (s, e) => Invalidate();
    cue = placeholder;
    Box.HandleCreated += (s, e) => { if (cue != null) SendMessage(Box.Handle, 0x1501, (IntPtr)1, cue); };  // EM_SETCUEBANNER
    Controls.Add(Box);
    Cursor = Cursors.IBeam;
    Click += (s, e) => Box.Focus();
  }
  public override string Text { get { return Box.Text; } set { Box.Text = value; } }
  protected override void OnPaintBackground(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
    var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
    Theme.FillRound(g, Theme.Field, r, 8);
    using (var p = Theme.Round(r, 8)) using (var pen = new Pen(Box.Focused ? Theme.Muted : Theme.Border)) g.DrawPath(pen, p);
  }
}

public class DarkCombo : ComboBox {
  public DarkCombo() {
    DrawMode = DrawMode.OwnerDrawFixed;
    DropDownStyle = ComboBoxStyle.DropDownList;
    FlatStyle = FlatStyle.Flat;
    BackColor = Theme.Field;
    ForeColor = Theme.Text;
    Font = Theme.F(10);
    ItemHeight = 26;
    MaxDropDownItems = 14;
  }
  protected override void OnDrawItem(DrawItemEventArgs e) {
    bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
    using (var b = new SolidBrush(sel ? Theme.Sel : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
    if (e.Index >= 0)
      TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
  }
}

// moldura para a caixa de escolha: esconde a borda e o botão claros do estilo Flat (a caixa fica
// recortada por um painel interno) e desenha borda e seta no tom da janela
public class ComboFrame : Panel {
  public readonly DarkCombo Combo = new DarkCombo();
  const int ARROW = 24;
  public ComboFrame(int w) {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    int h = Combo.Height - 2;
    Size = new Size(w, h);
    Cursor = Cursors.Hand;
    var clip = new Panel { Bounds = new Rectangle(1, 1, w - 2 - ARROW, h - 2), BackColor = Theme.Field };
    Combo.SetBounds(-2, -2, clip.Width + 2 + SystemInformation.VerticalScrollBarWidth + 4, Combo.Height);
    Combo.DropDownWidth = w;
    clip.Controls.Add(Combo);
    Controls.Add(clip);
    Combo.DropDown += (s, e) => Invalidate();
    Combo.DropDownClosed += (s, e) => Invalidate();
    Click += (s, e) => { Combo.Focus(); Combo.DroppedDown = true; };
  }
  protected override void OnPaintBackground(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Combo.DroppedDown ? Theme.Muted : Theme.Border);
    using (var b = new SolidBrush(Theme.Field)) g.FillRectangle(b, 1, 1, Width - 2, Height - 2);
    Glyphs.Draw(g, "chevron", new RectangleF(Width - ARROW + 2, Height / 2f - 9, 18, 18), Theme.Muted);
  }
}

// lista escura (resultados de busca)
public class DarkList : ListBox {
  public DarkList() {
    DrawMode = DrawMode.OwnerDrawFixed;
    BorderStyle = BorderStyle.None;
    BackColor = Theme.Field;
    ForeColor = Theme.Text;
    Font = Theme.F(10);
    ItemHeight = 30;
    IntegralHeight = false;
    Cursor = Cursors.Hand;
  }
  protected override void OnDrawItem(DrawItemEventArgs e) {
    bool sel = (e.State & DrawItemState.Selected) != 0;
    using (var b = new SolidBrush(sel ? Theme.Sel : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
    if (e.Index < 0) return;
    // item "Código · Nome": código em destaque; sem " · ", texto apagado (ex.: "Buscando...")
    string text = Items[e.Index].ToString();
    var r = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height);
    int dot = text.IndexOf(" · ");
    if (dot < 0) { TextRenderer.DrawText(e.Graphics, text, Font, r, Theme.Muted, TextFormatFlags.VerticalCenter); return; }
    using (var bold = Theme.F(10, true)) {
      TextRenderer.DrawText(e.Graphics, text.Substring(0, dot), bold, r, Theme.Text, TextFormatFlags.VerticalCenter);
      TextRenderer.DrawText(e.Graphics, text.Substring(dot + 3), Font, new Rectangle(r.X + 78, r.Y, r.Width - 78, r.Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
  }
}

// etiqueta com "×" para tirar (ações acompanhadas)
public class Chip : Control {
  public event Action<Chip> Removed;
  bool hover;
  public Chip(string text) {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    Text = text;
    Font = Theme.F(9.5f, true);
    Size = new Size(TextRenderer.MeasureText(text, Font).Width + 46, 32);
    Margin = new Padding(0, 0, 8, 8);
    Cursor = Cursors.Hand;
  }
  protected override void OnMouseMove(MouseEventArgs e) { bool h = e.X > Width - 30; if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
  protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnMouseClick(MouseEventArgs e) { var h = Removed; if (e.X > Width - 30 && h != null) h(this); base.OnMouseClick(e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
    Theme.FillRound(g, Theme.Sel, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f);
    TextRenderer.DrawText(g, Text, Font, new Point(14, 7), Theme.Text);
    Glyphs.Draw(g, "close", new RectangleF(Width - 26, 9, 14, 14), hover ? Theme.Text : Theme.Muted);
  }
}

public class NavItem : Control {
  public string Glyph;
  public bool Selected;
  public Image Picture;
  bool hover;
  public NavItem(string text, string glyph) {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    Text = text;
    Glyph = glyph;
    Font = Theme.F(10);
    Size = new Size(196, 42);
    Cursor = Cursors.Hand;
  }
  protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
  protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Theme.Side);
    if (Selected || hover) Theme.FillRound(g, Selected ? Theme.Sel : Theme.Hover, new RectangleF(0, 0, Width, Height), 9);
    if (Selected) Theme.FillRound(g, Theme.Accent, new RectangleF(0, 11, 3, Height - 22), 1.5f);
    Color c = Selected ? Theme.Text : Theme.Muted;
    if (Glyph != null) Glyphs.Draw(g, Glyph, new RectangleF(14, 11, 20, 20), c);
    TextRenderer.DrawText(g, Text, Selected ? Theme.F(10, true) : Font, new Rectangle(46, 0, Width - 50, Height), c, TextFormatFlags.VerticalCenter);
  }
}

// cartão de um app da placa (tela inicial): nome, situação, ícone grande e o botão de ação
public class AppCard : Card {
  public string AppName;
  public bool Skeleton, Running, Configurable;
  public Image Picture;
  public string Sub = "";
  public readonly FlatButton Action = new FlatButton("");
  public readonly FlatButton Settings = new FlatButton("") { Glyph = "gear" };  // configurações (app com módulo que não está aberto)
  public event Action Activated;
  public AppCard() {
    Size = new Size(210, 272);
    Margin = new Padding(0, 0, 20, 20);
    Cursor = Cursors.Hand;
    Action.SetBounds(Width - 58, Height - 58, 40, 40);
    Action.Visible = false;
    Controls.Add(Action);
    Settings.SetBounds(Width - 106, Height - 58, 40, 40);
    Settings.Visible = false;
    Controls.Add(Settings);
    Shimmer.Tick.Tick += (s, e) => { if (Skeleton && Visible) Invalidate(); };
  }
  protected override void OnMouseEnter(EventArgs e) { if (!Skeleton) { Hot = true; Invalidate(); } base.OnMouseEnter(e); }
  protected override void OnMouseLeave(EventArgs e) { if (!ClientRectangle.Contains(PointToClient(MousePosition))) { Hot = false; Invalidate(); } base.OnMouseLeave(e); }
  protected override void OnClick(EventArgs e) { var h = Activated; if (!Skeleton && h != null) h(); base.OnClick(e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    if (Skeleton) {
      Shimmer.Bar(g, new RectangleF(20, 22, 120, 16), 8);
      Shimmer.Bar(g, new RectangleF(20, 48, 80, 10), 5);
      Shimmer.Bar(g, new RectangleF(53, 92, 104, 104), 24);
      return;
    }
    using (var f = Theme.F(13, true)) TextRenderer.DrawText(g, Theme.AppLabel(AppName), f, new Rectangle(18, 16, Width - 36, 28), Theme.Text, TextFormatFlags.EndEllipsis);
    if (Running) using (var b = new SolidBrush(Theme.Good)) g.FillEllipse(b, 20, 52, 7, 7);
    TextRenderer.DrawText(g, Sub, Theme.F(8.5f), new Point(Running ? 31 : 18, 47), Theme.Muted);
    var ir = new RectangleF(53, 88, 104, 104);
    using (var sh = Theme.Round(new RectangleF(ir.X + 2, ir.Y + 8, ir.Width - 4, ir.Height - 4), 24))
    using (var br = new PathGradientBrush(sh) { CenterColor = Color.FromArgb(70, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } })
      g.FillPath(br, sh);  // sombra: o ícone "flutua", como no NEXLauncher
    if (Picture != null) {
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.DrawImage(Picture, ir);
    } else {
      Theme.FillRound(g, Theme.Sel, ir, 24);
      using (var f = Theme.F(26, true)) TextRenderer.DrawText(g, Theme.AppLabel(AppName).Substring(0, 1), f, Rectangle.Round(ir), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
  }
}

// "Instalar novo app"
public class InstallCard : Card {
  public event Action Activated;
  public InstallCard() {
    Dashed = true;
    Size = new Size(210, 272);
    Margin = new Padding(0, 0, 20, 20);
    Cursor = Cursors.Hand;
  }
  protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
  protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnClick(EventArgs e) { var h = Activated; if (h != null) h(); base.OnClick(e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    var c = new RectangleF(Width / 2f - 30, 92, 60, 60);
    using (var p = Theme.Round(c, 30)) using (var pen = new Pen(Hot ? Theme.Accent : Theme.Muted, 1.6f)) g.DrawPath(pen, p);
    Glyphs.Draw(g, "plus", new RectangleF(c.X + 16, c.Y + 16, 28, 28), Hot ? Theme.Text : Theme.Muted);
    using (var f = Theme.F(11, true)) TextRenderer.DrawText(g, "Instalar novo app", f, new Rectangle(0, 172, Width, 24), Theme.Text, TextFormatFlags.HorizontalCenter);
    TextRenderer.DrawText(g, "arquivo .nexapp", Theme.F(8.5f), new Rectangle(0, 198, Width, 20), Theme.Faint, TextFormatFlags.HorizontalCenter);
  }
}

// barra de andamento (instalação: operação longa, com porcentagem)
public class Progress : Control {
  int pct;
  public int Value { get { return pct; } set { pct = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
  public Progress() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Height = 8; }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    Theme.Hq(g);
    g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
    Theme.FillRound(g, Theme.Field, new RectangleF(0, 0, Width, Height), Height / 2f);
    if (pct > 0) Theme.FillRound(g, Theme.Accent, new RectangleF(0, 0, Math.Max(Height, Width * pct / 100f), Height), Height / 2f);
  }
}

// ============================================================================ abrir junto com o Windows
public static class Autostart {
  const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", NAME = "NEXDECK HUB";
  public static string[] OLD = new string[0];  // nomes de versões antigas do app (a entrada delas é trocada pela do HUB)
  static string Cmd { get { return "\"" + Application.ExecutablePath + "\" --startup"; } }
  public static bool Get() {
    using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(NAME) != null;
  }
  public static void Set(bool on) {
    using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) {
      if (on) k.SetValue(NAME, Cmd);
      else if (k.GetValue(NAME) != null) k.DeleteValue(NAME);
      foreach (var o in OLD) if (k.GetValue(o) != null) k.DeleteValue(o);
    }
  }
  // troca as entradas de nomes antigos pela do NEXDECK HUB e corrige o caminho se o app mudou de pasta
  public static void Migrate() {
    try {
      string cur;
      bool old;
      using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) {
        if (k == null) return;
        cur = k.GetValue(NAME) as string;
        old = OLD.Any(o => k.GetValue(o) != null);
      }
      if (old || (cur != null && !string.Equals(cur, Cmd, StringComparison.OrdinalIgnoreCase))) {
        Set(true);
        Log.Write("abrir com o Windows: entrada atualizada para " + Cmd);
      }
    } catch (Exception e) { Log.Write("abrir com o Windows: " + e.Message); }
  }
}

// ============================================================================ pergunta Sim/Não
public class Ask : Form {
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
  public static bool YesNo(IWin32Window owner, string question, Image pic) {
    using (var f = new Ask(question, pic)) return f.ShowDialog(owner) == DialogResult.Yes;
  }
  Ask(string question, Image pic) {
    Text = "NEXDECK HUB";
    Icon = Theme.AppIcon(32);
    BackColor = Theme.Card;
    ForeColor = Theme.Text;
    Font = Theme.F(9.5f);
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MaximizeBox = MinimizeBox = false;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.CenterParent;
    ClientSize = new Size(420, 176);
    int x = 24;
    if (pic != null) { Controls.Add(new PictureBox { Image = pic, Bounds = new Rectangle(24, 28, 56, 56), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Card }); x = 96; }
    Controls.Add(new Label { Text = question, Font = Theme.F(12, true), Bounds = new Rectangle(x, 34, 400 - x, 50), BackColor = Theme.Card });
    var yes = new FlatButton("Sim", true) { Bounds = new Rectangle(272, 114, 124, 38) };
    var no = new FlatButton("Não") { Bounds = new Rectangle(138, 114, 124, 38) };
    yes.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };
    no.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };
    Controls.Add(yes);
    Controls.Add(no);
  }
  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    try { int on = 1, col = ColorTranslator.ToWin32(Theme.Side); DwmSetWindowAttribute(Handle, 20, ref on, 4); DwmSetWindowAttribute(Handle, 35, ref col, 4); } catch { }
  }
}

// ============================================================================ janela de instalação
public class InstallDialog : Form {
  readonly Worker worker;
  readonly AppPackage pkg;
  readonly Progress bar = new Progress();
  readonly Label msg = new Label();
  readonly FlatButton go = new FlatButton("Instalar", true), close = new FlatButton("Fechar");
  readonly Action onChange;

  public InstallDialog(Worker w, AppPackage p) {
    worker = w;
    pkg = p;
    Text = "Instalar app";
    Icon = Theme.AppIcon(32);
    BackColor = Theme.Card;
    ForeColor = Theme.Text;
    Font = Theme.F(9.5f);
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MaximizeBox = MinimizeBox = false;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.CenterParent;
    ClientSize = new Size(440, 250);
    var pic = new PictureBox { Image = p.Preview, Bounds = new Rectangle(24, 24, 72, 72), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Card };
    Controls.Add(pic);
    Controls.Add(new Label { Text = p.Name, Font = Theme.F(15, true), AutoSize = true, Location = new Point(112, 28), BackColor = Theme.Card });
    Controls.Add(new Label { Text = (p.Version.Length > 0 ? "versão " + p.Version + "  ·  " : "") + (p.Bin.Length / 1024) + " KB", Font = Theme.F(9), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(114, 62), BackColor = Theme.Card });
    bar.SetBounds(24, 126, 392, 8);
    bar.Visible = false;
    Controls.Add(bar);
    msg.SetBounds(24, 144, 392, 40);
    msg.ForeColor = Theme.Muted;
    msg.BackColor = Theme.Card;
    Controls.Add(msg);
    go.SetBounds(276, 192, 140, 38);
    close.SetBounds(124, 192, 140, 38);
    Controls.Add(go);
    Controls.Add(close);
    close.Click += (s, e) => Close();
    go.Click += (s, e) => {
      go.Enabled = false;
      bar.Visible = true;
      bar.Value = 0;
      worker.Install(pkg);
    };
    onChange = () => { try { BeginInvoke((Action)UpdateState); } catch { } };
    worker.InstallChanged += onChange;
    UpdateState();
  }

  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    try { int on = 1, col = ColorTranslator.ToWin32(Theme.Side); DwmSetWindowAttribute(Handle, 20, ref on, 4); DwmSetWindowAttribute(Handle, 35, ref col, 4); } catch { }
  }
  protected override void OnFormClosed(FormClosedEventArgs e) { worker.InstallChanged -= onChange; base.OnFormClosed(e); }

  void UpdateState() {
    bool busy = !go.Enabled;
    if (!busy) {
      go.Enabled = worker.Connected;
      msg.Text = worker.Connected ? "Vai para o cartão da placa e aparece no NEXLauncher." : "Conecte a placa pela USB para instalar.";
      return;
    }
    if (worker.InstallPct >= 0) bar.Value = worker.InstallPct;
    msg.Text = worker.InstallMsg + (worker.InstallPct >= 0 && worker.InstallPct < 100 ? "  " + worker.InstallPct + "%" : "");
    msg.ForeColor = worker.InstallPct == -2 ? Theme.Bad : worker.InstallPct == 100 ? Theme.Good : Theme.Muted;
    if (worker.InstallPct == 100 || worker.InstallPct == -2) { close.Primary = true; close.Invalidate(); go.Text = worker.InstallPct == 100 ? "Instalado" : "Tentar de novo"; go.Enabled = worker.InstallPct == -2; }
  }
}

// ============================================================================ página de um app
// Moldura comum das configurações de um app: menu lateral (voltar, ícone, nome, "Abrir na placa" e as
// seções), conteúdo e Salvar. O módulo cria as seções em BuildSettings com Section(...).
public class AppPage : Panel {
  public const int PW = 700;  // largura do conteúdo
  public readonly HubForm Form;
  public readonly HubApp App;
  readonly Panel side = new Panel(), content = new Panel(), foot = new Panel();
  readonly List<NavItem> nav = new List<NavItem>();
  readonly List<Control> sections = new List<Control>();
  readonly FlatButton open;
  readonly Label saved;

  public AppPage(HubForm form, HubApp app) {
    Form = form;
    App = app;
    Dock = DockStyle.Fill;
    BackColor = Theme.Back;
    side.Dock = DockStyle.Left;
    side.Width = 236;
    side.BackColor = Theme.Side;
    var back = new NavItem("Apps", "back") { Location = new Point(18, 16) };
    back.Click += (s, e) => form.ShowApps();
    side.Controls.Add(back);
    side.Controls.Add(new PictureBox { Image = Theme.AppImage(app.Id), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(24, 76, 44, 44), BackColor = Theme.Side });
    side.Controls.Add(new Label { Text = app.Title, Font = Theme.F(13, true), ForeColor = Theme.Text, AutoSize = true, Location = new Point(78, 86), BackColor = Theme.Side });
    open = new FlatButton("Abrir na placa") { Bounds = new Rectangle(18, 132, 196, 38) };
    open.Click += (s, e) => app.W.OpenApp(app.Id);
    side.Controls.Add(open);
    content.Dock = DockStyle.Fill;
    content.BackColor = Theme.Back;
    foot.Dock = DockStyle.Bottom;
    foot.Height = 64;
    foot.BackColor = Theme.Back;
    var save = new FlatButton("Salvar", true) { Bounds = new Rectangle(36 + PW - 150, 10, 150, 40) };
    save.Click += (s, e) => {
      try { app.Save(); app.Cfg.Save(); Saved("Salvo", true); } catch (Exception ex) { Saved("Não salvou: " + ex.Message, false); }
    };
    foot.Controls.Add(save);
    saved = new Label { Bounds = new Rectangle(36, 20, PW - 170, 22), TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Good, BackColor = Theme.Back };
    foot.Controls.Add(saved);
    Controls.Add(content);
    Controls.Add(foot);
    Controls.Add(side);
    app.BuildSettings(this);
    if (sections.Count < 2) foreach (var n in nav) n.Visible = false;  // uma seção só: sem menu
    ShowSection(0);
  }

  // nova seção (página com título e cartões em coluna; os que abrem e fecham empurram os de baixo)
  public FlowLayoutPanel Section(string title, string glyph) {
    var flow = new FlowLayoutPanel { Bounds = new Rectangle(36, 0, PW + 30, 560), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Back, Visible = false };
    flow.Padding = new Padding(0, 26, 0, 10);
    flow.Controls.Add(new Label { Text = title, Font = Theme.F(19, true), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
    content.Controls.Add(flow);
    int idx = sections.Count;
    var n = new NavItem(title, glyph) { Location = new Point(18, 186 + idx * 46) };
    n.Click += (s, e) => ShowSection(idx);
    nav.Add(n);
    side.Controls.Add(n);
    sections.Add(flow);
    return flow;
  }

  public void ShowSection(int i) {
    for (int k = 0; k < sections.Count; k++) { sections[k].Visible = k == i; nav[k].Selected = k == i; nav[k].Invalidate(); }
    saved.Text = "";
  }

  public int SectionCount { get { return sections.Count; } }

  public void Saved(string msg, bool ok) { saved.ForeColor = ok ? Theme.Good : Theme.Bad; saved.Text = msg; }

  public void Tick() {
    open.Visible = App.W.Mode != BoardMode.None && App.W.RunningApp != App.Id;
    App.Tick();
  }

  public static Card NewCard(Control flow, int h) {
    var c = new Card { Size = new Size(PW, h) };
    flow.Controls.Add(c);
    return c;
  }

  public static Label Text(Control parent, string text, int x, int y, int w, int h, float size = 9.5f, bool bold = false, Color? color = null) {
    var l = new Label { Text = text, Font = Theme.F(size, bold), ForeColor = color ?? Theme.Text, Bounds = new Rectangle(x, y, w, h), BackColor = parent.BackColor };
    parent.Controls.Add(l);
    return l;
  }

  // "Escolher..." e "Abrir pasta de imagens" ao lado do campo de uma pasta (y = topo do campo)
  public void FolderButtons(Card c, Field f, int y = 46) {
    f.Width = 340;
    f.Box.Width = 340 - 24;
    var browse = new FlatButton("Escolher...") { Bounds = new Rectangle(368, y, 110, 38) };
    browse.Click += (s, e) => {
      using (var d = new FolderBrowserDialog { SelectedPath = f.Text })
        if (d.ShowDialog(Form) == DialogResult.OK) f.Text = d.SelectedPath;
    };
    c.Controls.Add(browse);
    var openDir = new FlatButton("Abrir pasta de imagens") { Bounds = new Rectangle(488, y, 194, 38) };
    openDir.Click += (s, e) => { try { Process.Start("explorer.exe", "\"" + (Directory.Exists(f.Text) ? f.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)) + "\""); } catch { } };
    c.Controls.Add(openDir);
  }

  // escolha entre opções (botões lado a lado; o escolhido fica claro)
  public static FlatButton[] Choice(Card c, string[] labels, int selected, int y, int w, Action<int> changed = null) {
    var bs = new FlatButton[labels.Length];
    for (int i = 0; i < labels.Length; i++) {
      var b = new FlatButton(labels[i]) { Bounds = new Rectangle(18 + i * (w + 10), y, w, 38), Primary = i == selected };
      int idx = i;
      b.Click += (s, e) => {
        foreach (var o in bs) { o.Primary = o == b; o.Invalidate(); }
        if (changed != null) changed(idx);
      };
      bs[i] = b;
      c.Controls.Add(b);
    }
    return bs;
  }
  public static int Chosen(FlatButton[] bs) { return Math.Max(0, Array.FindIndex(bs, b => b.Primary)); }
}

// ============================================================================ janela principal
public class HubForm : Form {
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

  readonly Worker worker;
  readonly Config cfg;
  readonly TrayApp tray;
  readonly Panel top = new Panel(), body = new Panel();
  readonly Panel appsView = new Panel(), settingsView = new Panel();
  readonly Dictionary<HubApp, AppPage> appPages = new Dictionary<HubApp, AppPage>();
  readonly Label crumb = new Label();
  readonly LiveText boardChip = new LiveText();
  readonly FlowLayoutPanel cards = new FlowLayoutPanel();
  readonly LiveText appsSub = new LiveText();
  readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
  string shownApps = "";
  bool dotOn;
  const int PW = AppPage.PW;

  public HubForm(Worker w, TrayApp t) {
    worker = w;
    cfg = w.Cfg;
    tray = t;
    Text = "NEXDECK HUB";
    Icon = Theme.AppIcon(32);
    Font = Theme.F(9.5f);
    BackColor = Theme.Back;
    ForeColor = Theme.Text;
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;
    StartPosition = FormStartPosition.CenterScreen;
    ClientSize = new Size(1040, 700);
    DoubleBuffered = true;

    // barra de cima: marca, onde estou, placa, configurações
    top.Dock = DockStyle.Top;
    top.Height = 76;
    top.BackColor = Theme.Side;
    top.Controls.Add(new PictureBox { Image = Theme.AppIcon(48).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(26, 18, 40, 40), BackColor = Theme.Side });
    var brand = new Label { Text = "NEXDECK HUB", Font = Theme.F(14, true), ForeColor = Theme.Text, AutoSize = true, Location = new Point(76, 22), BackColor = Theme.Side, Cursor = Cursors.Hand };
    brand.Click += (s, e) => ShowApps();
    top.Controls.Add(brand);
    crumb.SetBounds(220, 27, 300, 24);
    crumb.Font = Theme.F(11);
    crumb.ForeColor = Theme.Muted;
    crumb.BackColor = Theme.Side;
    top.Controls.Add(crumb);
    var chip = new Card { Bounds = new Rectangle(700, 18, 260, 40) };
    var dot = new Panel { Bounds = new Rectangle(16, 15, 10, 10), BackColor = Theme.Card };
    dot.Paint += (s, e) => { Theme.Hq(e.Graphics); using (var b = new SolidBrush(dotOn ? Theme.Good : Theme.Faint)) e.Graphics.FillEllipse(b, 0, 0, 9, 9); };
    chip.Controls.Add(dot);
    boardChip.SetBounds(34, 11, 214, 20);
    boardChip.Font = Theme.F(9.5f, true);
    chip.Controls.Add(boardChip);
    top.Controls.Add(chip);
    var gear = new FlatButton("") { Glyph = "gear", Bounds = new Rectangle(974, 18, 40, 40) };
    gear.Click += (s, e) => ShowSettings();
    top.Controls.Add(gear);

    body.Dock = DockStyle.Fill;
    body.BackColor = Theme.Back;
    foreach (var v in new[] { appsView, settingsView }) { v.Dock = DockStyle.Fill; v.BackColor = Theme.Back; v.Visible = false; body.Controls.Add(v); }
    Controls.Add(body);
    Controls.Add(top);
    BuildApps();
    BuildSettings();

    timer.Tick += (s, e) => { dotOn = worker.Connected; dot.Invalidate(); Tick(); };
    timer.Start();
    worker.AppsChanged += () => { try { BeginInvoke((Action)FillApps); } catch { } };
    Tick();
    ShowApps();
  }

  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    try {  // barra de título escura (Windows 10/11) na cor da barra de cima
      int on = 1, col = ColorTranslator.ToWin32(Theme.Side);
      DwmSetWindowAttribute(Handle, 20, ref on, 4);
      DwmSetWindowAttribute(Handle, 35, ref col, 4);
    } catch { }
  }
  protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); base.OnFormClosed(e); }

  void ShowView(Panel v, string where) {
    foreach (Control p in body.Controls) p.Visible = p == v;
    crumb.Text = where.Length > 0 ? "›   " + where : "";
  }
  public void ShowApps() { ShowView(appsView, ""); }
  void ShowSettings() { ShowView(settingsView, "Configurações"); }

  public AppPage ShowApp(HubApp app, int section = 0) {
    AppPage page;
    if (!appPages.TryGetValue(app, out page)) {
      page = new AppPage(this, app) { Visible = false };
      body.Controls.Add(page);
      appPages[app] = page;
    }
    ShowView(page, app.Title);
    page.ShowSection(section);
    return page;
  }

  // ---------------------------------------------------------------- tela inicial: apps da placa
  void BuildApps() {
    appsView.Controls.Add(new Label { Text = "Apps na placa", Font = Theme.F(19, true), ForeColor = Theme.Text, AutoSize = true, Location = new Point(38, 30) });
    appsSub.SetBounds(42, 72, 400, 20);
    appsSub.Font = Theme.F(10);
    appsSub.ForeColor = Theme.Muted;
    appsSub.BackColor = Theme.Back;
    appsView.Controls.Add(appsSub);
    cards.SetBounds(40, 112, 980, 520);
    cards.BackColor = Theme.Back;
    cards.AutoScroll = true;
    appsView.Controls.Add(cards);
    FillApps();
  }

  void FillApps() {
    var list = worker.Apps;
    if (list == null && !string.IsNullOrEmpty(cfg.LastApps))  // placa ainda não respondeu: a última lista conhecida
      list = cfg.LastApps.Split(';').Select(a => a.Split(',')).Where(a => a.Length == 2).Select(a => new Worker.BoardApp { Name = a[0], Size = long.Parse(a[1]) }).ToList();
    string key = (list == null ? "?" : string.Join(";", list.Select(a => a.Name + "," + a.Size))) + "|" + worker.Mode + "|" + worker.RunningApp + "|" + worker.Connected;
    if (key == shownApps) return;
    shownApps = key;
    if (worker.Apps != null) { cfg.LastApps = string.Join(";", worker.Apps.Select(a => a.Name + "," + a.Size)); try { cfg.Save(); } catch { } }
    cards.SuspendLayout();
    foreach (Control c in cards.Controls.Cast<Control>().ToList()) { cards.Controls.Remove(c); c.Dispose(); }
    if (list == null && worker.Connected) {  // lendo o cartão: esqueletos com brilho
      for (int i = 0; i < 3; i++) cards.Controls.Add(new AppCard { Skeleton = true });
    } else if (list == null) {  // placa nunca vista
      var empty = new Card { Size = new Size(640, 120) };
      var plug = new Panel { Bounds = new Rectangle(24, 34, 52, 52), BackColor = Theme.Card };
      plug.Paint += (s, e) => { Theme.Hq(e.Graphics); Glyphs.Draw(e.Graphics, "grid", new RectangleF(4, 4, 44, 44), Theme.Muted); };
      empty.Controls.Add(plug);
      AppPage.Text(empty, "Conecte a placa NEXDECK pela USB", 96, 36, 520, 26, 12, true);
      AppPage.Text(empty, "Os apps do cartão aparecem aqui.", 98, 66, 520, 22, 9.5f, false, Theme.Muted);
      cards.Controls.Add(empty);
    } else {
      foreach (var a in list.OrderBy(a => { var m = HubApps.ById(a.Name); return m != null && m.HasSettings ? 0 : 1; }).ThenBy(a => a.Name)) {
        var module = HubApps.ById(a.Name);
        var card = new AppCard { AppName = a.Name, Picture = Theme.AppImage(a.Name) };
        card.Configurable = module != null && module.HasSettings;
        card.Running = a.Name == worker.RunningApp;
        card.Sub = card.Running ? "Aberto agora" : (a.Size / 1024) + " KB";
        string name = a.Name;
        // app com configurações: abre a página dele; sem configurações: pergunta se abre na placa
        if (card.Configurable) card.Activated += () => ShowApp(module);
        else card.Activated += () => { if (worker.Mode != BoardMode.None && Ask.YesNo(this, "Deseja abrir esse app na placa?", card.Picture)) worker.OpenApp(name); };
        if (card.Running) {  // aberto agora: configurações (se tiver) e parar (volta ao NEXLauncher da placa)
          var stop = card.Configurable ? card.Settings : card.Action;
          stop.Glyph = "stop";
          stop.Click += (s, e) => { if (Ask.YesNo(this, "Fechar " + Theme.AppLabel(name) + " e voltar ao NEXLauncher na placa?", card.Picture)) worker.ReturnToLauncher(); };
          stop.Visible = true;
          new ToolTip().SetToolTip(stop, "Fechar e voltar ao NEXLauncher");
          if (card.Configurable) {
            card.Action.Glyph = "gear";
            card.Action.Primary = true;
            card.Action.Click += (s, e) => ShowApp(module);
            card.Action.Visible = true;
          }
        } else {
          card.Action.Glyph = "play";
          card.Action.Click += (s, e) => worker.OpenApp(name);
          card.Action.Visible = worker.Mode != BoardMode.None && !card.Running;
          if (card.Configurable) {  // configurações sem precisar abrir o app na placa
            card.Settings.Click += (s, e) => ShowApp(module);
            card.Settings.Visible = true;
            if (!card.Action.Visible) card.Settings.Left = card.Action.Left;
          }
        }
        cards.Controls.Add(card);
      }
      var inst = new InstallCard();
      inst.Activated += InstallFromFile;
      cards.Controls.Add(inst);
    }
    cards.ResumeLayout();
  }

  void InstallFromFile() {
    using (var d = new OpenFileDialog { Filter = "App do NEXDECK|*.nexapp", Title = "Instalar app na placa" }) {
      if (d.ShowDialog(this) != DialogResult.OK) return;
      AppPackage p;
      try { p = AppPackage.Load(d.FileName); }
      catch (Exception e) { MessageBox.Show(this, "Esse arquivo não é um app do NEXDECK: " + e.Message, "NEXDECK HUB", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
      using (var dlg = new InstallDialog(worker, p)) dlg.ShowDialog(this);
    }
  }

  // ---------------------------------------------------------------- configurações do HUB
  void BuildSettings() {
    var flow = new FlowLayoutPanel { Bounds = new Rectangle(40, 0, PW + 30, 620), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Back };
    flow.Padding = new Padding(0, 26, 0, 10);
    flow.Controls.Add(new Label { Text = "Configurações", Font = Theme.F(19, true), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
    settingsView.Controls.Add(flow);
    var c = AppPage.NewCard(flow, 136);
    var auto = new Toggle { Location = new Point(18, 26) };
    try { auto.Checked = Autostart.Get(); } catch { }
    auto.CheckedChanged += (s, e) => { try { Autostart.Set(auto.Checked); cfg.AutostartChosen = true; cfg.Save(); } catch { } };
    c.Controls.Add(auto);
    AppPage.Text(c, "Abrir junto com o Windows", 80, 28, 500, 22, 10.5f, true);
    var awake = new Toggle { Location = new Point(18, 84), Checked = cfg.KeepAwake };
    awake.CheckedChanged += (s, e) => { cfg.KeepAwake = awake.Checked; try { cfg.Save(); } catch { } };
    c.Controls.Add(awake);
    AppPage.Text(c, "Não deixar o PC dormir com a placa conectada", 80, 86, 560, 22, 10.5f, true);
    var c3 = AppPage.NewCard(flow, 92);
    var log = new FlatButton("Abrir registro") { Bounds = new Rectangle(18, 27, 150, 38) };
    log.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(Path.Combine(Config.Dir, "log.txt")) { UseShellExecute = true }); } catch { } };
    c3.Controls.Add(log);
    var quit = new FlatButton("Sair do NEXDECK HUB") { Bounds = new Rectangle(PW - 188, 27, 170, 38), Danger = true };
    quit.Click += (s, e) => { if (tray != null) tray.Quit(false); };
    c3.Controls.Add(quit);
  }

  // ---------------------------------------------------------------- estado ao vivo (1 s)
  void Tick() {
    var mode = worker.Mode;
    string app = worker.Current != null ? worker.Current.Title : Theme.AppLabel(worker.RunningApp ?? "");
    boardChip.Text = mode == BoardMode.App ? "Placa conectada · " + app : mode == BoardMode.Launcher ? "Placa conectada · NEXLauncher"
                   : (worker.Status.StartsWith("Pausado") ? "Pausado" : worker.Status.StartsWith("Placa em outro") ? "Placa em outro app" : "Procurando a placa");
    var list = worker.Apps;
    appsSub.Text = list == null ? (worker.Connected ? "" : (string.IsNullOrEmpty(cfg.LastApps) ? " " : "Placa desconectada · última lista"))
                 : list.Count + (list.Count == 1 ? " app" : " apps") + " no cartão";
    FillApps();
    foreach (var p in appPages.Values) if (p.Visible) p.Tick();
  }

  // NEXDECKHub.exe --shots <pasta>: grava uma imagem de cada tela (para conferir o visual sem a placa)
  public static void Shots(string dir) {
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    Application.EnableVisualStyles();
    Directory.CreateDirectory(dir);
    var w = new Worker { Cfg = Config.Load() };
    foreach (var a in HubApps.All) a.W = w;
    w.Status = "Procurando a placa";
    using (var f = new HubForm(w, null) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), ShowInTaskbar = false }) {
      f.Show();
      Action<string> snap = name => {
        Application.DoEvents();
        using (var b = new Bitmap(f.Width, f.Height)) {
          f.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
          b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        }
      };
      snap("hub_0_carregando");
      w.Apps = HubApps.All.Select(a => new Worker.BoardApp { Name = a.Id, Size = 512000 }).ToList();
      f.FillApps();
      f.Tick();
      snap("hub_1_apps");
      f.ShowSettings();
      snap("hub_2_config");
      foreach (var app in HubApps.All.Where(a => a.HasSettings)) {
        var page = f.ShowApp(app);
        for (int i = 0; i < page.SectionCount; i++) {
          page.ShowSection(i);
          f.Tick();
          snap("app_" + app.Id + "_" + i);
        }
      }
    }
  }
}

// ============================================================================ ícone na bandeja
public class TrayApp : ApplicationContext {
  readonly NotifyIcon tray = new NotifyIcon();
  readonly Worker worker = new Worker();
  readonly ToolStripMenuItem statusItem = new ToolStripMenuItem { Enabled = false };
  readonly ToolStripMenuItem pauseItem = new ToolStripMenuItem("Pausar (liberar a porta USB)");
  readonly Control invoker = new Control();  // criado na thread da interface: tudo que mexe na janela passa por ele
  HubForm win;

  void OnUi(Action a) {
    try { invoker.BeginInvoke(a); } catch (Exception e) { Log.Write("interface: " + e.Message); }
  }

  // outra cópia do app foi aberta: avisa esta para mostrar a janela
  public static void SignalOpen() {
    try { using (var h = EventWaitHandle.OpenExisting("NEXDECKHub.Open")) h.Set(); } catch { }
  }

  public void Quit(bool guardToo) {
    Log.Write(guardToo ? "saiu pelo menu, com o vigia" : "saiu pelo menu");
    Guard.SetFlag("sair", true);  // o vigia não reabre sozinho, só se a tela chamar
    if (guardToo) Guard.SetFlag("vigia-desligado", true);
    worker.Stop = true;
    tray.Visible = false;
    if (win != null && !win.IsDisposed) win.Close();
    Application.Exit();
  }

  // abre o HUB de novo (versão nova de um módulo que já estava carregado); a cópia nova espera esta fechar
  public void Restart() {
    bool showWin = win != null && !win.IsDisposed && win.Visible;
    Log.Write("reiniciando o HUB");
    Guard.SetFlag("sair", true);  // o vigia não reabre por conta própria
    try { Guard.StartDetached(showWin ? "--reiniciar-janela" : "--reiniciar"); } catch (Exception e) { Log.Write("reiniciar: " + e.Message); return; }
    worker.Stop = true;
    tray.Visible = false;
    if (win != null && !win.IsDisposed) win.Close();
    Application.Exit();
  }

  public void SetPaused(bool p) {
    worker.Paused = p;
    pauseItem.Checked = p;
    Guard.SetFlag("pausado", p);
  }

  // reason: "--startup" (com o Windows: fica só na bandeja), "--acordado"/"--reaberto" (vigia), "" (aberto pelo usuário)
  public TrayApp(string reason) {
    invoker.CreateControl();
    var forceHandle = invoker.Handle;
    worker.Cfg = Config.Load();
    if (!worker.Cfg.AutostartChosen) {  // "abrir junto com o Windows" vem ligado (dá para desligar nas configurações)
      try { Autostart.Set(true); Log.Write("abrir com o Windows: ligado"); } catch (Exception e) { Log.Write("abrir com o Windows: " + e.Message); }
      worker.Cfg.AutostartChosen = true;
    }
    worker.Cfg.Save();
    tray.Icon = Theme.AppIcon(16);
    tray.Text = "NEXDECK HUB";
    var menu = new ContextMenuStrip();
    menu.Items.Add(statusItem);
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add("Abrir NEXDECK HUB", null, (s, e) => OpenWindow());
    menu.Items.Add("Atualizar tudo agora", null, (s, e) => worker.RefreshRequested = true);
    pauseItem.Click += (s, e) => SetPaused(!worker.Paused);
    menu.Items.Add(pauseItem);
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add("Sair (a placa ainda pode chamar o app)", null, (s, e) => Quit(false));
    menu.Items.Add("Sair de vez (desliga o vigia)", null, (s, e) => Quit(true));
    menu.Opening += (s, e) => statusItem.Text = worker.Status;
    tray.ContextMenuStrip = menu;
    tray.DoubleClick += (s, e) => OpenWindow();
    tray.Visible = true;
    if (reason == "--acordado")
      tray.ShowBalloonTip(6000, "NEXDECK HUB", "A placa pediu e o app foi aberto de novo.", ToolTipIcon.Info);
    else if (reason == "--reaberto")
      tray.ShowBalloonTip(6000, "NEXDECK HUB", "O app tinha fechado sem querer e foi aberto de novo.", ToolTipIcon.Warning);
    worker.StatusChanged += () => OnUi(() => {
      string t = "NEXDECK HUB — " + worker.Status;
      tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
    });
    var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "NEXDECKHub.Open");
    new Thread(() => { while (signal.WaitOne()) OnUi(OpenWindow); }) { IsBackground = true, Name = "abrir" }.Start();
    worker.RestartNeeded += () => OnUi(Restart);
    worker.Start();
    if (reason == "" || reason == "--reiniciar-janela" || reason == "--instalado") OnUi(OpenWindow);  // aberto pelo usuário: mostra os apps da placa
  }

  void OpenWindow() {
    if (win == null || win.IsDisposed) win = new HubForm(worker, this);
    win.Show();
    if (win.WindowState == FormWindowState.Minimized) win.WindowState = FormWindowState.Normal;
    win.Activate();
  }
}

}  // namespace NexDeck
