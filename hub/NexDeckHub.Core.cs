// NEXDECK HUB — núcleo (público): app de bandeja do Windows que acompanha a placa NEXDECK pela USB.
// Acha a placa (no NEXDECK ou num app), mostra os apps do cartão, instala apps (.nexapp), abre apps na
// placa e entrega a conversa com cada app ao módulo dele (HubApp). Os módulos ficam em outros arquivos
// (NexDeckHub.Sample.cs é o exemplo). Compilar com build.ps1 (csc do .NET Framework 4, C# 5).
// Documentação: docs/ (hub-modules.md, protocol.md).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace NexDeck {

// ============================================================================ configuração
// config.json em %APPDATA%\NEXDECK HUB. Os módulos guardam configurações simples em AppData
// (Get/Set) ou acrescentam campos próprios com outra parte desta classe (partial).
public partial class Config {
  public string Port { get; set; }            // vazio = procura a placa sozinho
  public bool KeepAwake { get; set; }         // não deixar o PC dormir com a placa conectada
  public bool AutostartChosen { get; set; }   // false = ainda não decidiu: liga "abrir com o Windows" uma vez
  public string LastApps { get; set; }        // última lista de apps do cartão (nome,bytes;...)
  public Dictionary<string, string> AppData { get; set; }  // "app.chave" → valor (configurações dos módulos)

  static string AppDataDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
  public static string Dir { get { return Path.Combine(AppDataDir, "NEXDECK HUB"); } }
  public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

  partial void Defaults();           // campos dos módulos (outra parte da classe)
  static partial void BeforeLoad();  // ex.: copiar a configuração de uma versão antiga

  public static Config Load() {
    Config c = null;
    try {
      BeforeLoad();
      if (File.Exists(FilePath)) c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath));
    } catch { }
    if (c == null) c = new Config();
    if (c.Port == null) c.Port = "";
    if (c.AppData == null) c.AppData = new Dictionary<string, string>();
    c.Defaults();
    return c;
  }

  public void Save() {
    Directory.CreateDirectory(Dir);
    File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
  }

  public string Get(string app, string key, string def) {
    string v;
    return AppData != null && AppData.TryGetValue(app + "." + key, out v) ? v : def;
  }
  public void Set(string app, string key, string value) {
    if (AppData == null) AppData = new Dictionary<string, string>();
    AppData[app + "." + key] = value;
  }
}

// programas do menu Iniciar (mesma lista do Get-StartApps), útil para módulos com atalhos
public class AppEntry {
  public string Name, Id;
  public override string ToString() { return Name; }
}

public static class InstalledApps {
  public static List<AppEntry> List() {
    var result = new List<AppEntry>();
    try {
      Type t = Type.GetTypeFromProgID("Shell.Application");
      object shell = Activator.CreateInstance(t);
      object folder = t.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
      object items = folder.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, folder, null);
      int n = (int)items.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, items, null);
      for (int i = 0; i < n; i++) {
        object it = items.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, items, new object[] { i });
        string name = (string)it.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, it, null);
        string path = (string)it.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, it, null);
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path) && !path.StartsWith("http")) result.Add(new AppEntry { Name = name, Id = path });
      }
    } catch { }
    return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
  }
}

// ============================================================================ registro (%APPDATA%\NEXDECK HUB\log.txt)
public static class Log {
  static readonly object gate = new object();
  public static void Write(string s) {
    try {
      lock (gate) {
        string p = Path.Combine(Config.Dir, "log.txt");
        Directory.CreateDirectory(Config.Dir);
        if (File.Exists(p) && new FileInfo(p).Length > 300000) File.Delete(p);
        File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + s + Environment.NewLine);
      }
    } catch { }
  }
}

// ============================================================================ texto para a placa
public static class Txt {
  // caracteres que as fontes da placa têm
  const string EXTRA = "ÁÀÂÃÉÊÍÓÔÕÚÇáàâãéêíóôõúç−×·ÄËÏÖÜäëïöüÑñÈèÌìÒòÙùÎîÛûºª°«»€“”‘’–—…";

  public static string Clean(string s, int maxBytes) {
    if (string.IsNullOrEmpty(s)) return "";
    var sb = new StringBuilder();
    foreach (char ch0 in s) {
      char ch = ch0;
      if (ch == '\t' || ch == '\n' || ch == '\r' || ch == ' ') ch = ' ';
      if ((ch >= 32 && ch < 127) || EXTRA.IndexOf(ch) >= 0) { sb.Append(ch); continue; }
      // tira acentos que a fonte não tem (ę → e); o resto some
      foreach (char d in ch.ToString().Normalize(NormalizationForm.FormD))
        if (d >= 32 && d < 127) sb.Append(d);
    }
    string r = Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
    while (Encoding.UTF8.GetByteCount(r) > maxBytes) r = r.Substring(0, r.Length - 1);
    return r;
  }
}

// ============================================================================ ligação com a placa
// None = sem placa; Launcher = NEXDECK aberto (921600 baud); App = um app que fala com o PC (460800)
public enum BoardMode { None, Launcher, App }

public class Link {
  SerialPort sp;
  public BoardMode Mode = BoardMode.None;
  public List<string> LauncherApps = new List<string>();  // "nome bytes" da última listagem do NEXDECK
  readonly List<byte> rx = new List<byte>();
  public readonly Queue<string> Events = new Queue<string>();
  public bool Open { get { return sp != null && sp.IsOpen; } }
  public string PortName { get { return sp != null ? sp.PortName : ""; } }

  public static IEnumerable<string> Candidates(string preferred) {
    if (!string.IsNullOrEmpty(preferred)) { yield return preferred; yield break; }
    var ch340 = new List<string>();
    try {
      using (var q = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'")) {
        foreach (ManagementObject o in q.Get()) {
          string name = (o["Name"] ?? "").ToString(), id = (o["PNPDeviceID"] ?? "").ToString();
          var m = Regex.Match(name, @"\((COM\d+)\)");
          if (m.Success && (id.Contains("VID_1A86") || name.Contains("CH340") || id.Contains("VID_303A"))) ch340.Add(m.Groups[1].Value);
        }
      }
    } catch { }
    foreach (var p in ch340) yield return p;
  }

  // todo app que fala com o PC se apresenta com "EV HELLO <nome> [campos]" ao receber HI
  static bool IsHello(string e) { return e.StartsWith("EV\tHELLO\t"); }
  public string HelloName { get { var p = Hello.Split('\t'); return p.Length > 2 ? p[2] : ""; } }

  public bool SawPort;  // achou a porta da placa (mesmo sem resposta: ela pode estar num app que não fala com o PC)

  public bool Connect(string preferred) {
    SawPort = false;
    foreach (string port in Candidates(preferred)) {
      SawPort = true;
      try {
        sp = new SerialPort(port, 460800, Parity.None, 8, StopBits.One);
        sp.DtrEnable = false;  // não reinicia a placa ao abrir
        sp.RtsEnable = false;
        sp.ReadTimeout = 50;
        sp.WriteTimeout = 2000;
        sp.Open();
        sp.Write("\n");
        Thread.Sleep(120);
        sp.DiscardInBuffer();
        rx.Clear();
        Events.Clear();
        if (Send("HI", 1500)) {
          // a placa responde HELLO antes do K
          var deadline = DateTime.Now.AddMilliseconds(500);
          while (DateTime.Now < deadline && !Events.Any(IsHello)) Poll(50);
          if (Events.Any(IsHello)) {
            Hello = Events.Last(IsHello);
            var keep = Events.Where(e => !e.StartsWith("EV\tHELLO")).ToList();  // é o próprio começo da conexão
            Events.Clear();
            foreach (var e in keep) Events.Enqueue(e);
            Mode = BoardMode.App;
            return true;
          }
        }
      } catch { }
      Close();
      // nenhum app respondeu: talvez a placa esteja no NEXDECK (lançador, 921600 baud)
      try {
        sp = new SerialPort(port, 921600, Parity.None, 8, StopBits.One) { DtrEnable = false, RtsEnable = false, ReadTimeout = 50, WriteTimeout = 2000 };
        sp.Open();
        sp.Write("\n");
        Thread.Sleep(150);
        sp.DiscardInBuffer();
        rx.Clear();
        Events.Clear();
        if (LauncherList()) { Mode = BoardMode.Launcher; return true; }
      } catch { }
      Close();
    }
    return false;
  }

  public string Hello = "";  // a linha EV HELLO inteira

  // ---- NEXDECK (lançador): comandos de texto, sem soma
  public bool LauncherList() {
    sp.Write("LS\n");
    var list = new List<string>();
    for (string l = ReadLine(1500); l != null; l = ReadLine(1500)) {
      if (l == "END") { LauncherApps = list; return true; }
      list.Add(l);
    }
    return false;
  }

  public void LauncherRun(string name) { sp.Write("RUN " + name + "\n"); Thread.Sleep(300); }

  // PUT do lançador: blocos de 4096 com K depois de cada um, DONE no fim
  public bool LauncherPut(string path, byte[] data, Action<int> progress) {
    sp.Write("PUT " + path + " " + data.Length + "\n");
    string r = ReadLine(5000);
    while (r != null && r != "OK" && !r.StartsWith("ERR")) r = ReadLine(5000);
    if (r != "OK") return false;
    for (int off = 0; off < data.Length; off += 4096) {
      int n = Math.Min(4096, data.Length - off);
      sp.Write(data, off, n);
      string a = ReadLine(8000);
      while (a != null && a != "K" && !a.StartsWith("ERR")) a = ReadLine(8000);
      if (a != "K") return false;
      if (progress != null) progress((off + n) * 100 / data.Length);
    }
    string done = ReadLine(8000);
    while (done != null && !done.StartsWith("DONE")) done = ReadLine(8000);
    return done != null;
  }

  public void Close() {
    try { if (sp != null) sp.Close(); } catch { }
    sp = null;
    Mode = BoardMode.None;
  }

  // lê o que chegou e separa linhas; eventos vão para a fila, o resto é devolvido
  string ReadLine(int timeoutMs) {
    var until = DateTime.Now.AddMilliseconds(timeoutMs);
    while (true) {
      int nl = rx.IndexOf((byte)'\n');
      if (nl >= 0) {
        string l = Encoding.UTF8.GetString(rx.GetRange(0, nl).ToArray()).TrimEnd('\r');
        rx.RemoveRange(0, nl + 1);
        if (l.StartsWith("EV\t")) { Events.Enqueue(l); continue; }
        if (l.Length > 0) return l;
        continue;
      }
      if (DateTime.Now > until) return null;
      int n = sp.BytesToRead;
      if (n > 0) {
        var buf = new byte[n];
        int got = sp.Read(buf, 0, n);
        for (int i = 0; i < got; i++) rx.Add(buf[i]);
      } else {
        Thread.Sleep(2);
      }
    }
  }

  public void Poll(int ms) {
    string l;
    while ((l = ReadLine(ms)) != null) { }  // respostas soltas são descartadas
  }

  static string WithSum(string payload) {
    int sum = 0;
    foreach (byte b in Encoding.UTF8.GetBytes(payload)) sum = (sum + b) & 0xFFFF;
    return payload + "\t~" + sum.ToString("x4") + "\n";
  }

  // manda uma linha e espera K (reenvia se a placa responder E ou não responder)
  public bool Send(string payload, int timeoutMs = 2500) {
    byte[] data = Encoding.UTF8.GetBytes(WithSum(payload));
    for (int tries = 0; tries < 4; tries++) {
      sp.Write(data, 0, data.Length);
      while (true) {
        string r = ReadLine(timeoutMs);
        if (r == null) { Log.Write("sem resposta, reenviando " + payload.Split('\t')[0]); sp.Write("\n"); Thread.Sleep(150); Poll(30); break; }
        if (r == "K") return true;
        if (r == "E") { Log.Write("soma errada, reenviando " + payload.Split('\t')[0]); break; }
      }
    }
    return false;
  }

  public bool SendBlob(string kind, int arg, byte[] data) { return SendChunks("B\t" + kind + "\t" + arg + "\t" + data.Length, kind, data, null); }

  // arquivo para o cartão da placa, com um app aberto (FPUT, SquareLink.h)
  public bool SendFile(string path, byte[] data, Action<int> progress) { return SendChunks("FPUT\t" + path + "\t" + data.Length, path, data, progress); }

  bool SendChunks(string header, string kind, byte[] data, Action<int> progress) {
    for (int tries = 0; tries < 3; tries++) {
      byte[] head = Encoding.UTF8.GetBytes(WithSum(header));
      sp.Write(head, 0, head.Length);
      string r = ReadLine(2500);
      while (r != null && r != "OK" && r != "E" && !r.StartsWith("ERR")) r = ReadLine(2500);
      if (r == null || r == "E") { Thread.Sleep(200); Poll(30); continue; }
      if (r != "OK") return false;
      int off = 0, fails = 0;
      while (off < data.Length) {
        int n = Math.Min(2048, data.Length - off);
        var chunk = new byte[n + 2];
        System.Buffer.BlockCopy(data, off, chunk, 0, n);
        int sum = 0;
        for (int i = 0; i < n; i++) sum = (sum + chunk[i]) & 0xFFFF;
        chunk[n] = (byte)(sum & 255);
        chunk[n + 1] = (byte)(sum >> 8);
        sp.Write(chunk, 0, chunk.Length);
        string a = ReadLine(3000);
        while (a != null && a != "K" && a != "R" && !a.StartsWith("ERR")) a = ReadLine(3000);
        if (a == "K") { off += n; if (progress != null) progress(off * 100 / data.Length); continue; }
        Log.Write("bloco " + off + " de " + kind + ": " + (a ?? "sem resposta"));
        if (a == null || a.StartsWith("ERR") || ++fails > 8) return false;
      }
      string done = ReadLine(6000);
      while (done != null && done != "DONE") done = ReadLine(6000);
      return done == "DONE";
    }
    return false;
  }
}

// ============================================================================ ícones de programas
[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemImageFactory {
  [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
}

[StructLayout(LayoutKind.Sequential)]
struct NativeSize { public int cx, cy; }

[StructLayout(LayoutKind.Sequential)]
struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

public static class Icons {
  [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
  [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

  // ícone do programa em size×size com transparência
  public static Bitmap Get(string target, int size) {
    try {
      Guid iid = typeof(IShellItemImageFactory).GUID;
      IShellItemImageFactory f;
      SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out f);
      IntPtr hbm;
      if (f.GetImage(new NativeSize { cx = 96, cy = 96 }, 0x4 /* só ícone */, out hbm) != 0) return null;
      try {
        BITMAP bm;
        GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm);
        if (bm.bmBitsPixel != 32 || bm.bmBits == IntPtr.Zero) return null;
        var src = new Bitmap(bm.bmWidth, bm.bmHeight, bm.bmWidthBytes, PixelFormat.Format32bppPArgb, bm.bmBits);
        var copy = new Bitmap(bm.bmWidth, bm.bmHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(copy)) g.DrawImage(src, 0, 0);
        src.Dispose();
        if (bm.bmHeight > 0) copy.RotateFlip(RotateFlipType.RotateNoneFlipY);  // DIB de baixo para cima
        var outBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(outBmp)) {
          g.InterpolationMode = InterpolationMode.HighQualityBicubic;
          g.PixelOffsetMode = PixelOffsetMode.HighQuality;
          g.DrawImage(copy, new Rectangle(0, 0, size, size));
        }
        copy.Dispose();
        return outBmp;
      } finally { DeleteObject(hbm); }
    } catch { return null; }
  }

  // RGB565 little-endian + alfa, 3 bytes por pixel
  public static byte[] ToRgb565A(Bitmap b) {
    var data = new byte[b.Width * b.Height * 3];
    int k = 0;
    for (int y = 0; y < b.Height; y++)
      for (int x = 0; x < b.Width; x++) {
        Color c = b.GetPixel(x, y);
        int v = ((c.R & 0xF8) << 8) | ((c.G & 0xFC) << 3) | (c.B >> 3);
        data[k++] = (byte)(v & 255);
        data[k++] = (byte)(v >> 8);
        data[k++] = c.A;
      }
    return data;
  }
}

// ============================================================================ imagens
public static class Img {
  static ImageCodecInfo jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

  // corta o centro e reduz para size×size; devolve JPEG simples (a placa não lê JPEG progressivo)
  public static byte[] SquareJpeg(Image src, int size, long quality = 85) { return CoverJpeg(src, size, size, quality); }

  // cobre w×h cortando o excesso no centro
  public static byte[] CoverJpeg(Image src, int w, int h, long quality = 85) {
    using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb)) {
      using (var g = Graphics.FromImage(bmp)) {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        double sc = Math.Max((double)w / src.Width, (double)h / src.Height);
        int sw = (int)Math.Round(w / sc), sh = (int)Math.Round(h / sc);
        g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle((src.Width - sw) / 2, (src.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
      }
      var ms = new MemoryStream();
      var ep = new EncoderParameters(1);
      ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
      bmp.Save(ms, jpegCodec, ep);
      return ms.ToArray();
    }
  }

  // gira conforme a orientação EXIF (fotos de celular)
  // tira bordas lisas (brancas ou pretas) que alguns serviços colocam em volta da foto
  public static Bitmap TrimBorders(Image src) {
    var b = new Bitmap(src);
    Func<int, int, bool> flat = (x, y) => { var c = b.GetPixel(x, y); return (c.R > 245 && c.G > 245 && c.B > 245) || (c.R < 10 && c.G < 10 && c.B < 10); };
    Func<int, bool> rowFlat = y => { for (int x = 0; x < b.Width; x += 4) if (!flat(x, y)) return false; return true; };
    Func<int, bool> colFlat = x => { for (int y = 0; y < b.Height; y += 4) if (!flat(x, y)) return false; return true; };
    int t = 0, bo = b.Height - 1, l = 0, r = b.Width - 1;
    while (t < bo && rowFlat(t)) t++;
    while (bo > t && rowFlat(bo)) bo--;
    while (l < r && colFlat(l)) l++;
    while (r > l && colFlat(r)) r--;
    if (t == 0 && l == 0 && bo == b.Height - 1 && r == b.Width - 1) return b;
    if (r - l < 40 || bo - t < 40) return b;  // quase tudo liso: deixa como está
    var cut = b.Clone(new Rectangle(l, t, r - l + 1, bo - t + 1), b.PixelFormat);
    b.Dispose();
    return cut;
  }

  public static void FixOrientation(Image img) {
    try {
      if (!img.PropertyIdList.Contains(0x0112)) return;
      int o = img.GetPropertyItem(0x0112).Value[0];
      var map = new Dictionary<int, RotateFlipType> {
        { 2, RotateFlipType.RotateNoneFlipX }, { 3, RotateFlipType.Rotate180FlipNone }, { 4, RotateFlipType.Rotate180FlipX },
        { 5, RotateFlipType.Rotate90FlipX }, { 6, RotateFlipType.Rotate90FlipNone }, { 7, RotateFlipType.Rotate270FlipX },
        { 8, RotateFlipType.Rotate270FlipNone } };
      if (map.ContainsKey(o)) img.RotateFlip(map[o]);
    } catch { }
  }
}

// ============================================================================ internet
public static class Web {
  const string UA = "NEXDECKHub/1.0", BROWSER = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
  // browser = true: alguns serviços (Yahoo Finance) recusam clientes sem cara de navegador
  public static string Get(string url, bool browser = false) {
    using (var wc = new WebClient()) {
      wc.Encoding = Encoding.UTF8;
      wc.Headers[HttpRequestHeader.UserAgent] = browser ? BROWSER : UA;
      return wc.DownloadString(url);
    }
  }
  public static byte[] GetBytes(string url) {
    using (var wc = new WebClient()) {
      wc.Headers[HttpRequestHeader.UserAgent] = UA;
      return wc.DownloadData(url);
    }
  }
}

// ============================================================================ pacote de app (.nexapp)
// Um .zip com manifest.json ({"id", "name", "version", "bin", "icon"}), o .bin do app e o ícone (.icn
// do NEXDECK ou .png, convertido aqui). Instalar = copiar para /apps/<id>.bin e /apps/<id>.icn no cartão.
public class AppPackage {
  public string Id, Name, Version, File;
  public byte[] Bin, Icn;
  public Bitmap Preview;

  public static AppPackage Load(string file) {
    var p = new AppPackage { File = file };
    using (var zip = ZipFile.OpenRead(file)) {
      Func<string, byte[]> read = n => {
        var e = zip.Entries.FirstOrDefault(x => x.FullName.Equals(n, StringComparison.OrdinalIgnoreCase));
        if (e == null) return null;
        using (var s = e.Open()) using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
      };
      var mf = read("manifest.json");
      if (mf == null) throw new Exception("o pacote não tem manifest.json");
      var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(mf));
      Func<string, string> S = k => j.ContainsKey(k) && j[k] != null ? j[k].ToString() : "";
      p.Id = S("id");
      p.Name = S("name").Length > 0 ? S("name") : p.Id;
      p.Version = S("version");
      if (!Regex.IsMatch(p.Id, "^[A-Za-z0-9_-]{1,30}$")) throw new Exception("id inválido no manifest.json");
      p.Bin = read(S("bin").Length > 0 ? S("bin") : "app.bin");
      if (p.Bin == null || p.Bin.Length < 1024 || p.Bin[0] != 0xE9) throw new Exception("o pacote não tem um app ESP32 válido");
      string icon = S("icon");
      byte[] ic = icon.Length > 0 ? read(icon) : null;
      if (ic != null && ic.Length > 6 && Encoding.ASCII.GetString(ic, 0, 4) == "ICN1") { p.Icn = ic; p.Preview = IcnToBitmap(ic); }
      else if (ic != null) {
        using (var img = Image.FromStream(new MemoryStream(ic))) {
          p.Preview = new Bitmap(img, 88, 88);
          p.Icn = BitmapToIcn(p.Preview);
        }
      }
    }
    return p;
  }

  public static byte[] BitmapToIcn(Bitmap b) {
    var px = Icons.ToRgb565A(b);
    var data = new byte[6 + px.Length];
    Encoding.ASCII.GetBytes("ICN1").CopyTo(data, 0);
    data[4] = (byte)b.Width;
    data[5] = (byte)b.Height;
    px.CopyTo(data, 6);
    return data;
  }

  public static Bitmap IcnToBitmap(byte[] d) {
    int w = d[4], h = d[5];
    var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    for (int y = 0, k = 6; y < h; y++)
      for (int x = 0; x < w; x++, k += 3) {
        if (k + 2 >= d.Length) break;
        int v = d[k] | (d[k + 1] << 8);
        b.SetPixel(x, y, Color.FromArgb(d[k + 2], ((v >> 11) & 31) * 255 / 31, ((v >> 5) & 63) * 255 / 63, (v & 31) * 255 / 31));
      }
    return b;
  }
}

// ============================================================================ módulos de app
// Cada app da placa que conversa com o PC tem um módulo aqui no HUB (lado PC do app): ele recebe a
// conexão quando a placa diz "EV HELLO <nome>", troca mensagens enquanto o app está aberto e pode ter
// uma página de configurações. Veja docs/hub-modules.md.
public abstract class HubApp {
  public Worker W { get; internal set; }
  public Config Cfg { get { return W.Cfg; } }

  public abstract string Id { get; }      // nome do .bin no cartão (/apps/<Id>.bin)
  public abstract string Hello { get; }   // nome que o app manda no EV HELLO
  public virtual string Title { get { return Id; } }
  public virtual string Status { get { return Title + " aberto na placa"; } }

  // lado da placa (rodam na thread da placa; use link.Send / Worker.Must)
  public virtual void Start() { }                                     // uma vez, ao abrir o HUB (threads de internet...)
  public virtual void OnConnect(Link link, string[] hello) { }        // o app abriu na placa (ou mandou HELLO de novo)
  public virtual void Loop(Link link) { Thread.Sleep(20); }           // repetido enquanto ele está aberto (o HUB já faz o ping)
  public virtual bool OnEvent(Link link, string[] ev) { return false; }  // EV vindo da placa; ev[1] = nome
  public virtual string CoalesceKey(string[] ev) { return null; }     // eventos repetidos com a mesma chave: só o último vale

  // janela
  public virtual bool HasSettings { get { return false; } }
  public virtual void BuildSettings(AppPage page) { }                 // cria as seções da página
  public virtual void Save() { }                                      // botão Salvar
  public virtual void Tick() { }                                      // a cada 1 s com a página aberta
}

// Lista de módulos. A versão pública traz o exemplo HelloPc; quem compila o HUB com apps próprios
// acrescenta os módulos em RegisterApps (outra parte desta classe, em outro arquivo).
public static partial class HubApps {
  public static readonly List<HubApp> All = new List<HubApp>();
  static partial void RegisterApps(List<HubApp> list);
  static HubApps() {
    RegisterApps(All);
    All.Add(new HelloPcApp());
  }
  public static HubApp ByHello(string hello) { return All.FirstOrDefault(a => a.Hello == hello); }
  public static HubApp ById(string id) { return All.FirstOrDefault(a => a.Id == id); }
}

// fotos para os apps que mostram fotos do PC: de uma pasta (aleatórias) ou da internet (picsum.photos)
public class PhotoSource {
  readonly Random rnd = new Random();
  List<string> files;
  int index;
  string lastDir;

  public byte[] Next(bool web, string folder) {
    byte[] jpg = null;
    try {
      if (!web) {
        string dir = string.IsNullOrEmpty(folder) ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : folder;
        if (files == null || files.Count == 0 || index >= files.Count || dir != lastDir) {
          files = new List<string>();
          foreach (var ext in new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" })
            try { files.AddRange(Directory.EnumerateFiles(dir, ext, SearchOption.AllDirectories).Take(3000)); } catch { }
          files = files.OrderBy(_ => rnd.Next()).ToList();
          index = 0;
          lastDir = dir;
        }
        for (int tries = 0; tries < 5 && jpg == null && index < files.Count; tries++) {
          try {
            using (var img = Image.FromFile(files[index++])) { Img.FixOrientation(img); jpg = Img.SquareJpeg(img, 480); }
          } catch { }
        }
      } else {
        var data = Web.GetBytes("https://picsum.photos/480/480?random=" + rnd.Next());
        using (var img = Image.FromStream(new MemoryStream(data))) jpg = Img.SquareJpeg(img, 480);
      }
    } catch (Exception e) { Log.Write("foto: " + e.Message); }
    Log.Write("foto " + (web ? "web" : "pc") + ": " + (jpg != null ? jpg.Length + " bytes" : "nenhuma"));
    return jpg;
  }
}

// ============================================================================ o trabalho em segundo plano
// Thread da placa: acha a placa, sabe em que app ela está, lista e instala apps, entrega a conversa ao
// módulo do app aberto e atende os pedidos da janela.
public class Worker {
  public Config Cfg;
  public volatile bool Stop, Paused, ResyncRequested, RefreshRequested;
  public volatile string Status = "Procurando a placa...";
  public event Action StatusChanged, AppsChanged, InstallChanged;
  public bool Connected { get { return link.Open; } }
  public BoardMode Mode { get { return link.Mode; } }
  public HubApp Current { get; private set; }  // módulo do app aberto na placa (null = app sem módulo)
  // app aberto na placa (nome do .bin, ou o nome do HELLO se não houver módulo)
  public string RunningApp { get { return link.Mode != BoardMode.App ? null : Current != null ? Current.Id : link.HelloName; } }

  public class BoardApp { public string Name; public long Size; }
  public volatile List<BoardApp> Apps;  // apps do cartão (null = ainda não sabe)
  public volatile int InstallPct = -1;
  public volatile string InstallMsg = "";

  readonly Link link = new Link();
  public Link Link { get { return link; } }  // só dentro de Request(...) ou dos métodos do módulo (thread da placa)
  readonly Queue<Action> requests = new Queue<Action>();
  readonly Queue<Tuple<string, int, byte[]>> blobs = new Queue<Tuple<string, int, byte[]>>();
  string[] hello = new string[0];
  DateTime lastPing;

  public static void Must(bool ok) { if (!ok) throw new IOException("a placa não respondeu"); }

  // pedidos da janela: rodam na thread da placa, entre uma troca de mensagens e outra
  public void Request(Action a) { lock (requests) requests.Enqueue(a); }
  // imagem/arquivo para mandar como bloco (B\t<tipo>\t<arg>) quando der; útil para preparar em outra thread
  public void QueueBlob(string kind, int arg, byte[] data) { lock (blobs) blobs.Enqueue(Tuple.Create(kind, arg, data)); }
  // lê os eventos que chegaram (para módulos com laços longos)
  public void PumpEvents() { HandleEvents(); }

  public void SetStatus(string s) {
    if (Status == s) return;
    Status = s;
    Log.Write(s);
    var h = StatusChanged;
    if (h != null) h();
  }
  void SetApps(List<BoardApp> list) {
    Apps = list;
    var h = AppsChanged;
    if (h != null) h();
  }
  void SetInstall(int pct, string msg) {
    InstallPct = pct;
    InstallMsg = msg;
    var h = InstallChanged;
    if (h != null) h();
  }

  public void Install(AppPackage p) {
    Request(() => {
      SetInstall(0, "Enviando " + p.Name + "...");
      bool ok;
      Action<int> prog = pct => SetInstall(Math.Min(99, pct * 92 / 100), "Enviando " + p.Name + "...");
      if (link.Mode == BoardMode.Launcher) {
        ok = link.LauncherPut("/apps/" + p.Id + ".bin", p.Bin, prog) && (p.Icn == null || link.LauncherPut("/apps/" + p.Id + ".icn", p.Icn, null));
        if (ok) link.LauncherList();
      } else {
        ok = link.SendFile("/apps/" + p.Id + ".bin", p.Bin, prog) && (p.Icn == null || link.SendFile("/apps/" + p.Id + ".icn", p.Icn, null));
      }
      if (ok) {
        try {
          Directory.CreateDirectory(Path.Combine(Config.Dir, "icones"));
          if (p.Preview != null) p.Preview.Save(Path.Combine(Config.Dir, "icones", p.Id + ".png"), ImageFormat.Png);
        } catch { }
      }
      Log.Write("instalar " + p.Id + ": " + (ok ? "ok" : "falhou"));
      SetInstall(ok ? 100 : -2, ok ? p.Name + " foi instalado na placa." : "Não deu para instalar. Confira o cabo e tente de novo.");
      RefreshApps();
    });
  }

  // grava arquivos no cartão (com a placa em qualquer app que use SquareLink, ou no NEXDECK)
  public bool PutFile(string path, byte[] data, Action<int> progress) {
    if (link.Mode == BoardMode.Launcher) return link.LauncherPut(path, data, progress);
    if (link.Mode == BoardMode.App) return link.SendFile(path, data, progress);
    return false;
  }

  public void OpenApp(string name) {
    Request(() => {
      if (link.Mode == BoardMode.Launcher) link.LauncherRun(name);
      else Must(link.Send("OPENAPP\t" + name));
      link.Close();  // a placa reinicia no app pedido
    });
  }

  void RefreshApps() {
    if (link.Mode == BoardMode.App) { Must(link.Send("LS")); HandleEvents(); return; }
    if (link.Mode == BoardMode.Launcher)
      SetApps(link.LauncherApps.Select(l => l.Split(' ')).Where(a => a.Length >= 2)
                .Select(a => new BoardApp { Name = a[0], Size = long.Parse(a[1]) }).ToList());
  }

  void RunRequests() {
    while (true) {
      Action a = null;
      lock (requests) if (requests.Count > 0) a = requests.Dequeue();
      if (a == null) return;
      a();
      if (!link.Open) return;
    }
  }

  public void Start() {
    foreach (var app in HubApps.All) {
      app.W = this;
      try { app.Start(); } catch (Exception e) { Log.Write(app.Id + ": " + e.Message); }
    }
    new Thread(Run) { IsBackground = true, Name = "placa" }.Start();
  }

  [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
  const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
  bool awakeOn;

  // com a placa conectada (e a opção ligada), pede ao Windows para não dormir; o monitor ainda apaga
  void UpdateKeepAwake() {
    bool want = Cfg.KeepAwake && link.Open && !Paused;
    if (want == awakeOn) return;
    SetThreadExecutionState(want ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
    awakeOn = want;
  }

  void Run() {
    while (!Stop) {
      UpdateKeepAwake();
      if (Paused) {
        if (link.Open) link.Close();
        SetStatus("Pausado: a porta USB está livre");
        Thread.Sleep(300);
        continue;
      }
      if (!link.Open) {
        Current = null;
        if (!link.Connect(Cfg.Port)) {
          SetStatus(link.SawPort ? "Placa em outro app (sem conversa com o PC)" : "Procurando a placa...");
          Thread.Sleep(3000);
          continue;
        }
        if (link.Mode == BoardMode.Launcher) {  // NEXDECK aberto: lista, atende pedidos e tenta de novo (pode abrir um app)
          SetStatus("Placa no NEXDECK");
          RefreshApps();
          var until = DateTime.Now.AddSeconds(4);
          while (DateTime.Now < until && !Stop) { try { RunRequests(); } catch (Exception e) { Log.Write("pedido: " + e.Message); } Thread.Sleep(100); }
          link.Close();
          continue;
        }
        try {
          hello = link.Hello.Split('\t');
          Current = HubApps.ByHello(link.HelloName);
          lock (blobs) blobs.Clear();
          SetStatus(Current != null ? Current.Status : link.HelloName + " aberto na placa");
          if (Current != null) Current.OnConnect(link, hello);
          RefreshApps();
        } catch (Exception e) { Log.Write("conectar: " + e.Message); link.Close(); continue; }
      }
      try {
        Loop();
      } catch (Exception e) {
        Log.Write("erro: " + e.Message);
        link.Close();
        SetStatus("Procurando a placa...");
        Thread.Sleep(1500);
      }
    }
    link.Close();
  }

  void Loop() {
    HandleEvents();
    RunRequests();
    if (!link.Open) return;
    if (ResyncRequested) { ResyncRequested = false; if (Current != null) Current.OnConnect(link, hello); }
    var now = DateTime.Now;
    if ((now - lastPing).TotalSeconds >= 5) { Must(link.Send("P")); lastPing = now; }
    Tuple<string, int, byte[]> blob = null;
    lock (blobs) if (blobs.Count > 0) blob = blobs.Dequeue();
    if (blob != null) {
      var sw = Stopwatch.StartNew();
      Must(link.SendBlob(blob.Item1, blob.Item2, blob.Item3));
      Log.Write(blob.Item1 + " enviado: " + blob.Item3.Length + " bytes em " + sw.ElapsedMilliseconds + " ms");
    }
    if (Current != null) Current.Loop(link);
    else Thread.Sleep(20);
  }

  void HandleEvents() {
    link.Poll(5);
    if (link.Events.Count == 0) return;
    var evs = new List<string[]>();
    while (link.Events.Count > 0) evs.Add(link.Events.Dequeue().Split('\t'));
    for (int i = 0; i < evs.Count; i++) {
      string key = Current != null ? Current.CoalesceKey(evs[i]) : null;
      if (key != null && evs.Skip(i + 1).Any(e => Current.CoalesceKey(e) == key)) continue;
      HandleEvent(evs[i], key != null);
    }
  }

  void HandleEvent(string[] p, bool quiet) {
    if (!quiet) Log.Write("placa: " + string.Join(" ", p));
    if (p.Length < 2) return;
    try {
      switch (p[1]) {
        case "HELLO":  // o app (re)abriu: o módulo manda tudo de novo
          hello = p;
          if (Current != null) Current.OnConnect(link, p);
          return;
        case "APPS": {  // apps do cartão: nome,bytes;nome,bytes
          var list = new List<BoardApp>();
          if (p.Length > 2)
            foreach (string a in p[2].Split(';')) {
              var f = a.Split(',');
              long sz;
              if (f.Length == 2 && long.TryParse(f[1], out sz)) list.Add(new BoardApp { Name = f[0], Size = sz });
            }
          SetApps(list);
          return;
        }
      }
      if (Current != null) Current.OnEvent(link, p);
    } catch (Exception e) { Log.Write("evento " + string.Join(" ", p) + ": " + e.Message); }
  }
}

// ============================================================================ vigia
// Processo pequeno, sem janela (NEXDECKHub.exe --guard), aberto pelo app e solto dele: se o app
// fechar sem o "Sair" (travou, foi encerrado junto com outro programa), abre de novo; se o usuário
// saiu pelo menu, fica escutando a porta USB de vez em quando e abre o app quando a tela pede
// ("Acordar o app no PC" manda EV WAKE).
public static class Guard {
  static string FlagPath(string name) { return Path.Combine(Config.Dir, name); }
  public static bool HasFlag(string name) { return File.Exists(FlagPath(name)); }
  public static void SetFlag(string name, bool on) {
    try {
      Directory.CreateDirectory(Config.Dir);
      if (on) File.WriteAllText(FlagPath(name), DateTime.Now.ToString("s"));
      else if (File.Exists(FlagPath(name))) File.Delete(FlagPath(name));
    } catch { }
  }

  static bool Running(string mutex) {
    Mutex m;
    if (!Mutex.TryOpenExisting(mutex, out m)) return false;
    m.Dispose();
    return true;
  }

  // abre o programa fora da árvore de processos de quem chamou (pelo WMI), para não ser
  // encerrado junto com ela
  public static void StartDetached(string args) {
    using (var mc = new ManagementClass("Win32_Process")) {
      var inP = mc.GetMethodParameters("Create");
      inP["CommandLine"] = "\"" + Application.ExecutablePath + "\" " + args;
      inP["CurrentDirectory"] = Path.GetDirectoryName(Application.ExecutablePath);
      mc.InvokeMethod("Create", inP, null);
    }
  }

  public static void EnsureRunning() {
    if (HasFlag("vigia-desligado") || Running("NEXDECKHub.Guard")) return;
    try { StartDetached("--guard"); } catch (Exception e) { Log.Write("vigia: não abriu: " + e.Message); }
  }

  public static void Run() {
    bool first;
    using (var gm = new Mutex(true, "NEXDECKHub.Guard", out first)) {
      if (!first) return;
      Log.Write("vigia: ligado");
      bool wasRunning = Running("NEXDECKHub.PC");
      while (!HasFlag("vigia-desligado")) {
        if (Running("NEXDECKHub.PC")) { wasRunning = true; Thread.Sleep(2000); continue; }
        if (wasRunning && !HasFlag("sair")) {
          Log.Write("vigia: o app fechou sem o Sair; abrindo de novo");
          wasRunning = false;
          Thread.Sleep(1500);
          try { StartDetached("--reaberto"); } catch (Exception e) { Log.Write("vigia: " + e.Message); }
          Thread.Sleep(10000);
          continue;
        }
        wasRunning = false;
        if (!HasFlag("pausado") && ListenForWake(1500)) {
          Log.Write("vigia: a tela pediu para abrir o app");
          SetFlag("sair", false);
          try { StartDetached("--acordado"); } catch (Exception e) { Log.Write("vigia: " + e.Message); }
          Thread.Sleep(10000);
          continue;
        }
        Thread.Sleep(1500);
      }
      Log.Write("vigia: desligado");
    }
  }

  // abre a porta por pouco tempo (para não prender o push.ps1) e procura o pedido da tela
  static bool ListenForWake(int ms) {
    string pref = "";
    try { pref = Config.Load().Port; } catch { }
    foreach (string port in Link.Candidates(pref)) {
      try {
        using (var sp = new SerialPort(port, 460800, Parity.None, 8, StopBits.One)) {
          sp.DtrEnable = false;
          sp.RtsEnable = false;
          sp.Open();
          var got = new StringBuilder();
          var until = DateTime.Now.AddMilliseconds(ms);
          while (DateTime.Now < until) {
            got.Append(sp.ReadExisting());
            if (got.ToString().Contains("EV\tWAKE")) return true;
            Thread.Sleep(50);
          }
        }
      } catch { }
    }
    return false;
  }
}

public static class Program {
  [STAThread]
  public static void Main(string[] args) {
    if (args.Length == 1 && args[0] == "--guard") { Guard.Run(); return; }
    if (args.Length == 2 && args[0] == "--shots") { HubForm.Shots(args[1]); return; }
    if (args.Length == 2 && args[0] == "--pack") { File.WriteAllText(args[1] + ".txt", AppPackage.Load(args[1]).Name); return; }
    string reason = args.Length == 1 ? args[0] : "";
    bool first;
    using (var m = new Mutex(true, "NEXDECKHub.PC", out first)) {
      if (!first) {
        if (reason == "") TrayApp.SignalOpen();  // já está aberto: mostra a janela dele
        return;
      }
      // registra qualquer erro que derrubaria o app, para saber depois o que houve
      AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("erro fatal: " + e.ExceptionObject);
      Application.ThreadException += (s, e) => Log.Write("erro na janela: " + e.Exception);
      Log.Write("app aberto" + (reason != "" ? " (" + reason + ")" : ""));
      Guard.SetFlag("sair", false);
      Guard.SetFlag("pausado", false);
      Guard.SetFlag("vigia-desligado", false);
      Guard.EnsureRunning();
      Autostart.Migrate();
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new TrayApp(reason));
    }
  }
}

}  // namespace NexDeck
