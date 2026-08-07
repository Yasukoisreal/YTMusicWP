using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.UI.Xaml.Media.Imaging;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Lightweight, self-contained QR Code generator for Windows Phone 8.1 (WinRT).
    /// Generates standard ISO/IEC 18004 QR codes directly to WriteableBitmap with 0 external dependencies.
    /// </summary>
    public static class QrCodeGenerator
    {
        public enum EccLevel { Low = 0, Medium = 1, Quartile = 2, High = 3 }

        /// <summary>
        /// Generates a WriteableBitmap containing the QR code for the given text.
        /// </summary>
        /// <param name="content">Text to encode (e.g. login URL)</param>
        /// <param name="scale">Pixel size for each QR module (default 4)</param>
        /// <param name="border">Quiet zone border size in modules (default 3)</param>
        /// <returns>WriteableBitmap or null on failure</returns>
        public static WriteableBitmap GenerateQrBitmap(string content, int scale = 4, int border = 3)
        {
            if (string.IsNullOrEmpty(content)) return null;

            try
            {
                var qr = QrCode.EncodeText(content, QrCode.Ecc.Medium);
                int size = qr.Size;
                int totalSize = (size + border * 2) * scale;

                var wb = new WriteableBitmap(totalSize, totalSize);
                byte[] pixels = new byte[totalSize * totalSize * 4];

                // Initialize entire image to White (BGRA: 255, 255, 255, 255)
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;     // B
                    pixels[i + 1] = 255; // G
                    pixels[i + 2] = 255; // R
                    pixels[i + 3] = 255; // A
                }

                // Draw black modules
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (qr.GetModule(x, y))
                        {
                            int startX = (x + border) * scale;
                            int startY = (y + border) * scale;

                            for (int py = 0; py < scale; py++)
                            {
                                int rowOffset = (startY + py) * totalSize * 4;
                                for (int px = 0; px < scale; px++)
                                {
                                    int pixelIndex = rowOffset + (startX + px) * 4;
                                    pixels[pixelIndex] = 0;     // B
                                    pixels[pixelIndex + 1] = 0; // G
                                    pixels[pixelIndex + 2] = 0; // R
                                    // Alpha stays 255
                                }
                            }
                        }
                    }
                }

                // Copy buffer into WriteableBitmap pixel buffer
                using (Stream stream = wb.PixelBuffer.AsStream())
                {
                    stream.Write(pixels, 0, pixels.Length);
                }
                wb.Invalidate();

                return wb;
            }
            catch
            {
                return null;
            }
        }

        /*
         * QR Code generator core
         * Fully compliant with ISO/IEC 18004
         */
        private sealed class QrCode
        {
            public sealed class Ecc
            {
                public static readonly Ecc Low = new Ecc(0, 1);
                public static readonly Ecc Medium = new Ecc(1, 0);
                public static readonly Ecc Quartile = new Ecc(2, 3);
                public static readonly Ecc High = new Ecc(3, 2);

                public int Ordinal { get; }
                public int FormatBits { get; }

                private Ecc(int ordinal, int formatBits)
                {
                    Ordinal = ordinal;
                    FormatBits = formatBits;
                }
            }

            public int Version { get; }
            public int Size { get; }
            public Ecc ErrorCorrectionLevel { get; }
            public int Mask { get; }

            private readonly bool[] _modules;
            private readonly bool[] _isFunction;

            public static QrCode EncodeText(string text, Ecc ecc)
            {
                byte[] textBytes = Encoding.UTF8.GetBytes(text);
                var seg = QrSegment.MakeBytes(textBytes);
                return EncodeSegments(new[] { seg }, ecc);
            }

            public static QrCode EncodeSegments(IList<QrSegment> segs, Ecc ecc, int minVersion = 1, int maxVersion = 40, int mask = -1, bool boostEcc = true)
            {
                if (segs == null) throw new ArgumentNullException("segs");
                if (minVersion < 1 || minVersion > maxVersion || maxVersion > 40)
                    throw new ArgumentOutOfRangeException("Invalid version range");

                int version, dataUsedBits;
                for (version = minVersion; ; version++)
                {
                    int dataCapacityBits = GetNumDataCodewords(version, ecc) * 8;
                    dataUsedBits = QrSegment.GetTotalBits(segs, version);
                    if (dataUsedBits != -1 && dataUsedBits <= dataCapacityBits)
                        break;
                    if (version >= maxVersion)
                        throw new ArgumentException("Data too long for QR Code");
                }

                if (boostEcc)
                {
                    foreach (Ecc newEcc in new[] { Ecc.Medium, Ecc.Quartile, Ecc.High })
                    {
                        if (dataUsedBits <= GetNumDataCodewords(version, newEcc) * 8)
                            ecc = newEcc;
                    }
                }

                var bb = new BitBuffer();
                foreach (var seg in segs)
                {
                    bb.AppendBits((int)seg.Mode, 4);
                    bb.AppendBits(seg.NumChars, seg.GetCountLength(version));
                    bb.AppendData(seg.Data);
                }

                int capacityBits = GetNumDataCodewords(version, ecc) * 8;
                bb.AppendBits(0, Math.Min(4, capacityBits - bb.BitLength));
                bb.AppendBits(0, (8 - (bb.BitLength % 8)) % 8);

                for (byte padByte = 0xEC; bb.BitLength < capacityBits; padByte = (byte)(padByte == 0xEC ? 0x11 : 0xEC))
                    bb.AppendBits(padByte, 8);

                byte[] dataCodewords = new byte[bb.BitLength / 8];
                for (int i = 0; i < dataCodewords.Length; i++)
                    dataCodewords[i] = (byte)bb.ReadBits(i * 8, 8);

                return new QrCode(version, ecc, dataCodewords, mask);
            }

            private QrCode(int version, Ecc ecc, byte[] dataCodewords, int mask)
            {
                Version = version;
                Size = version * 4 + 17;
                ErrorCorrectionLevel = ecc;

                _modules = new bool[Size * Size];
                _isFunction = new bool[Size * Size];

                DrawFunctionPatterns();
                byte[] allCodewords = AddEccAndInterleave(dataCodewords);
                DrawCodewords(allCodewords);

                if (mask == -1)
                {
                    int minPenalty = int.MaxValue;
                    for (int m = 0; m < 8; m++)
                    {
                        ApplyMask(m);
                        DrawFormatBits(m);
                        int penalty = GetPenaltyScore();
                        if (penalty < minPenalty)
                        {
                            minPenalty = penalty;
                            mask = m;
                        }
                        ApplyMask(m); // Undoes mask
                    }
                }

                Mask = mask;
                ApplyMask(mask);
                DrawFormatBits(mask);
            }

            public bool GetModule(int x, int y)
            {
                if (x >= 0 && x < Size && y >= 0 && y < Size)
                    return _modules[y * Size + x];
                return false;
            }

            private void SetFunctionModule(int x, int y, bool isDark)
            {
                _modules[y * Size + x] = isDark;
                _isFunction[y * Size + x] = true;
            }

            private void DrawFunctionPatterns()
            {
                // Draw horizontal and vertical timing patterns
                for (int i = 0; i < Size; i++)
                {
                    SetFunctionModule(6, i, i % 2 == 0);
                    SetFunctionModule(i, 6, i % 2 == 0);
                }

                // Draw 3 finder patterns
                DrawFinderPattern(3, 3);
                DrawFinderPattern(Size - 4, 3);
                DrawFinderPattern(3, Size - 4);

                // Draw alignment patterns
                int[] alignPositions = GetAlignmentPatternPositions(Version);
                int numAlign = alignPositions.Length;
                for (int i = 0; i < numAlign; i++)
                {
                    for (int j = 0; j < numAlign; j++)
                    {
                        if ((i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0))
                            continue;
                        DrawAlignmentPattern(alignPositions[i], alignPositions[j]);
                    }
                }

                // Draw configuration info dummy to reserve function modules
                DrawFormatBits(0);
                DrawVersion();
            }

            private void DrawFormatBits(int mask)
            {
                int data = (int)ErrorCorrectionLevel.FormatBits << 3 | mask;
                int rem = data;
                for (int i = 0; i < 10; i++)
                    rem = (rem << 1) ^ ((rem >> 9) * 0x537);
                int bits = ((data << 10) | rem) ^ 0x5412;

                // Draw first copy
                for (int i = 0; i <= 5; i++) SetFunctionModule(8, i, ((bits >> i) & 1) != 0);
                SetFunctionModule(8, 7, ((bits >> 6) & 1) != 0);
                SetFunctionModule(8, 8, ((bits >> 7) & 1) != 0);
                SetFunctionModule(7, 8, ((bits >> 8) & 1) != 0);
                for (int i = 9; i < 15; i++) SetFunctionModule(14 - i, 8, ((bits >> i) & 1) != 0);

                // Draw second copy
                for (int i = 0; i < 8; i++) SetFunctionModule(Size - 1 - i, 8, ((bits >> i) & 1) != 0);
                for (int i = 8; i < 15; i++) SetFunctionModule(8, Size - 15 + i, ((bits >> i) & 1) != 0);
                SetFunctionModule(8, Size - 8, true);
            }

            private void DrawVersion()
            {
                if (Version < 7) return;

                int rem = Version;
                for (int i = 0; i < 12; i++)
                    rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = (Version << 12) | rem;

                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = Size - 11 + i % 3;
                    int b = i / 3;
                    SetFunctionModule(a, b, bit);
                    SetFunctionModule(b, a, bit);
                }
            }

            private void DrawFinderPattern(int x, int y)
            {
                for (int dy = -4; dy <= 4; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        int px = x + dx;
                        int py = y + dy;
                        if (px >= 0 && px < Size && py >= 0 && py < Size)
                        {
                            SetFunctionModule(px, py, dist != 2 && dist != 4);
                        }
                    }
                }
            }

            private void DrawAlignmentPattern(int x, int y)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }

            private void DrawCodewords(byte[] allCodewords)
            {
                int bitIndex = 0;
                for (int right = Size - 1; right >= 1; right -= 2)
                {
                    if (right == 6) right = 5;
                    for (int vert = 0; vert < Size; vert++)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            bool upward = ((right + 1) & 2) == 0;
                            int y = upward ? Size - 1 - vert : vert;
                            if (!_isFunction[y * Size + x] && bitIndex < allCodewords.Length * 8)
                            {
                                bool dark = ((allCodewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                                _modules[y * Size + x] = dark;
                                bitIndex++;
                            }
                        }
                    }
                }
            }

            private void ApplyMask(int mask)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        if (_isFunction[y * Size + x]) continue;
                        bool invert;
                        switch (mask)
                        {
                            case 0: invert = (x + y) % 2 == 0; break;
                            case 1: invert = y % 2 == 0; break;
                            case 2: invert = x % 3 == 0; break;
                            case 3: invert = (x + y) % 3 == 0; break;
                            case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: invert = (x * y) % 2 + (x * y) % 3 == 0; break;
                            case 6: invert = ((x * y) % 2 + (x * y) % 3) % 2 == 0; break;
                            case 7: invert = ((x + y) % 2 + (x * y) % 3) % 2 == 0; break;
                            default: throw new ArgumentOutOfRangeException();
                        }
                        if (invert) _modules[y * Size + x] = !_modules[y * Size + x];
                    }
                }
            }

            private int GetPenaltyScore()
            {
                int penalty = 0;
                for (int y = 0; y < Size; y++)
                {
                    bool runColor = false;
                    int runLen = 0;
                    for (int x = 0; x < Size; x++)
                    {
                        if (x == 0 || _modules[y * Size + x] != runColor)
                        {
                            runColor = _modules[y * Size + x];
                            runLen = 1;
                        }
                        else
                        {
                            runLen++;
                            if (runLen == 5) penalty += 3;
                            else if (runLen > 5) penalty++;
                        }
                    }
                }
                for (int x = 0; x < Size; x++)
                {
                    bool runColor = false;
                    int runLen = 0;
                    for (int y = 0; y < Size; y++)
                    {
                        if (y == 0 || _modules[y * Size + x] != runColor)
                        {
                            runColor = _modules[y * Size + x];
                            runLen = 1;
                        }
                        else
                        {
                            runLen++;
                            if (runLen == 5) penalty += 3;
                            else if (runLen > 5) penalty++;
                        }
                    }
                }

                for (int y = 0; y < Size - 1; y++)
                {
                    for (int x = 0; x < Size - 1; x++)
                    {
                        bool c = _modules[y * Size + x];
                        if (c == _modules[y * Size + (x + 1)] &&
                            c == _modules[(y + 1) * Size + x] &&
                            c == _modules[(y + 1) * Size + (x + 1)])
                            penalty += 3;
                    }
                }

                int total = Size * Size;
                int darkCount = 0;
                for (int i = 0; i < total; i++)
                    if (_modules[i]) darkCount++;
                int k = (int)Math.Abs(darkCount * 20L - total * 10L) / total - 1;
                penalty += k * 10;

                return penalty;
            }

            private byte[] AddEccAndInterleave(byte[] data)
            {
                int numBlocks = _eccBlocks[(int)ErrorCorrectionLevel.Ordinal, Version];
                int blockEccLen = _eccCodewordsPerBlock[(int)ErrorCorrectionLevel.Ordinal, Version];
                int totalDataCodewords = GetNumDataCodewords(Version, ErrorCorrectionLevel);
                int numShortBlocks = numBlocks - totalDataCodewords % numBlocks;
                int shortBlockDataLen = totalDataCodewords / numBlocks;

                byte[][] dataBlocks = new byte[numBlocks][];
                byte[][] eccBlocks = new byte[numBlocks][];
                var rs = new ReedSolomonGenerator(blockEccLen);

                int dataIndex = 0;
                for (int i = 0; i < numBlocks; i++)
                {
                    int blockLen = shortBlockDataLen + (i >= numShortBlocks ? 1 : 0);
                    byte[] block = new byte[blockLen];
                    Array.Copy(data, dataIndex, block, 0, blockLen);
                    dataIndex += blockLen;
                    dataBlocks[i] = block;
                    eccBlocks[i] = rs.GetRemainder(block);
                }

                byte[] result = new byte[_numDataCodewords[Version]];
                int resIndex = 0;
                for (int i = 0; i <= shortBlockDataLen; i++)
                {
                    for (int j = 0; j < numBlocks; j++)
                    {
                        if (i < dataBlocks[j].Length)
                            result[resIndex++] = dataBlocks[j][i];
                    }
                }
                for (int i = 0; i < blockEccLen; i++)
                {
                    for (int j = 0; j < numBlocks; j++)
                        result[resIndex++] = eccBlocks[j][i];
                }
                return result;
            }

            private static int GetNumDataCodewords(int version, Ecc ecc)
            {
                return _numDataCodewords[version] - _eccBlocks[(int)ecc.Ordinal, version] * _eccCodewordsPerBlock[(int)ecc.Ordinal, version];
            }

            private static int[] GetAlignmentPatternPositions(int version)
            {
                return _alignmentPatternPositions[version];
            }

            #region Tables
            private static readonly int[] _numDataCodewords = { 0,
                26, 44, 70, 100, 134, 172, 196, 242, 292, 346,
                404, 466, 532, 581, 655, 733, 815, 901, 991, 1085,
                1156, 1258, 1364, 1474, 1588, 1706, 1828, 1921, 2051, 2185,
                2323, 2465, 2611, 2761, 2876, 3034, 3196, 3362, 3532, 3706
            };

            private static readonly int[,] _eccBlocks = {
                { 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
                { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
                { 0, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
                { 0, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }
            };

            private static readonly int[,] _eccCodewordsPerBlock = {
                { 0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
                { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
                { 0, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
                { 0, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }
            };

            private static readonly int[][] _alignmentPatternPositions = {
                new int[] { },
                new int[] { },
                new[] { 6, 18 },
                new[] { 6, 22 },
                new[] { 6, 26 },
                new[] { 6, 30 },
                new[] { 6, 34 },
                new[] { 6, 22, 38 },
                new[] { 6, 24, 42 },
                new[] { 6, 26, 46 },
                new[] { 6, 28, 50 },
                new[] { 6, 30, 54 },
                new[] { 6, 32, 58 },
                new[] { 6, 34, 62 },
                new[] { 6, 26, 46, 66 },
                new[] { 6, 26, 48, 70 },
                new[] { 6, 26, 50, 74 },
                new[] { 6, 30, 54, 78 },
                new[] { 6, 30, 56, 82 },
                new[] { 6, 30, 58, 86 },
                new[] { 6, 34, 62, 90 },
                new[] { 6, 28, 50, 72, 94 },
                new[] { 6, 26, 50, 74, 98 },
                new[] { 6, 30, 54, 78, 102 },
                new[] { 6, 28, 54, 80, 106 },
                new[] { 6, 32, 58, 84, 110 },
                new[] { 6, 30, 58, 86, 114 },
                new[] { 6, 34, 62, 90, 118 },
                new[] { 6, 26, 50, 74, 98, 122 },
                new[] { 6, 30, 54, 78, 102, 126 },
                new[] { 6, 26, 52, 78, 104, 130 },
                new[] { 6, 30, 56, 82, 108, 134 },
                new[] { 6, 34, 60, 86, 112, 138 },
                new[] { 6, 30, 58, 86, 114, 142 },
                new[] { 6, 34, 62, 90, 118, 146 },
                new[] { 6, 30, 54, 78, 102, 126, 150 },
                new[] { 6, 24, 50, 76, 102, 128, 154 },
                new[] { 6, 28, 54, 80, 106, 132, 158 },
                new[] { 6, 32, 58, 84, 110, 136, 162 },
                new[] { 6, 26, 54, 82, 110, 138, 166 },
                new[] { 6, 30, 58, 86, 114, 142, 170 }
            };
            #endregion
        }

        private sealed class QrSegment
        {
            public enum ModeType { Byte = 4 }
            public ModeType Mode { get; }
            public int NumChars { get; }
            public BitBuffer Data { get; }

            public QrSegment(ModeType mode, int numChars, BitBuffer data)
            {
                Mode = mode;
                NumChars = numChars;
                Data = data;
            }

            public static QrSegment MakeBytes(byte[] data)
            {
                var bb = new BitBuffer();
                foreach (byte b in data) bb.AppendBits(b, 8);
                return new QrSegment(ModeType.Byte, data.Length, bb);
            }

            public int GetCountLength(int version)
            {
                return version <= 9 ? 8 : 16;
            }

            public static int GetTotalBits(IList<QrSegment> segs, int version)
            {
                int result = 0;
                foreach (var seg in segs)
                {
                    int ccbits = seg.GetCountLength(version);
                    if (seg.NumChars >= (1 << ccbits)) return -1;
                    result += 4 + ccbits + seg.Data.BitLength;
                }
                return result;
            }
        }

        private sealed class BitBuffer
        {
            private readonly List<bool> _data = new List<bool>();
            public int BitLength { get { return _data.Count; } }

            public void AppendBits(int val, int len)
            {
                for (int i = len - 1; i >= 0; i--)
                    _data.Add(((val >> i) & 1) != 0);
            }

            public void AppendData(BitBuffer other)
            {
                _data.AddRange(other._data);
            }

            public int ReadBits(int start, int len)
            {
                int val = 0;
                for (int i = 0; i < len; i++)
                    val = (val << 1) | (_data[start + i] ? 1 : 0);
                return val;
            }
        }

        private sealed class ReedSolomonGenerator
        {
            private readonly byte[] _coefficients;

            public ReedSolomonGenerator(int degree)
            {
                _coefficients = new byte[degree];
                _coefficients[degree - 1] = 1;
                int root = 1;
                for (int i = 0; i < degree; i++)
                {
                    for (int j = 0; j < degree; j++)
                    {
                        _coefficients[j] = Multiply(_coefficients[j], root);
                        if (j + 1 < degree)
                            _coefficients[j] ^= _coefficients[j + 1];
                    }
                    root = Multiply(root, 0x02);
                }
            }

            public byte[] GetRemainder(byte[] data)
            {
                byte[] result = new byte[_coefficients.Length];
                foreach (byte b in data)
                {
                    byte factor = (byte)(b ^ result[0]);
                    Array.Copy(result, 1, result, 0, result.Length - 1);
                    result[result.Length - 1] = 0;
                    for (int i = 0; i < result.Length; i++)
                        result[i] ^= Multiply(_coefficients[i], factor);
                }
                return result;
            }

            private static byte Multiply(int x, int y)
            {
                int z = 0;
                for (int i = 7; i >= 0; i--)
                {
                    z = (z << 1) ^ ((z >> 7) * 0x11D);
                    if (((y >> i) & 1) != 0) z ^= x;
                }
                return (byte)z;
            }
        }
    }
}
