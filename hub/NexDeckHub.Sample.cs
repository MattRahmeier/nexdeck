// NEXDECK HUB — example app module: the PC side of firmware/templates/hello_pc.
// Copy this file to start the PC side of your own app (see docs/hub-modules.md).
//
// Protocol of this example (lines are sent with link.Send, which adds the checksum and waits for K):
//   PC → board:  MSG\t<text>        message typed in the HUB page
//                TIME\t<HH:mm:ss>   PC clock, every second
//   board → PC:  EV\tHELLO\thello_pc   (the app just opened)
//                EV\tTAP\t<count>      (the screen was touched)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace NexDeck {

public class HelloPcApp : HubApp {
  public override string Id { get { return "hello_pc"; } }      // /apps/hello_pc.bin on the SD card
  public override string Hello { get { return "hello_pc"; } }   // name sent in EV HELLO
  public override string Title { get { return "Hello PC"; } }
  public override bool HasSettings { get { return true; } }

  volatile int taps;
  DateTime lastTime;
  Field message;
  Label tapsLabel;

  string Message { get { return Cfg.Get(Id, "message", "Olá do PC!"); } }

  // the app opened on the board: send everything it needs
  public override void OnConnect(Link link, string[] hello) {
    Worker.Must(link.Send("MSG\t" + Txt.Clean(Message, 120)));
    lastTime = DateTime.MinValue;
  }

  // called repeatedly while the app is open (the HUB already pings and reads events)
  public override void Loop(Link link) {
    if ((DateTime.Now - lastTime).TotalSeconds >= 1) {
      lastTime = DateTime.Now;
      Worker.Must(link.Send("TIME\t" + DateTime.Now.ToString("HH:mm:ss")));
    }
    System.Threading.Thread.Sleep(20);
  }

  public override bool OnEvent(Link link, string[] ev) {
    if (ev[1] == "TAP" && ev.Length > 2) { int.TryParse(ev[2], out taps); return true; }
    return false;
  }

  // settings page: one section with a text field
  public override void BuildSettings(AppPage page) {
    var flow = page.Section("Mensagem", "news");
    var c = AppPage.NewCard(flow, 100);
    AppPage.Text(c, "Texto mostrado na placa", 18, 16, 400, 22, 10.5f, true);
    message = new Field(Message, 664, "Escreva uma mensagem") { Location = new Point(18, 46) };
    c.Controls.Add(message);
    var c2 = AppPage.NewCard(flow, 64);
    tapsLabel = AppPage.Text(c2, "", 18, 20, 600, 24, 10.5f);
  }

  public override void Save() {
    Cfg.Set(Id, "message", message.Text.Trim());
    W.ResyncRequested = true;  // OnConnect runs again and sends the new message
  }

  public override void Tick() {
    tapsLabel.Text = W.RunningApp == Id ? "Toques na placa: " + taps : "Abra o Hello PC na placa para ver os toques.";
  }
}

}  // namespace NexDeck
