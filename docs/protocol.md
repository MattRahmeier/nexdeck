# Board ⇄ PC protocol

USB serial (CH340). Two dialects:

| Who is running on the board | Baud | Framing |
|---|---|---|
| an **app** that talks to the PC | **460800** | checksummed lines, `K`/`E` answers, checksummed blocks |
| the **NEXDECK launcher** | **921600** | plain text commands (for tools and the HUB) |

Open the port with **DTR and RTS low** (otherwise the board may reset).

## App dialect (460800)

### PC → board: lines

```
<field>\t<field>\t...\t~<sum16 hex>\n
```

`sum16` = sum of the UTF-8 bytes before `\t~`, modulo 65536, lowercase hex (4 digits). The board answers **one
line** to every line: `K` (accepted — also for commands it ignores) or `E` (bad checksum: resend). The HUB
resends up to 4 times and treats silence for 2.5 s as a lost connection. Keep lines under ~2 KB.

C# (`Link.Send`) and C++ (`sqlink::checkLine`) implement this.

### Board → PC: events

Plain lines starting with `EV\t`, no checksum, sent any time (also between a PC line and its `K`):

```
EV\tHELLO\t<app name>[\t<fields>...]    the app started, or answered HI
EV\t<NAME>\t<fields>...                  anything the app wants to report (touches, requests)
```

### Blocks (images, files)

```
PC:    B\t<kind>\t<arg>\t<bytes>\t~sum        (images)    or    FPUT\t<path>\t<bytes>\t~sum   (files to SD)
board: OK                                     (or ERR ...)
PC:    <up to 2048 bytes><sum16 LE, 2 bytes>   repeated
board: K   (next block)  |  R  (resend this block)  |  ERR ... (give up)
board: DONE                                   after the last block
```

The block checksum is the plain 16-bit sum of the block bytes, little-endian. `sqlink::receiveChunks` handles it.
An app decides what `kind`/`arg` mean (e.g. `B\tPHOTO\t0\t<len>` = a 480×480 JPEG).

### Commands every app should support (`sqlink::common`)

| PC sends | Board does |
|---|---|
| `HI` | sends `EV\tHELLO\t<name>...`, then `K` (the app answers this itself) |
| `P` | ping every 5 s: just `K`. Apps can treat ~15 s without lines as "PC gone" |
| `LS` | `EV\tAPPS\t<id>,<bytes>;<id>,<bytes>...` (from `/apps/*.bin`), then `K` |
| `FPUT\t<path>\t<bytes>` | receives a file in blocks to `<path>.tmp`, renames to `<path>`, `DONE` |
| `RM\t<path>` | deletes a file: `K` or `ERR` |
| `OPENAPP\t<id>` | `K`, then returns to the launcher, which opens `<id>` immediately |

Paths must start with `/` and cannot contain `..`.

### Conventions

- App name in `HELLO` = lowercase, no spaces (e.g. `hello_pc`). It selects the HUB module (`HubApp.Hello`).
- Put extra state the PC must know in HELLO fields (e.g. a setting changed on the board).
- Time: send local time as Unix-like seconds of the PC's local clock (`T\t<seconds>`) and keep a base +
  `millis()` on the board (there is no RTC).
- Text must only use characters the fonts have (`Txt.Clean` on the PC).
- Tabs and newlines are separators: escape newlines inside a field (e.g. `\n` written as backslash + n).

## Launcher dialect (921600)

| Command | Answer |
|---|---|
| `LS` | one line per app: `<id> <bytes>[ (instalado)]`, then `END` |
| `PUT <path> <bytes>` | `OK`, then the PC sends 4096-byte blocks, each answered `K`; finally `DONE <bytes>` |
| `RUN <id>` | installs if needed and opens the app (like a tap) |
| `RM <path>` | `OK` or `ERR ...` |

`tools/push.ps1` speaks this dialect (`-File/-Dest`, `-Run`, `-Remove`, `-List`). The CH340 sometimes drops
bytes at 921600: `push.ps1` retries a whole file if a block fails.
