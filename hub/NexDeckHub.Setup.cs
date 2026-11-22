// NEXDECK HUB — instalador de um clique (público). O mesmo .exe instala a si mesmo quando o arquivo se
// chama "Instalar NEXDECK HUB.exe" (ou tem "Setup" no nome): copia para %LOCALAPPDATA%\Programs\NEXDECK HUB,
// cria o atalho no menu Iniciar e a entrada em "Aplicativos instalados" do Windows, e abre o HUB. Sem
// administrador. Os apps vêm do cartão da placa: ao conectar, o HUB copia os módulos (/apps/*.dll).
// Desinstalar: Configurações do Windows → Aplicativos (roda "NEXDECKHub.exe --desinstalar").
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexDeck {

public static class Setup {
  const string NAME = "NEXDECK HUB";
  const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NEXDECK HUB";
  public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", NAME); } }
  public static string Exe { get { return Path.Combine(Dir, "NEXDECKHub.exe"); } }
  static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), NAME + ".lnk"); } }

  public static bool IsInstaller {
    get {
      string n = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
      return n.IndexOf("Instalar", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0;
    }
  }

  // ---- instalar
  public static void Install() {
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new SetupForm());
  }

  // o trabalho (fora da thread da janela); devolve a mensagem de erro ou null
  internal static string DoInstall(Action<string> step) {
    try {
      step("Fechando o NEXDECK HUB aberto...");
      Guard.SetFlag("vigia-desligado", true);  // o vigia sai sozinho e não reabre o app durante a cópia
      Guard.SetFlag("sair", true);
      StopOthers(Exe);
      step("Copiando o NEXDECK HUB...");
      Directory.CreateDirectory(Dir);
      string self = Application.ExecutablePath;
      if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase)) {
        for (int tries = 0; ; tries++) {
          try { File.Copy(self, Exe, true); break; } catch (IOException) { if (tries > 20) throw; Thread.Sleep(250); }
        }
      }
      step("Criando o atalho no menu Iniciar...");
      MakeShortcut(Shortcut, Exe);
      using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey)) {
        k.SetValue("DisplayName", NAME);
        k.SetValue("DisplayIcon", Exe);
        k.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString());
        k.SetValue("Publisher", "NEXDECK");
        k.SetValue("InstallLocation", Dir);
        k.SetValue("UninstallString", "\"" + Exe + "\" --desinstalar");
        k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
      }
      // "abrir com o Windows": o HUB liga na primeira vez e corrige o caminho se já existia
      Guard.SetFlag("vigia-desligado", false);
      Guard.SetFlag("sair", false);
      step("Abrindo o NEXDECK HUB...");
      Process.Start(new ProcessStartInfo(Exe, "--instalado") { WorkingDirectory = Dir, UseShellExecute = false });
      Log.Write("instalado em " + Dir);
      return null;
    } catch (Exception e) {
      Log.Write("instalar: " + e);
      return e.Message;
    }
  }

  // fecha as outras cópias do HUB (e o vigia) que estejam rodando do arquivo de destino
  static void StopOthers(string exePath) {
    int me = Process.GetCurrentProcess().Id;
    foreach (var p in Process.GetProcessesByName("NEXDECKHub")) {
      try {
        if (p.Id == me) continue;
        string path = p.MainModule.FileName;
        if (!string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)) continue;
        p.Kill();
        p.WaitForExit(5000);
      } catch { }
    }
  }

  static void MakeShortcut(string lnk, string target) {
    Type t = Type.GetTypeFromProgID("WScript.Shell");
    object shell = Activator.CreateInstance(t);
    object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
    Type st = sc.GetType();
    st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
    st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
    st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "App do PC da placa NEXDECK" });
    st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
    st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
  }

  // ---- desinstalar (as configurações em %APPDATA%\NEXDECK HUB ficam, para uma reinstalação)
  public static void Uninstall(bool quiet) {
    Application.EnableVisualStyles();
    if (!quiet && MessageBox.Show("Remover o NEXDECK HUB deste PC?\n\nOs apps no cartão da placa continuam lá.", NAME,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
    Guard.SetFlag("vigia-desligado", true);
    Guard.SetFlag("sair", true);
    StopOthers(Application.ExecutablePath);
    StopOthers(Exe);
    try { Autostart.Set(false); } catch { }
    try { if (File.Exists(Shortcut)) File.Delete(Shortcut); } catch { }
    try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
    try { Directory.Delete(Modules.Dir, true); } catch { }  // módulos voltam do cartão numa reinstalação
    Guard.SetFlag("vigia-desligado", false);
    Guard.SetFlag("sair", false);
    Log.Write("desinstalado");
    // o próprio .exe está rodando: a pasta some logo depois que ele fechar
    try {
      Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + Dir + "\"") {
        CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    } catch { }
    if (!quiet) MessageBox.Show("O NEXDECK HUB foi removido.", NAME, MessageBoxButtons.OK, MessageBoxIcon.Information);
  }
}

// janela pequena do instalador: ícone, título e o passo atual; fecha sozinha quando o HUB abre
class SetupForm : Form {
  readonly Label step = new Label();
  readonly Button close = new Button();

  public SetupForm() {
    Text = "Instalar NEXDECK HUB";
    Icon = Theme.AppIcon(32);
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = MinimizeBox = false;
    StartPosition = FormStartPosition.CenterScreen;
    ClientSize = new Size(420, 210);
    BackColor = Theme.Back;
    ForeColor = Theme.Text;
    var pic = new PictureBox { Image = Theme.AppIcon(64).ToBitmap(), SizeMode = PictureBoxSizeMode.CenterImage, Bounds = new Rectangle(178, 22, 64, 64) };
    var title = new Label { Text = "NEXDECK HUB", Font = Theme.F(15, true), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, 94, 420, 34) };
    step.Font = Theme.F(10);
    step.ForeColor = Theme.Muted;
    step.TextAlign = ContentAlignment.MiddleCenter;
    step.Bounds = new Rectangle(16, 130, 388, 40);
    close.Text = "Fechar";
    close.FlatStyle = FlatStyle.Flat;
    close.FlatAppearance.BorderColor = Theme.Border;
    close.BackColor = Theme.Card;
    close.ForeColor = Theme.Text;
    close.Bounds = new Rectangle(160, 168, 100, 30);
    close.Visible = false;
    close.Click += (s, e) => Close();
    Controls.AddRange(new Control[] { pic, title, step, close });
    Shown += (s, e) => new Thread(Work) { IsBackground = true }.Start();
  }

  void Say(string s) { try { BeginInvoke((Action)(() => step.Text = s)); } catch { } }

  void Work() {
    string err = Setup.DoInstall(Say);
    if (err == null) {
      Say("Pronto! Conecte a placa: os apps do cartão aparecem sozinhos no HUB.");
      Thread.Sleep(2500);
      try { BeginInvoke((Action)Close); } catch { }
    } else {
      try {
        BeginInvoke((Action)(() => {
          step.ForeColor = Theme.Bad;
          step.Text = "Não deu para instalar: " + err;
          close.Visible = true;
        }));
      } catch { }
    }
  }
}

}  // namespace NexDeck
