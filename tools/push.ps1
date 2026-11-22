# Manda arquivos para o cartão SD da placa pela USB, com o NEXLauncher (lançador) aberto na tela.
#   powershell -File tools\push.ps1 -File firmware\build\hello_app\hello_app.ino.bin -Dest /apps/hello_app.bin
#   powershell -File tools\push.ps1 -File minha.jpg -Dest /imagens/minha.jpg
#   powershell -File tools\push.ps1 -Run hello_app      (abre o app, como um toque no menu)
# Rode pelo PowerShell: no Git Bash, /apps/... vira um caminho do Windows.
#   powershell -File tools\push.ps1 -List
#   powershell -File tools\push.ps1 -Remove /apps/velho.bin   (apaga do cartão)
param(
  [string]$Port = "COM3",
  [string]$File,
  [string]$Dest,
  [string]$Run,
  [string[]]$Remove,
  [switch]$List
)
$ErrorActionPreference = "Stop"
$sp = New-Object System.IO.Ports.SerialPort $Port, 921600, 'None', 8, 'One'
$sp.ReadTimeout = 15000
$sp.DtrEnable = $false   # não reinicia a placa ao abrir a porta
$sp.RtsEnable = $false
$sp.NewLine = "`n"
$sp.Open()
$sp.Write("`n")   # termina qualquer linha pela metade que tenha ficado na placa
Start-Sleep -Milliseconds 300
$sp.DiscardInBuffer()

function Read-Reply {
  while ($true) {
    $l = $sp.ReadLine().Trim()
    if ($l -match '^(OK|K|DONE|ERR|END)') { return $l }
    if ($l) { Write-Host "  placa: $l" }
  }
}

try {
  if ($File) {
    if (-not $Dest) { $Dest = "/apps/" + [IO.Path]::GetFileName($File) }
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path $File))
    # a serial da CH340 às vezes perde bytes a 921600: se um bloco falhar, manda o arquivo de novo
    for ($try = 1; $try -le 4; $try++) {
      try {
        $sp.WriteLine("PUT $Dest $($bytes.Length)")
        $r = Read-Reply
        if ($r -ne "OK") { throw "a placa recusou: $r (o lançador está aberto e com cartão?)" }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        for ($off = 0; $off -lt $bytes.Length; $off += 4096) {
          $n = [Math]::Min(4096, $bytes.Length - $off)
          $sp.Write($bytes, $off, $n)
          $r = Read-Reply
          if ($r -ne "K") { throw "falhou em $off bytes: $r" }
        }
        $r = Read-Reply
        "{0}: {1} bytes em {2:N1} s ({3})" -f $Dest, $bytes.Length, $sw.Elapsed.TotalSeconds, $r
        break
      } catch {
        if ($try -eq 4 -or "$_" -like "*recusou*") { throw }
        Write-Host "tentativa $try falhou ($_); tentando de novo"
        Start-Sleep -Milliseconds 6000   # a placa desiste do bloco incompleto após 5 s
        $sp.Write("`n"); Start-Sleep -Milliseconds 300; $sp.DiscardInBuffer()
      }
    }
  }
  foreach ($r in $Remove) {
    $sp.WriteLine("RM $r")
    "{0}: {1}" -f $r, (Read-Reply)
  }
  if ($List) {
    $sp.WriteLine("LS")
    while (($l = $sp.ReadLine().Trim()) -ne "END") { $l }
  }
  if ($Run) {
    $sp.WriteLine("RUN $Run")
    Start-Sleep -Milliseconds 500
    $sp.ReadExisting()
  }
} finally {
  $sp.Close()
}
