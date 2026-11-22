using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SbsReport.Services;

/// <summary>
/// Harici bağımlılıksız, düşük RAM tüketimli, ultra-kompakt saf C# PDF 1.4 üreticisi (v1.0.4).
/// - 1-byte Türkçe/WinAnsi (/Subtype /TrueType + Windows-1254 /Differences + /ToUnicode) hızlı yolu;
///   tablo dışı Unicode karakter varsa otomatik 2-byte CIDFontType2 yedeğine geçer.
/// - Gerçek TrueType glyf/loca alt kümeleme (font subsetting): yalnızca kullanılan glifleri gömer (~20 KB vs ~240 KB).
/// - 2 geçişli sayfa akışı: (1) Arka plan + toplu kenarlık (tek S), (2) Sayfa başına tek BT..ET bloğu + göreli Td konumlandırma.
/// - ArrayPool&lt;byte&gt; + ZLibStream(Optimal) ile sıfır LOH baskısı.
/// </summary>
internal static class SbsPdfWriter
{
    private const double MmToPt = 72.0 / 25.4;
    private static readonly ConcurrentDictionary<string, TtfFontInfo> FontCache = new(StringComparer.OrdinalIgnoreCase);

    // Windows-1254 (Turkish) <-> Unicode lookup tables for 1-byte TrueType encoding
    private static readonly byte[] CharToWin1254 = BuildCharToWin1254();
    private static readonly char[] Win1254ToChar = BuildWin1254ToChar();

    public static byte[] Generate(RenderedReport r)
    {
        double pageW = r.PageWidthMm * MmToPt;
        double pageH = r.PageHeightMm * MmToPt;
        var pages = r.Pages.Count > 0 ? r.Pages : new List<RenderedPage> { new() { PageNumber = 1 } };

        var regFont = LoadTtfCached("verdana.ttf", "trebuc.ttf", "arial.ttf");

        // Single fast scan: check if Bold is needed AND if all renderable characters fit in 1-byte Windows-1254
        bool needBold = r.Watermark?.Enabled == true;
        bool useSingleByte = true;
        if (r.Watermark?.Enabled == true && !string.IsNullOrEmpty(r.Watermark.Text))
        {
            foreach (char ch in r.Watermark.Text)
                if (!CanEncodeWin1254(ch) && regFont.GetGlyphId(ch) != 0) { useSingleByte = false; break; }
        }
        foreach (var p in pages)
        {
            foreach (var el in p.Elements)
            {
                var t = el.Text;
                if (string.IsNullOrEmpty(t)) continue;
                if (el.Bold) needBold = true;
                if (useSingleByte)
                {
                    for (int i = 0; i < t.Length; i++)
                    {
                        char ch = t[i];
                        if (ch == '\r' || ch == '\n') continue;
                        if (!CanEncodeWin1254(ch) && regFont.GetGlyphId(ch) != 0) { useSingleByte = false; break; }
                    }
                }
            }
        }

        var boldFont = needBold ? LoadTtfCached("verdanab.ttf", "trebucbd.ttf", "arialbd.ttf") : regFont;

        var usedRegGids = new SortedDictionary<ushort, char>();
        var usedBoldGids = new SortedDictionary<ushort, char>();
        var usedRegBytes = new bool[256];
        var usedBoldBytes = new bool[256];

        using var outMs = new MemoryStream(32768);
        var offsets = new List<long>(pages.Count * 2 + 24);

        void WriteAscii(string s)
        {
            var b = Encoding.Latin1.GetBytes(s);
            outMs.Write(b, 0, b.Length);
        }

        int BeginObj()
        {
            offsets.Add(outMs.Position);
            int id = offsets.Count;
            WriteAscii($"{id} 0 obj\n");
            return id;
        }

        void EndObj() => WriteAscii("\nendobj\n");

        int WriteObj(string body)
        {
            int id = BeginObj();
            WriteAscii(body);
            EndObj();
            return id;
        }

        int WriteCompressedBytes(byte[] rawBytes, string extraDict = "")
        {
            using var ms = new MemoryStream(rawBytes.Length / 2 + 64);
            using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                zs.Write(rawBytes, 0, rawBytes.Length);
            int len = (int)ms.Length;
            int id = BeginObj();
            WriteAscii($"<< /Length {len} /Filter /FlateDecode {extraDict}>>\nstream\n");
            outMs.Write(ms.GetBuffer(), 0, len);
            WriteAscii("\nendstream");
            EndObj();
            return id;
        }

        WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        var pageContentIds = new List<int>(pages.Count);
        using var pageRawBuf = new MemoryStream(16384);
        using var compBuf = new MemoryStream(8192);
        var sb = new StringBuilder(16384);
        byte[] chunkPool = ArrayPool<byte>.Shared.Rent(8192);

        void FlushSbToRaw()
        {
            if (sb.Length == 0) return;
            foreach (var chunk in sb.GetChunks())
            {
                var span = chunk.Span;
                if (span.Length > chunkPool.Length)
                {
                    ArrayPool<byte>.Shared.Return(chunkPool);
                    chunkPool = ArrayPool<byte>.Shared.Rent(span.Length);
                }
                int n = Encoding.Latin1.GetBytes(span, chunkPool);
                pageRawBuf.Write(chunkPool, 0, n);
            }
            sb.Clear();
        }

        void WriteGlyphString(TtfFontInfo font, string text, SortedDictionary<ushort, char> usedGids, bool[] usedBytes)
        {
            FlushSbToRaw();
            pageRawBuf.WriteByte((byte)'(');
            if (useSingleByte)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = text[i];
                    if (!CanEncodeWin1254(ch)) continue;
                    byte b = EncodeWin1254(ch);
                    if (!usedBytes[b])
                    {
                        usedBytes[b] = true;
                        usedGids.TryAdd(font.GetGlyphId(ch), ch);
                    }
                    WritePdfEscapedByte(pageRawBuf, b);
                }
            }
            else
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = text[i];
                    ushort gid = font.GetGlyphId(ch);
                    usedGids.TryAdd(gid, ch);
                    WritePdfEscapedByte(pageRawBuf, (byte)(gid >> 8));
                    WritePdfEscapedByte(pageRawBuf, (byte)(gid & 0xFF));
                }
            }
            pageRawBuf.WriteByte((byte)')');
        }

        try
        {
            foreach (var page in pages)
            {
                pageRawBuf.SetLength(0);
                sb.Clear();

                string curFill = "";
                string curStroke = "";
                double curLw = -1;
                bool hasPendingStroke = false;

                void FlushStroke()
                {
                    if (hasPendingStroke)
                    {
                        sb.Append("S\n");
                        hasPendingStroke = false;
                    }
                }

                void SetFill(double r0, double g0, double b0)
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", r0, g0, b0);
                    if (key != curFill)
                    {
                        FlushStroke();
                        curFill = key;
                        sb.Append(key).Append(" rg\n");
                    }
                }

                void SetStroke(double r0, double g0, double b0, double lw)
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", r0, g0, b0);
                    double lwR = Math.Round(lw, 2);
                    if (key != curStroke || Math.Abs(lwR - curLw) > 0.005)
                    {
                        FlushStroke();
                        if (key != curStroke)
                        {
                            curStroke = key;
                            sb.Append(key).Append(" RG\n");
                        }
                        if (Math.Abs(lwR - curLw) > 0.005)
                        {
                            curLw = lwR;
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} w\n", lwR);
                        }
                    }
                }

                // Watermark
                if (r.Watermark?.Enabled == true && !string.IsNullOrWhiteSpace(r.Watermark.Text))
                {
                    var (wr, wg, wb) = ParseHexColor(r.Watermark.Color, 0.82, 0.85, 0.88);
                    double rad = -r.Watermark.Angle * Math.PI / 180.0;
                    double cos = Math.Cos(rad), sin = Math.Sin(rad);
                    double fs = Math.Min(72, Math.Max(18, r.Watermark.FontSizePt));
                    double tw = boldFont.MeasureWidth(r.Watermark.Text, fs);
                    double cx = pageW / 2.0, cy = pageH / 2.0;
                    double blend = 1.0 - Math.Clamp(r.Watermark.Opacity, 0.05, 0.4);
                    double rCol = wr + (1 - wr) * blend, gCol = wg + (1 - wg) * blend, bCol = wb + (1 - wb) * blend;

                    sb.Append("q\n");
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##} rg\n", rCol, gCol, bCol);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.####} {1:0.####} {2:0.####} {3:0.####} {4:0.#} {5:0.#} cm\n",
                        cos, sin, -sin, cos, cx - (tw / 2.0) * cos, cy - (tw / 2.0) * sin);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "BT /F2 {0:0.#} Tf 0 0 Td ", fs);
                    WriteGlyphString(boldFont, r.Watermark.Text, usedBoldGids, usedBoldBytes);
                    sb.Append(" Tj ET\nQ\n");
                }

                // === PASS 1: Backgrounds, Lines & Batched Borders (single 'S' per stroke style!) ===
                foreach (var el in page.Elements)
                {
                    double x = el.XMm * MmToPt;
                    double w = Math.Max(0.5, el.WidthMm * MmToPt);
                    double h = Math.Max(0.5, el.HeightMm * MmToPt);
                    double y = pageH - (el.YMm * MmToPt) - h;

                    bool hasBg = !string.IsNullOrWhiteSpace(el.BackgroundColor) &&
                                 !el.BackgroundColor.Equals("transparent", StringComparison.OrdinalIgnoreCase);
                    bool hasBorder = el.BorderWidthPx > 0 && !string.Equals(el.BorderSides, "None", StringComparison.OrdinalIgnoreCase);

                    if (el.Type.Equals("Line", StringComparison.OrdinalIgnoreCase))
                    {
                        var (lr, lg, lb) = ParseHexColor(el.Color, 0.07, 0.09, 0.15);
                        SetStroke(lr, lg, lb, Math.Max(0.5, el.BorderWidthPx * 0.75));
                        if (string.Equals(el.LineOrientation, "Vertical", StringComparison.OrdinalIgnoreCase))
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} m {0:0.#} {2:0.#} l\n", x, y, y + h);
                        else
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} m {2:0.#} {1:0.#} l\n", x, y + h / 2.0, x + w);
                        hasPendingStroke = true;
                        continue;
                    }

                    if (hasBg)
                    {
                        var (br, bg, bb) = ParseHexColor(el.BackgroundColor, 1, 1, 1);
                        // Skip pure white background rectangles on white PDF canvas
                        if (!(br >= 0.999 && bg >= 0.999 && bb >= 0.999))
                        {
                            FlushStroke();
                            SetFill(br, bg, bb);
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} {2:0.#} {3:0.#} re f\n", x, y, w, h);
                        }
                    }

                    if (hasBorder)
                    {
                        var (cr, cg, cb) = ParseHexColor(el.BorderColor, 0.8, 0.82, 0.86);
                        SetStroke(cr, cg, cb, Math.Max(0.4, el.BorderWidthPx * 0.75));
                        var sides = el.BorderSides ?? "All";
                        if (sides is "All" or "Box")
                        {
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} {2:0.#} {3:0.#} re\n", x, y, w, h);
                        }
                        else
                        {
                            if (sides.Contains("Top", StringComparison.OrdinalIgnoreCase))
                                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} m {2:0.#} {1:0.#} l\n", x, y + h, x + w);
                            if (sides.Contains("Bottom", StringComparison.OrdinalIgnoreCase))
                                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} m {2:0.#} {1:0.#} l\n", x, y, x + w);
                            if (sides.Contains("Left", StringComparison.OrdinalIgnoreCase))
                                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} m {0:0.#} {2:0.#} l\n", x, y, y + h);
                            if (sides.Contains("Right", StringComparison.OrdinalIgnoreCase))
                                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} m {0:0.#} {2:0.#} l\n", x + w, y, y + h);
                        }
                        hasPendingStroke = true;
                    }
                }
                FlushStroke();

                // === PASS 2: Single Batched BT .. ET Text Pass with Relative Td Positioning ===
                bool inTextObj = false;
                string curFontTag = "";
                double curFontSize = -1;
                double textX = 0, textY = 0;

                void EnsureTextOpen(string fTag, double fs, double tr, double tg, double tb)
                {
                    if (!inTextObj)
                    {
                        sb.Append("BT\n");
                        inTextObj = true;
                        curFontTag = "";
                        curFontSize = -1;
                        textX = 0;
                        textY = 0;
                    }
                    string fKey = string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", tr, tg, tb);
                    if (fKey != curFill)
                    {
                        curFill = fKey;
                        sb.Append(fKey).Append(" rg\n");
                    }
                    if (fTag != curFontTag || Math.Abs(fs - curFontSize) > 0.05)
                    {
                        curFontTag = fTag;
                        curFontSize = fs;
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:0.#} Tf\n", fTag, fs);
                    }
                }

                void MoveTextTo(double tx, double ty)
                {
                    double rx = Math.Round(tx, 1);
                    double ry = Math.Round(ty, 1);
                    double dx = Math.Round(rx - textX, 1);
                    double dy = Math.Round(ry - textY, 1);
                    textX = rx;
                    textY = ry;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} Td ", dx, dy);
                }

                void CloseTextIfOpen()
                {
                    if (inTextObj)
                    {
                        sb.Append("ET\n");
                        inTextObj = false;
                    }
                }

                foreach (var el in page.Elements)
                {
                    if (el.Type.Equals("Line", StringComparison.OrdinalIgnoreCase) ||
                        el.Type.Equals("Shape", StringComparison.OrdinalIgnoreCase) ||
                        el.Type.Equals("Image", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var txt = el.Text;
                    if (el.Type == "CheckBox") txt = (el.Checked ? "[X] " : "[ ] ") + (el.Text ?? el.Label ?? "");
                    else if (el.Type == "Radio") txt = (el.Checked ? "(*) " : "( ) ") + (el.Text ?? el.Label ?? "");
                    if (string.IsNullOrWhiteSpace(txt)) continue;

                    double x = el.XMm * MmToPt;
                    double w = Math.Max(0.5, el.WidthMm * MmToPt);
                    double h = Math.Max(0.5, el.HeightMm * MmToPt);
                    double y = pageH - (el.YMm * MmToPt) - h;

                    var fInfo = el.Bold ? boldFont : regFont;
                    var usedGids = el.Bold ? usedBoldGids : usedRegGids;
                    var usedBytes = el.Bold ? usedBoldBytes : usedRegBytes;
                    string fTag = el.Bold && needBold ? "/F2" : "/F1";
                    double fs = Math.Max(5.5, el.FontSizePt);
                    var (tr, tg, tb) = ParseHexColor(el.Color, 0.07, 0.09, 0.15);
                    double maxW = Math.Max(4, w - 4);

                    // Fast single-line path
                    if (!txt.Contains('\n'))
                    {
                        double tw = fInfo.MeasureWidth(txt, fs);
                        if (tw <= maxW || !el.WordWrap)
                        {
                            double tx = el.HAlign == "right" ? x + w - tw - 2.5
                                : el.HAlign == "center" ? x + (w - tw) / 2.0
                                : x + 2.5;
                            double ty = el.VAlign == "top" ? y + h - fs - 1.5
                                : el.VAlign == "bottom" ? y + 2.5
                                : y + (h - fs * 0.72) / 2.0;

                            EnsureTextOpen(fTag, fs, tr, tg, tb);
                            MoveTextTo(tx, ty);
                            WriteGlyphString(fInfo, txt, usedGids, usedBytes);
                            sb.Append(" Tj\n");
                            continue;
                        }
                    }

                    var lines = WrapLines(fInfo, txt, fs, maxW, el.WordWrap);
                    double lineH = fs * 1.22;
                    double totalH = lines.Count * lineH;
                    double startY = el.VAlign == "top" ? y + h - fs - 1.5
                        : el.VAlign == "bottom" ? y + 2.5 + (lines.Count - 1) * lineH
                        : y + (h + totalH) / 2.0 - fs * 0.9;

                    bool clip = totalH > h;
                    if (clip)
                    {
                        CloseTextIfOpen();
                        sb.AppendFormat(CultureInfo.InvariantCulture, "q {0:0.#} {1:0.#} {2:0.#} {3:0.#} re W n\n", x, y, w, h);
                    }

                    EnsureTextOpen(fTag, fs, tr, tg, tb);
                    for (int li = 0; li < lines.Count; li++)
                    {
                        var line = lines[li];
                        if (line.Length == 0) continue;
                        double tw = fInfo.MeasureWidth(line, fs);
                        double tx = el.HAlign == "right" ? x + w - tw - 2.5
                            : el.HAlign == "center" ? x + (w - tw) / 2.0
                            : x + 2.5;
                        double ty = startY - li * lineH;
                        MoveTextTo(tx, ty);
                        WriteGlyphString(fInfo, line, usedGids, usedBytes);
                        sb.Append(" Tj\n");
                    }

                    if (clip)
                    {
                        CloseTextIfOpen();
                        sb.Append("Q\n");
                        curFill = "";
                    }
                }
                CloseTextIfOpen();
                FlushSbToRaw();

                compBuf.SetLength(0);
                using (var zs = new ZLibStream(compBuf, CompressionLevel.Optimal, leaveOpen: true))
                    zs.Write(pageRawBuf.GetBuffer(), 0, (int)pageRawBuf.Length);

                int clen = (int)compBuf.Length;
                int cid = BeginObj();
                WriteAscii($"<< /Length {clen} /Filter /FlateDecode >>\nstream\n");
                outMs.Write(compBuf.GetBuffer(), 0, clen);
                WriteAscii("\nendstream");
                EndObj();
                pageContentIds.Add(cid);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkPool);
        }

        // Write subsetted embedded TrueType fonts
        int f1Id, f2Id;
        if (useSingleByte)
        {
            int encId = WriteObj(
                "<< /Type /Encoding /BaseEncoding /WinAnsiEncoding " +
                "/Differences [208 /Gbreve 221 /Idotaccent /Scedilla 240 /gbreve 253 /dotlessi /scedilla] >>");
            f1Id = WriteSingleByteTrueTypeFont(regFont, usedRegGids, usedRegBytes, encId, WriteObj, WriteCompressedBytes);
            f2Id = (needBold && !ReferenceEquals(boldFont, regFont))
                ? WriteSingleByteTrueTypeFont(boldFont, usedBoldGids, usedBoldBytes, encId, WriteObj, WriteCompressedBytes)
                : f1Id;
        }
        else
        {
            f1Id = WriteCidTrueTypeFont(regFont, usedRegGids, WriteObj, WriteCompressedBytes);
            f2Id = (needBold && !ReferenceEquals(boldFont, regFont))
                ? WriteCidTrueTypeFont(boldFont, usedBoldGids, WriteObj, WriteCompressedBytes)
                : f1Id;
        }

        int firstPageObjId = offsets.Count + 1;
        int pagesObjId = firstPageObjId + pages.Count;
        var pageIds = new List<int>(pages.Count);

        for (int i = 0; i < pages.Count; i++)
        {
            int pid = WriteObj(string.Format(CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent {0} 0 R /MediaBox [0 0 {1:0.#} {2:0.#}] /Resources << /Font << /F1 {3} 0 R /F2 {4} 0 R >> >> /Contents {5} 0 R >>",
                pagesObjId, pageW, pageH, f1Id, f2Id, pageContentIds[i]));
            pageIds.Add(pid);
        }

        int actualPagesId = WriteObj($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>");
        int catalogId = WriteObj($"<< /Type /Catalog /Pages {actualPagesId} 0 R >>");

        long xrefPos = outMs.Position;
        WriteAscii($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        for (int i = 0; i < offsets.Count; i++)
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        WriteAscii($"trailer\n<< /Size {offsets.Count + 1} /Root {catalogId} 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        return outMs.ToArray();
    }

    private static void WritePdfEscapedByte(MemoryStream ms, byte b)
    {
        if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\')
        {
            ms.WriteByte((byte)'\\');
            ms.WriteByte(b);
        }
        else if (b == (byte)'\r')
        {
            ms.WriteByte((byte)'\\');
            ms.WriteByte((byte)'r');
        }
        else if (b == (byte)'\n')
        {
            ms.WriteByte((byte)'\\');
            ms.WriteByte((byte)'n');
        }
        else
        {
            ms.WriteByte(b);
        }
    }

    private static int WriteSingleByteTrueTypeFont(
        TtfFontInfo font,
        SortedDictionary<ushort, char> usedGids,
        bool[] usedBytes,
        int encId,
        Func<string, int> writeObj,
        Func<byte[], string, int> writeCompressedStream)
    {
        byte[] subsetTtf = font.BuildSubset(usedGids.Keys);
        int fontFile2Id = writeCompressedStream(subsetTtf, $"/Length1 {subsetTtf.Length} ");
        string subsetName = $"SBSRPT+{font.PdfName}";

        int descId = writeObj(
            $"<< /Type /FontDescriptor /FontName /{subsetName} /Flags 32 " +
            $"/FontBBox [-200 -250 1200 1000] /ItalicAngle 0 /Ascent {font.Ascent} /Descent {font.Descent} /CapHeight {font.Ascent} /StemV 80 " +
            $"/FontFile2 {fontFile2Id} 0 R >>");

        int firstChar = 32, lastChar = 254;
        var wSb = new StringBuilder(1024);
        wSb.Append('[');
        for (int c = firstChar; c <= lastChar; c++)
        {
            if (c > firstChar) wSb.Append(' ');
            char uni = Win1254ToChar[c];
            int w = uni != '\0' ? font.GetGlyphWidth1000(font.GetGlyphId(uni)) : 0;
            wSb.Append(w);
        }
        wSb.Append(']');

        // ToUnicode CMap for 100% accurate Turkish text search & copy/paste
        var cmapSb = new StringBuilder();
        cmapSb.Append("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n");
        cmapSb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmapSb.Append("/CMapName /Adobe-Identity-UCS def /CMapType 2 def\n");
        cmapSb.Append("1 begincodespacerange <00> <FF> endcodespacerange\n");
        var entries = new List<(byte B, char C)>(128);
        for (int c = 32; c <= 255; c++)
            if (usedBytes[c] && Win1254ToChar[c] != '\0')
                entries.Add(((byte)c, Win1254ToChar[c]));
        for (int i = 0; i < entries.Count; i += 100)
        {
            int batch = Math.Min(100, entries.Count - i);
            cmapSb.Append($"{batch} beginbfchar\n");
            for (int j = 0; j < batch; j++)
            {
                var (b, ch) = entries[i + j];
                cmapSb.Append($"<{b:X2}> <{(int)ch:X4}>\n");
            }
            cmapSb.Append("endbfchar\n");
        }
        cmapSb.Append("endcmap CMapName currentdict /CMap defineresource pop end end\n");
        int toUnicodeId = writeCompressedStream(Encoding.ASCII.GetBytes(cmapSb.ToString()), "");

        return writeObj(
            $"<< /Type /Font /Subtype /TrueType /BaseFont /{subsetName} " +
            $"/FirstChar {firstChar} /LastChar {lastChar} /Widths {wSb} " +
            $"/FontDescriptor {descId} 0 R /Encoding {encId} 0 R /ToUnicode {toUnicodeId} 0 R >>");
    }

    private static int WriteCidTrueTypeFont(
        TtfFontInfo font,
        SortedDictionary<ushort, char> usedGlyphs,
        Func<string, int> writeObj,
        Func<byte[], string, int> writeCompressedStream)
    {
        byte[] subsetTtf = font.BuildSubset(usedGlyphs.Keys);
        int fontFile2Id = writeCompressedStream(subsetTtf, $"/Length1 {subsetTtf.Length} ");
        string subsetName = $"SBSRPT+{font.PdfName}";

        int descId = writeObj(
            $"<< /Type /FontDescriptor /FontName /{subsetName} /Flags 32 " +
            $"/FontBBox [-200 -250 1200 1000] /ItalicAngle 0 /Ascent {font.Ascent} /Descent {font.Descent} /CapHeight {font.Ascent} /StemV 80 " +
            $"/FontFile2 {fontFile2Id} 0 R >>");

        var wSb = new StringBuilder();
        wSb.Append('[');
        foreach (var gid in usedGlyphs.Keys)
            wSb.Append(gid).Append('[').Append(font.GetGlyphWidth1000(gid)).Append("] ");
        wSb.Append(']');

        int cidFontId = writeObj(
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{subsetName} " +
            $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descId} 0 R /DW 600 /W {wSb} /CIDToGIDMap /Identity >>");

        var cmapSb = new StringBuilder();
        cmapSb.Append("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n");
        cmapSb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmapSb.Append("/CMapName /Adobe-Identity-UCS def /CMapType 2 def\n");
        cmapSb.Append("1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
        var entries = usedGlyphs.ToList();
        for (int i = 0; i < entries.Count; i += 100)
        {
            int batch = Math.Min(100, entries.Count - i);
            cmapSb.Append($"{batch} beginbfchar\n");
            for (int j = 0; j < batch; j++)
            {
                var kv = entries[i + j];
                cmapSb.Append($"<{kv.Key:X4}> <{(int)kv.Value:X4}>\n");
            }
            cmapSb.Append("endbfchar\n");
        }
        cmapSb.Append("endcmap CMapName currentdict /CMap defineresource pop end end\n");
        int toUnicodeId = writeCompressedStream(Encoding.ASCII.GetBytes(cmapSb.ToString()), "");

        return writeObj(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{subsetName} /Encoding /Identity-H " +
            $"/DescendantFonts [{cidFontId} 0 R] /ToUnicode {toUnicodeId} 0 R >>");
    }

    private static bool CanEncodeWin1254(char ch) => ch < 0x100 && CharToWin1254[ch] != 0
        || ch is 'Ğ' or 'ğ' or 'İ' or 'ı' or 'Ş' or 'ş' or '€' or '‚' or '„' or '…' or '†' or '‡' or '‰' or '‹' or '‘' or '’' or '“' or '”' or '•' or '–' or '—' or '™' or '›';

    private static byte EncodeWin1254(char ch)
    {
        if (ch < 0x100)
        {
            byte b = CharToWin1254[ch];
            if (b != 0) return b;
        }
        return ch switch
        {
            'Ğ' => 0xD0,
            'ğ' => 0xF0,
            'İ' => 0xDD,
            'ı' => 0xFD,
            'Ş' => 0xDE,
            'ş' => 0xFE,
            '€' => 0x80,
            '‚' => 0x82,
            '„' => 0x84,
            '…' => 0x85,
            '†' => 0x86,
            '‡' => 0x87,
            '‰' => 0x89,
            '‹' => 0x8B,
            '‘' => 0x91,
            '’' => 0x92,
            '“' => 0x93,
            '”' => 0x94,
            '•' => 0x95,
            '–' => 0x96,
            '—' => 0x97,
            '™' => 0x99,
            '›' => 0x9B,
            _ => (byte)'?'
        };
    }

    private static byte[] BuildCharToWin1254()
    {
        var map = new byte[256];
        for (int c = 0x20; c <= 0x7E; c++) map[c] = (byte)c;
        for (int c = 0xA0; c <= 0xFF; c++)
        {
            if (c is 0xD0 or 0xDD or 0xDE or 0xF0 or 0xFD or 0xFE) continue;
            map[c] = (byte)c;
        }
        return map;
    }

    private static char[] BuildWin1254ToChar()
    {
        var map = new char[256];
        for (int c = 0x20; c <= 0x7E; c++) map[c] = (char)c;
        for (int c = 0xA0; c <= 0xFF; c++) map[c] = (char)c;
        map[0xD0] = 'Ğ'; map[0xF0] = 'ğ';
        map[0xDD] = 'İ'; map[0xFD] = 'ı';
        map[0xDE] = 'Ş'; map[0xFE] = 'ş';
        map[0x80] = '€'; map[0x82] = '‚'; map[0x84] = '„'; map[0x85] = '…';
        map[0x86] = '†'; map[0x87] = '‡'; map[0x89] = '‰'; map[0x8B] = '‹';
        map[0x91] = '‘'; map[0x92] = '’'; map[0x93] = '“'; map[0x94] = '”';
        map[0x95] = '•'; map[0x96] = '–'; map[0x97] = '—'; map[0x99] = '™'; map[0x9B] = '›';
        return map;
    }

    private static List<string> WrapLines(TtfFontInfo font, string text, double fs, double maxW, bool wordWrap)
    {
        var result = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            if (!wordWrap || font.MeasureWidth(raw, fs) <= maxW)
            {
                result.Add(raw);
                continue;
            }
            var words = raw.Split(' ');
            var cur = new StringBuilder();
            foreach (var w in words)
            {
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (font.MeasureWidth(cand, fs) <= maxW) { cur.Clear(); cur.Append(cand); }
                else { if (cur.Length > 0) result.Add(cur.ToString()); cur.Clear(); cur.Append(w); }
            }
            if (cur.Length > 0) result.Add(cur.ToString());
        }
        return result.Count > 0 ? result : new List<string> { "" };
    }

    private static (double R, double G, double B) ParseHexColor(string? hex, double dr, double dg, double db)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return (dr, dg, db);
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length >= 6 &&
            int.TryParse(hex[..2], NumberStyles.HexNumber, null, out int r) &&
            int.TryParse(hex[2..4], NumberStyles.HexNumber, null, out int g) &&
            int.TryParse(hex[4..6], NumberStyles.HexNumber, null, out int b))
            return (r / 255.0, g / 255.0, b / 255.0);
        return (dr, dg, db);
    }

    private static TtfFontInfo LoadTtfCached(params string[] fileNames)
    {
        var key = fileNames[0];
        return FontCache.GetOrAdd(key, _ =>
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var fn in fileNames)
            {
                var path = Path.Combine(fontsDir, fn);
                if (File.Exists(path))
                    return TtfFontInfo.Parse(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(fn));
            }
            throw new FileNotFoundException($"Sistem fontu bulunamadı: {string.Join(", ", fileNames)}");
        });
    }

    private sealed class TtfFontInfo
    {
        private static readonly HashSet<string> EssentialTables = new(StringComparer.Ordinal)
        {
            "OS/2", "cmap", "cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "post", "prep"
        };

        private readonly byte[] _raw;
        private readonly Dictionary<string, (int Off, int Len)> _tables;
        private readonly int _numGlyphs;
        private readonly short _indexToLocFormat;
        private readonly Dictionary<char, ushort> _cmap;
        private readonly ushort[] _advanceWidths;

        public string PdfName { get; }
        public int UnitsPerEm { get; }
        public int Ascent { get; }
        public int Descent { get; }

        private TtfFontInfo(byte[] raw, Dictionary<string, (int Off, int Len)> tables, int numGlyphs, short indexToLocFormat,
            string name, int upm, int asc, int desc, Dictionary<char, ushort> cmap, ushort[] widths)
        {
            _raw = raw; _tables = tables; _numGlyphs = numGlyphs; _indexToLocFormat = indexToLocFormat;
            PdfName = name; UnitsPerEm = upm; Ascent = asc; Descent = desc;
            _cmap = cmap; _advanceWidths = widths;
        }

        public ushort GetGlyphId(char ch) => _cmap.TryGetValue(ch, out var g) ? g : (ushort)0;

        public int GetGlyphWidth1000(ushort gid)
        {
            if (_advanceWidths.Length == 0) return 600;
            ushort w = gid < _advanceWidths.Length ? _advanceWidths[gid] : _advanceWidths[^1];
            return (int)Math.Round(w * 1000.0 / UnitsPerEm);
        }

        public double MeasureWidth(string text, double fontSizePt)
        {
            double sum = 0;
            for (int i = 0; i < text.Length; i++)
                sum += GetGlyphWidth1000(GetGlyphId(text[i]));
            return sum * fontSizePt / 1000.0;
        }

        /// <summary>
        /// Gerçek TrueType glyf/loca alt kümeleme: GID numaralarını koruyarak kullanılmayan glif konturlarını sıfırlar
        /// ve gereksiz OpenType tablolarını çıkarır. Bileşik gliflerin (Ş, Ğ, İ, Ç, Ö, Ü) alt bileşenlerini otomatik korur.
        /// </summary>
        public byte[] BuildSubset(IEnumerable<ushort> requestedGids)
        {
            if (!_tables.TryGetValue("glyf", out var glyfTab) || !_tables.TryGetValue("loca", out var locaTab))
                return _raw;

            // Read original loca offsets
            var oldOffsets = new int[_numGlyphs + 1];
            if (_indexToLocFormat == 0)
            {
                for (int i = 0; i <= _numGlyphs; i++)
                    oldOffsets[i] = U16(_raw, locaTab.Off + i * 2) * 2;
            }
            else
            {
                for (int i = 0; i <= _numGlyphs; i++)
                    oldOffsets[i] = (int)U32(_raw, locaTab.Off + i * 4);
            }

            // Expand used GIDs with composite glyph components (BFS)
            var used = new HashSet<ushort> { 0 }; // always keep .notdef
            var queue = new Queue<ushort>();
            foreach (var g in requestedGids)
                if (g < _numGlyphs && used.Add(g)) queue.Enqueue(g);

            while (queue.Count > 0)
            {
                ushort gid = queue.Dequeue();
                int start = oldOffsets[gid];
                int end = oldOffsets[gid + 1];
                if (end - start < 10) continue;
                int abs = glyfTab.Off + start;
                short contours = I16(_raw, abs);
                if (contours >= 0) continue; // simple glyph

                // Composite glyph: walk component records
                int pos = abs + 10;
                int limit = glyfTab.Off + end;
                while (pos + 4 <= limit)
                {
                    ushort flags = U16(_raw, pos);
                    ushort compGid = U16(_raw, pos + 2);
                    pos += 4;
                    if (compGid < _numGlyphs && used.Add(compGid))
                        queue.Enqueue(compGid);

                    pos += (flags & 0x0001) != 0 ? 4 : 2; // ARG_1_AND_2_ARE_WORDS
                    if ((flags & 0x0008) != 0) pos += 2;       // WE_HAVE_A_SCALE
                    else if ((flags & 0x0040) != 0) pos += 4;  // WE_HAVE_AN_X_AND_Y_SCALE
                    else if ((flags & 0x0080) != 0) pos += 8;  // WE_HAVE_A_TWO_BY_TWO
                    if ((flags & 0x0020) == 0) break;          // MORE_COMPONENTS
                }
            }

            // Rebuild glyf & loca keeping only used glyphs
            using var newGlyfMs = new MemoryStream(16384);
            var newOffsets = new int[_numGlyphs + 1];
            for (int gid = 0; gid < _numGlyphs; gid++)
            {
                newOffsets[gid] = (int)newGlyfMs.Position;
                if (used.Contains((ushort)gid))
                {
                    int start = oldOffsets[gid];
                    int len = oldOffsets[gid + 1] - start;
                    if (len > 0)
                    {
                        newGlyfMs.Write(_raw, glyfTab.Off + start, len);
                        int pad = (4 - (len & 3)) & 3;
                        for (int p = 0; p < pad; p++) newGlyfMs.WriteByte(0);
                    }
                }
            }
            newOffsets[_numGlyphs] = (int)newGlyfMs.Position;
            byte[] newGlyf = newGlyfMs.ToArray();

            byte[] newLoca;
            if (_indexToLocFormat == 0)
            {
                newLoca = new byte[(_numGlyphs + 1) * 2];
                for (int i = 0; i <= _numGlyphs; i++)
                    WriteU16(newLoca, i * 2, (ushort)(newOffsets[i] / 2));
            }
            else
            {
                newLoca = new byte[(_numGlyphs + 1) * 4];
                for (int i = 0; i <= _numGlyphs; i++)
                    WriteU32(newLoca, i * 4, (uint)newOffsets[i]);
            }

            // Prepare head table copy with checkSumAdjustment zeroed
            var headTab = _tables["head"];
            byte[] newHead = new byte[headTab.Len];
            Buffer.BlockCopy(_raw, headTab.Off, newHead, 0, headTab.Len);
            WriteU32(newHead, 8, 0);

            // Collect essential tables sorted by tag
            var outTables = new List<(string Tag, byte[] Data)>();
            foreach (var tag in EssentialTables.OrderBy(t => t, StringComparer.Ordinal))
            {
                if (tag == "glyf") outTables.Add((tag, newGlyf));
                else if (tag == "loca") outTables.Add((tag, newLoca));
                else if (tag == "head") outTables.Add((tag, newHead));
                else if (_tables.TryGetValue(tag, out var t))
                {
                    var copy = new byte[t.Len];
                    Buffer.BlockCopy(_raw, t.Off, copy, 0, t.Len);
                    outTables.Add((tag, copy));
                }
            }

            // Assemble SFNT binary
            int numOut = outTables.Count;
            int headerSize = 12 + numOut * 16;
            int totalSize = headerSize;
            foreach (var (_, d) in outTables)
                totalSize += (d.Length + 3) & ~3;

            byte[] sfnt = new byte[totalSize];
            // sfntVersion = 0x00010000
            WriteU32(sfnt, 0, 0x00010000);
            WriteU16(sfnt, 4, (ushort)numOut);
            int entrySelector = 0;
            while ((1 << (entrySelector + 1)) <= numOut) entrySelector++;
            int searchRange = (1 << entrySelector) * 16;
            WriteU16(sfnt, 6, (ushort)searchRange);
            WriteU16(sfnt, 8, (ushort)entrySelector);
            WriteU16(sfnt, 10, (ushort)(numOut * 16 - searchRange));

            int dataOffset = headerSize;
            int headOffsetInSfnt = -1;
            for (int i = 0; i < numOut; i++)
            {
                var (tag, d) = outTables[i];
                int rec = 12 + i * 16;
                sfnt[rec] = (byte)tag[0];
                sfnt[rec + 1] = (byte)tag[1];
                sfnt[rec + 2] = (byte)tag[2];
                sfnt[rec + 3] = (byte)tag[3];
                WriteU32(sfnt, rec + 4, ComputeChecksum(d));
                WriteU32(sfnt, rec + 8, (uint)dataOffset);
                WriteU32(sfnt, rec + 12, (uint)d.Length);

                if (tag == "head") headOffsetInSfnt = dataOffset;
                Buffer.BlockCopy(d, 0, sfnt, dataOffset, d.Length);
                dataOffset += (d.Length + 3) & ~3;
            }

            if (headOffsetInSfnt >= 0)
            {
                uint wholeChecksum = ComputeChecksum(sfnt);
                WriteU32(sfnt, headOffsetInSfnt + 8, 0xB1B0AFBAu - wholeChecksum);
            }
            return sfnt;
        }

        private static uint ComputeChecksum(byte[] data)
        {
            uint sum = 0;
            int n = data.Length;
            int i = 0;
            for (; i + 3 < n; i += 4)
                sum += (uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]);
            if (i < n)
            {
                uint tail = 0;
                int shift = 24;
                for (; i < n; i++, shift -= 8)
                    tail |= (uint)data[i] << shift;
                sum += tail;
            }
            return sum;
        }

        private static ushort U16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
        private static short I16(byte[] b, int o) => (short)((b[o] << 8) | b[o + 1]);
        private static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)(v & 0xFF); }
        private static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        public static TtfFontInfo Parse(byte[] data, string name)
        {
            int numTables = U16(data, 4);
            var tables = new Dictionary<string, (int Off, int Len)>(numTables);
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(data, rec, 4);
                tables[tag] = ((int)U32(data, rec + 8), (int)U32(data, rec + 12));
            }

            int headOff = tables["head"].Off;
            int upm = U16(data, headOff + 18);
            short indexToLocFormat = I16(data, headOff + 50);

            int maxpOff = tables["maxp"].Off;
            int numGlyphs = U16(data, maxpOff + 4);

            int hheaOff = tables["hhea"].Off;
            int asc = (int)Math.Round(I16(data, hheaOff + 4) * 1000.0 / upm);
            int desc = (int)Math.Round(I16(data, hheaOff + 6) * 1000.0 / upm);
            int numHMetrics = U16(data, hheaOff + 34);

            int hmtxOff = tables["hmtx"].Off;
            var widths = new ushort[numHMetrics];
            for (int i = 0; i < numHMetrics; i++)
                widths[i] = U16(data, hmtxOff + i * 4);

            var cmap = new Dictionary<char, ushort>(512);
            int cmapOff = tables["cmap"].Off;
            int subCount = U16(data, cmapOff + 2);
            int fmt4Off = -1;
            for (int i = 0; i < subCount; i++)
            {
                int sr = cmapOff + 4 + i * 8;
                ushort plat = U16(data, sr);
                ushort enc = U16(data, sr + 2);
                int subOff = cmapOff + (int)U32(data, sr + 4);
                if (U16(data, subOff) == 4 && ((plat == 3 && (enc == 1 || enc == 10)) || plat == 0))
                {
                    fmt4Off = subOff;
                    break;
                }
            }
            if (fmt4Off >= 0)
            {
                int segCount = U16(data, fmt4Off + 6) / 2;
                int endCodesOff = fmt4Off + 14;
                int startCodesOff = endCodesOff + segCount * 2 + 2;
                int idDeltaOff = startCodesOff + segCount * 2;
                int idRangeOff = idDeltaOff + segCount * 2;

                for (int s = 0; s < segCount; s++)
                {
                    int start = U16(data, startCodesOff + s * 2);
                    int end = U16(data, endCodesOff + s * 2);
                    short delta = I16(data, idDeltaOff + s * 2);
                    int rangeOffPos = idRangeOff + s * 2;
                    int rangeOff = U16(data, rangeOffPos);
                    if (start == 0xFFFF) break;

                    for (int ch = start; ch <= end; ch++)
                    {
                        ushort gid;
                        if (rangeOff == 0)
                            gid = (ushort)((ch + delta) & 0xFFFF);
                        else
                        {
                            int gPos = rangeOffPos + rangeOff + (ch - start) * 2;
                            gid = gPos + 1 < data.Length ? U16(data, gPos) : (ushort)0;
                            if (gid != 0) gid = (ushort)((gid + delta) & 0xFFFF);
                        }
                        if (gid != 0) cmap[(char)ch] = gid;
                    }
                }
            }

            return new TtfFontInfo(data, tables, numGlyphs, indexToLocFormat, name, upm, asc, desc, cmap, widths);
        }
    }
}
