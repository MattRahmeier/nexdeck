// NEXDECK HUB — QR Code encoder (ISO/IEC 18004), no external libraries.
// Byte mode (UTF-8), error correction M (falls back to L if it doesn't fit), versions 1–10
// (up to ~270 bytes: links, Wi-Fi strings, Pix "copia e cola"). Based on the public algorithm
// description by Project Nayuki. Apps send the module matrix to the board, which draws it crisp
// (see QrCode.ToHex and docs/protocol.md).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace NexDeck {

public sealed class QrCode {
  public readonly int Size;  // modules per side (21..57)
  readonly bool[,] mod, fn;  // [row, col]

  public bool this[int row, int col] { get { return mod[row, col]; } }

  static readonly int[] ECC_M = { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 }, BLK_M = { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
  static readonly int[] ECC_L = { 7, 10, 15, 20, 26, 18, 20, 24, 30, 18 }, BLK_L = { 1, 1, 1, 1, 1, 2, 2, 2, 2, 4 };
  static readonly int[] RAW = { 26, 44, 70, 100, 134, 172, 196, 242, 292, 346 };  // codewords per version
  static readonly int[][] ALIGN = {
    new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
    new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
  };

  public static QrCode Encode(string text) {
    byte[] data = Encoding.UTF8.GetBytes(text ?? "");
    for (int level = 0; level < 2; level++)  // 0 = M, 1 = L
      for (int v = 1; v <= 10; v++) {
        int ecc = (level == 0 ? ECC_M : ECC_L)[v - 1], blocks = (level == 0 ? BLK_M : BLK_L)[v - 1];
        int dataCw = RAW[v - 1] - ecc * blocks;
        if (4 + (v < 10 ? 8 : 16) + data.Length * 8 <= dataCw * 8)
          return new QrCode(v, level == 0 ? 0 : 1, data, ecc, blocks, dataCw);
      }
    throw new ArgumentException("texto longo demais para o QR Code (até cerca de 270 caracteres)");
  }

  QrCode(int ver, int formatEcl, byte[] data, int ecc, int blocks, int dataCw) {
    Size = ver * 4 + 17;
    mod = new bool[Size, Size];
    fn = new bool[Size, Size];
    DrawFunctionPatterns(ver, formatEcl);
    DrawCodewords(AddEcc(MakeData(ver, data, dataCw), ver, ecc, blocks));
    int best = 0;
    long bestPenalty = long.MaxValue;
    for (int m = 0; m < 8; m++) {  // the mask with the lowest penalty wins
      ApplyMask(m);
      DrawFormat(formatEcl, m);
      long p = Penalty();
      if (p < bestPenalty) { bestPenalty = p; best = m; }
      ApplyMask(m);  // XOR again = undo
    }
    ApplyMask(best);
    DrawFormat(formatEcl, best);
  }

  // matrix as hex, row by row, 8 modules per byte, MSB first (what the NEXDECK apps read)
  public string ToHex() {
    var bytes = new byte[(Size * Size + 7) / 8];
    for (int i = 0; i < Size * Size; i++)
      if (mod[i / Size, i % Size]) bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
    var sb = new StringBuilder(bytes.Length * 2);
    foreach (var b in bytes) sb.Append(b.ToString("x2"));
    return sb.ToString();
  }

  public Bitmap ToBitmap(int scale, int quiet = 4) {
    int px = (Size + 2 * quiet) * scale;
    var bmp = new Bitmap(px, px);
    using (var g = Graphics.FromImage(bmp)) {
      g.Clear(Color.White);
      for (int r = 0; r < Size; r++)
        for (int c = 0; c < Size; c++)
          if (mod[r, c]) g.FillRectangle(Brushes.Black, (c + quiet) * scale, (r + quiet) * scale, scale, scale);
    }
    return bmp;
  }

  // ---------------------------------------------------------------- dados
  static byte[] MakeData(int ver, byte[] data, int dataCw) {
    var bits = new List<bool>();
    Action<int, int> put = (val, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
    put(4, 4);  // byte mode
    put(data.Length, ver < 10 ? 8 : 16);
    foreach (var b in data) put(b, 8);
    int cap = dataCw * 8;
    put(0, Math.Min(4, cap - bits.Count));  // terminator
    while (bits.Count % 8 != 0) bits.Add(false);
    for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) put(pad, 8);
    var result = new byte[dataCw];
    for (int i = 0; i < bits.Count; i++) if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
    return result;
  }

  static byte[] AddEcc(byte[] data, int ver, int ecc, int numBlocks) {
    int raw = RAW[ver - 1], numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
    var divisor = RsDivisor(ecc);
    var blocks = new List<byte[]>();
    for (int i = 0, k = 0; i < numBlocks; i++) {
      int len = shortLen - ecc + (i < numShort ? 0 : 1);
      var dat = new byte[len];
      Array.Copy(data, k, dat, 0, len);
      k += len;
      var block = new byte[shortLen + 1];
      Array.Copy(dat, block, len);
      var rem = RsRemainder(dat, divisor);
      Array.Copy(rem, 0, block, block.Length - ecc, ecc);
      blocks.Add(block);
    }
    var result = new List<byte>();
    for (int i = 0; i < shortLen + 1; i++)
      for (int j = 0; j < numBlocks; j++)
        if (i != shortLen - ecc || j >= numShort) result.Add(blocks[j][i]);  // short blocks have no byte there
    return result.ToArray();
  }

  static byte Mul(int x, int y) {  // GF(256), polynomial 0x11D
    int z = 0;
    for (int i = 7; i >= 0; i--) {
      z = (z << 1) ^ ((z >> 7) * 0x11D);
      z ^= ((y >> i) & 1) * x;
    }
    return (byte)z;
  }

  static byte[] RsDivisor(int degree) {
    var r = new byte[degree];
    r[degree - 1] = 1;
    int root = 1;
    for (int i = 0; i < degree; i++) {
      for (int j = 0; j < r.Length; j++) {
        r[j] = Mul(r[j], root);
        if (j + 1 < r.Length) r[j] ^= r[j + 1];
      }
      root = Mul(root, 2);
    }
    return r;
  }

  static byte[] RsRemainder(byte[] data, byte[] divisor) {
    var r = new byte[divisor.Length];
    foreach (byte b in data) {
      int factor = b ^ r[0];
      Array.Copy(r, 1, r, 0, r.Length - 1);
      r[r.Length - 1] = 0;
      for (int i = 0; i < r.Length; i++) r[i] ^= Mul(divisor[i], factor);
    }
    return r;
  }

  // ---------------------------------------------------------------- desenho da matriz
  void Fn(int x, int y, bool dark) { mod[y, x] = dark; fn[y, x] = true; }

  void DrawFunctionPatterns(int ver, int ecl) {
    for (int i = 0; i < Size; i++) { Fn(6, i, i % 2 == 0); Fn(i, 6, i % 2 == 0); }  // timing
    foreach (var c in new[] { new[] { 3, 3 }, new[] { Size - 4, 3 }, new[] { 3, Size - 4 } })  // finders + separators
      for (int dy = -4; dy <= 4; dy++)
        for (int dx = -4; dx <= 4; dx++) {
          int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), x = c[0] + dx, y = c[1] + dy;
          if (x >= 0 && x < Size && y >= 0 && y < Size) Fn(x, y, d != 2 && d != 4);
        }
    var al = ALIGN[ver - 1];
    for (int i = 0; i < al.Length; i++)
      for (int j = 0; j < al.Length; j++) {
        if ((i == 0 && j == 0) || (i == 0 && j == al.Length - 1) || (i == al.Length - 1 && j == 0)) continue;
        for (int dy = -2; dy <= 2; dy++)
          for (int dx = -2; dx <= 2; dx++) Fn(al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
      }
    DrawFormat(ecl, 0);  // reserves the format areas
    if (ver >= 7) {
      int rem = ver;
      for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
      int bits = ver << 12 | rem;
      for (int i = 0; i < 18; i++) {
        bool bit = ((bits >> i) & 1) != 0;
        int a = Size - 11 + i % 3, b = i / 3;
        Fn(a, b, bit);
        Fn(b, a, bit);
      }
    }
  }

  void DrawFormat(int ecl, int mask) {
    int data = ecl << 3 | mask, rem = data;
    for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
    int bits = (data << 10 | rem) ^ 0x5412;
    Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
    for (int i = 0; i <= 5; i++) Fn(8, i, bit(i));
    Fn(8, 7, bit(6));
    Fn(8, 8, bit(7));
    Fn(7, 8, bit(8));
    for (int i = 9; i < 15; i++) Fn(14 - i, 8, bit(i));
    for (int i = 0; i < 8; i++) Fn(Size - 1 - i, 8, bit(i));
    for (int i = 8; i < 15; i++) Fn(8, Size - 15 + i, bit(i));
    Fn(8, Size - 8, true);  // always dark
  }

  void DrawCodewords(byte[] data) {
    int i = 0;
    for (int right = Size - 1; right >= 1; right -= 2) {
      if (right == 6) right = 5;
      for (int vert = 0; vert < Size; vert++)
        for (int j = 0; j < 2; j++) {
          int x = right - j, y = ((right + 1) & 2) == 0 ? Size - 1 - vert : vert;
          if (!fn[y, x] && i < data.Length * 8) { mod[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0; i++; }
        }
    }
  }

  void ApplyMask(int m) {
    for (int y = 0; y < Size; y++)
      for (int x = 0; x < Size; x++) {
        if (fn[y, x]) continue;
        bool inv;
        switch (m) {
          case 0: inv = (x + y) % 2 == 0; break;
          case 1: inv = y % 2 == 0; break;
          case 2: inv = x % 3 == 0; break;
          case 3: inv = (x + y) % 3 == 0; break;
          case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
          case 5: inv = x * y % 2 + x * y % 3 == 0; break;
          case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
          default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
        }
        if (inv) mod[y, x] = !mod[y, x];
      }
  }

  // regras de penalidade da norma (versão simplificada da regra 3; qualquer máscara é válida)
  long Penalty() {
    long p = 0;
    for (int pass = 0; pass < 2; pass++)
      for (int a = 0; a < Size; a++) {
        int run = 1;
        for (int b = 1; b < Size; b++) {
          bool cur = pass == 0 ? mod[a, b] : mod[b, a], prev = pass == 0 ? mod[a, b - 1] : mod[b - 1, a];
          if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
          else run = 1;
        }
        for (int b = 0; b + 10 < Size; b++) {  // 1:1:3:1:1 com 4 claros de um lado
          Func<int, bool> at = k => pass == 0 ? mod[a, b + k] : mod[b + k, a];
          bool core = at(0) && !at(1) && at(2) && at(3) && at(4) && !at(5) && at(6);
          bool lightAfter = !at(7) && !at(8) && !at(9) && !at(10);
          bool lightBefore = !at(0) && !at(1) && !at(2) && !at(3) && at(4) && !at(5) && at(6) && at(7) && at(8) && !at(9) && at(10);
          if ((core && lightAfter) || lightBefore) p += 40;
        }
      }
    for (int y = 0; y + 1 < Size; y++)
      for (int x = 0; x + 1 < Size; x++)
        if (mod[y, x] == mod[y, x + 1] && mod[y, x] == mod[y + 1, x] && mod[y, x] == mod[y + 1, x + 1]) p += 3;
    int dark = 0;
    foreach (bool b in mod) if (b) dark++;
    int total = Size * Size, k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
    return p + k2 * 10;
  }
}

}  // namespace NexDeck
