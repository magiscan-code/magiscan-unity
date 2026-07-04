/*
 * QR Code generator (C#).
 *
 * Derived from the QR Code generator library by Project Nayuki (MIT License):
 *   https://www.nayuki.io/page/qr-code-generator-library
 *
 * Copyright (c) Project Nayuki. (MIT License)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of
 * this software and associated documentation files (the "Software"), to deal in
 * the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
 * the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *   - The above copyright notice and this permission notice shall be included in
 *     all copies or substantial portions of the Software.
 *   - The Software is provided "as is", without warranty of any kind.
 *
 * This is a trimmed, byte-mode-only port sufficient for encoding short link codes.
 * No Unity dependencies — kept pure C# so it can be unit-tested / validated in isolation.
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace Magiscan.Editor.Qr
{
    /// <summary>Error correction level for a QR Code.</summary>
    public enum QrEcc
    {
        Low = 0,
        Medium = 1,
        Quartile = 2,
        High = 3,
    }

    /// <summary>A square QR Code symbol. Build one with <see cref="EncodeText"/>.</summary>
    public sealed class QrCode
    {
        /// <summary>The width and height of this QR Code, in modules. Always between 21 and 177.</summary>
        public int Size { get; }

        /// <summary>The version number (1..40).</summary>
        public int Version { get; }

        /// <summary>The error correction level used.</summary>
        public QrEcc ErrorCorrectionLevel { get; }

        /// <summary>The data mask pattern applied (0..7).</summary>
        public int Mask { get; private set; }

        readonly bool[,] _modules;   // [y, x], true = dark
        bool[,] _isFunction;         // [y, x], true = reserved/function module

        const int PenaltyN1 = 3;
        const int PenaltyN2 = 3;
        const int PenaltyN3 = 40;
        const int PenaltyN4 = 10;

        // ECC codewords per block, indexed [ecc][version]. Index 0 of each row is unused.
        static readonly sbyte[][] EccCodewordsPerBlock =
        {
            new sbyte[]{-1, 7,10,15,20,26,18,20,24,30,18,20,24,26,30,22,24,28,30,28,28,28,28,30,30,26,28,30,30,30,30,30,30,30,30,30,30,30,30,30,30}, // Low
            new sbyte[]{-1,10,16,26,18,24,16,18,22,22,26,30,22,22,24,24,28,28,26,26,26,26,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28}, // Medium
            new sbyte[]{-1,13,22,18,26,18,24,18,22,20,24,28,26,24,20,30,24,28,28,26,30,28,30,30,30,30,28,30,30,30,30,30,30,30,30,30,30,30,30,30,30}, // Quartile
            new sbyte[]{-1,17,28,22,16,22,28,26,26,24,28,24,28,22,24,24,30,28,28,26,28,30,24,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30}, // High
        };

        // Number of error correction blocks, indexed [ecc][version]. Index 0 of each row is unused.
        static readonly sbyte[][] NumEccBlocks =
        {
            new sbyte[]{-1,1,1,1,1,1,2,2,2,2,4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9,10,12,12,12,13,14,15,16,17,18,19,19,20,21,22,24,25}, // Low
            new sbyte[]{-1,1,1,1,2,2,4,4,4,5,5, 5, 8, 9, 9,10,10,11,13,14,16,17,17,18,20,21,23,25,26,28,29,31,33,35,37,38,40,43,45,47,49}, // Medium
            new sbyte[]{-1,1,1,2,2,4,4,6,6,8,8, 8,10,12,16,12,17,16,18,21,20,23,23,25,27,29,34,34,35,38,40,43,45,48,51,53,56,59,62,65,68}, // Quartile
            new sbyte[]{-1,1,1,2,4,4,4,5,6,8,8,11,11,16,16,18,16,19,21,25,25,25,34,30,32,35,37,40,42,45,48,51,54,57,60,63,66,70,74,77,81}, // High
        };

        // ---- Public factory -------------------------------------------------

        /// <summary>Encodes a string as a QR Code using byte (UTF-8) mode.</summary>
        /// <param name="text">The text to encode.</param>
        /// <param name="ecc">Minimum error correction level (may be raised to fit the chosen version).</param>
        public static QrCode EncodeText(string text, QrEcc ecc = QrEcc.Medium)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return EncodeBytes(Encoding.UTF8.GetBytes(text), ecc);
        }

        /// <summary>Encodes raw bytes as a QR Code using byte mode.</summary>
        /// <param name="mask">Mask pattern 0..7, or -1 to choose automatically.</param>
        public static QrCode EncodeBytes(byte[] data, QrEcc ecc = QrEcc.Medium, int minVersion = 1, int maxVersion = 40, bool boostEcc = true, int mask = -1)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (minVersion < 1 || maxVersion > 40 || minVersion > maxVersion)
                throw new ArgumentOutOfRangeException(nameof(minVersion), "Invalid version range.");

            int version;
            int dataUsedBits;
            for (version = minVersion; ; version++)
            {
                int capacityBits = GetNumDataCodewords(version, ecc) * 8;
                dataUsedBits = ByteSegmentBitLength(version, data.Length);
                if (dataUsedBits <= capacityBits)
                    break;
                if (version >= maxVersion)
                    throw new ArgumentException($"Data too long ({data.Length} bytes) to fit version {maxVersion}.", nameof(data));
            }

            // Raise the error correction level for free if it still fits the chosen version.
            if (boostEcc)
            {
                foreach (QrEcc candidate in new[] { QrEcc.Medium, QrEcc.Quartile, QrEcc.High })
                {
                    if ((int)candidate > (int)ecc && dataUsedBits <= GetNumDataCodewords(version, candidate) * 8)
                        ecc = candidate;
                }
            }

            var bb = new BitBuffer();
            bb.AppendBits(0x4, 4);                                  // Byte mode indicator
            bb.AppendBits(data.Length, ByteCharCountBits(version)); // Character count
            foreach (byte b in data)
                bb.AppendBits(b, 8);

            int dataCapacityBits = GetNumDataCodewords(version, ecc) * 8;
            bb.AppendBits(0, Math.Min(4, dataCapacityBits - bb.Length)); // Terminator
            bb.AppendBits(0, (8 - bb.Length % 8) % 8);                    // Pad to byte boundary
            for (int pad = 0xEC; bb.Length < dataCapacityBits; pad ^= 0xEC ^ 0x11)
                bb.AppendBits(pad, 8);                                    // Alternating pad bytes

            return new QrCode(version, ecc, bb.ToBytes(), mask);
        }

        // ---- Construction ---------------------------------------------------

        QrCode(int version, QrEcc ecc, byte[] dataCodewords, int mask)
        {
            if (version < 1 || version > 40) throw new ArgumentOutOfRangeException(nameof(version));
            if (mask < -1 || mask > 7) throw new ArgumentOutOfRangeException(nameof(mask));
            Version = version;
            ErrorCorrectionLevel = ecc;
            Size = version * 4 + 17;
            _modules = new bool[Size, Size];
            _isFunction = new bool[Size, Size];

            DrawFunctionPatterns();
            byte[] allCodewords = AddEccAndInterleave(dataCodewords);
            DrawCodewords(allCodewords);

            // Pick the mask with the lowest penalty, unless one was requested.
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
                        mask = m;
                        minPenalty = penalty;
                    }
                    ApplyMask(m); // Undo
                }
            }

            ApplyMask(mask);
            DrawFormatBits(mask);
            Mask = mask;
            _isFunction = null;
        }

        /// <summary>Returns true if the module at (x, y) is dark.</summary>
        public bool GetModule(int x, int y)
        {
            return 0 <= x && x < Size && 0 <= y && y < Size && _modules[y, x];
        }

        // ---- Drawing --------------------------------------------------------

        void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunctionModule(6, i, i % 2 == 0);
                SetFunctionModule(i, 6, i % 2 == 0);
            }

            DrawFinderPattern(3, 3);
            DrawFinderPattern(Size - 4, 3);
            DrawFinderPattern(3, Size - 4);

            int[] alignPatPos = GetAlignmentPatternPositions();
            int numAlign = alignPatPos.Length;
            for (int i = 0; i < numAlign; i++)
            {
                for (int j = 0; j < numAlign; j++)
                {
                    if (!(i == 0 && j == 0 || i == 0 && j == numAlign - 1 || i == numAlign - 1 && j == 0))
                        DrawAlignmentPattern(alignPatPos[i], alignPatPos[j]);
                }
            }

            DrawFormatBits(0); // Dummy; overwritten once the mask is known.
            DrawVersion();
        }

        void DrawFormatBits(int mask)
        {
            int data = FormatBits(ErrorCorrectionLevel) << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
                SetFunctionModule(8, i, GetBit(bits, i));
            SetFunctionModule(8, 7, GetBit(bits, 6));
            SetFunctionModule(8, 8, GetBit(bits, 7));
            SetFunctionModule(7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++)
                SetFunctionModule(14 - i, 8, GetBit(bits, i));

            for (int i = 0; i < 8; i++)
                SetFunctionModule(Size - 1 - i, 8, GetBit(bits, i));
            for (int i = 8; i < 15; i++)
                SetFunctionModule(8, Size - 15 + i, GetBit(bits, i));
            SetFunctionModule(8, Size - 8, true);
        }

        void DrawVersion()
        {
            if (Version < 7) return;

            int rem = Version;
            for (int i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;

            for (int i = 0; i < 18; i++)
            {
                bool bit = GetBit(bits, i);
                int a = Size - 11 + i % 3;
                int b = i / 3;
                SetFunctionModule(a, b, bit);
                SetFunctionModule(b, a, bit);
            }
        }

        void DrawFinderPattern(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (0 <= xx && xx < Size && 0 <= yy && yy < Size)
                        SetFunctionModule(xx, yy, dist != 2 && dist != 4);
                }
            }
        }

        void DrawAlignmentPattern(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        void SetFunctionModule(int x, int y, bool isDark)
        {
            _modules[y, x] = isDark;
            _isFunction[y, x] = true;
        }

        // ---- Error correction -----------------------------------------------

        byte[] AddEccAndInterleave(byte[] data)
        {
            int ecc = (int)ErrorCorrectionLevel;
            int numBlocks = NumEccBlocks[ecc][Version];
            int blockEccLen = EccCodewordsPerBlock[ecc][Version];
            int rawCodewords = GetNumRawDataModules(Version) / 8;
            int numShortBlocks = numBlocks - rawCodewords % numBlocks;
            int shortBlockLen = rawCodewords / numBlocks;

            var blocks = new byte[numBlocks][];
            byte[] rsDiv = ReedSolomonComputeDivisor(blockEccLen);
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                var dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;

                var block = new byte[shortBlockLen + 1];
                Array.Copy(dat, 0, block, 0, datLen);
                byte[] eccBytes = ReedSolomonComputeRemainder(dat, rsDiv);
                Array.Copy(eccBytes, 0, block, block.Length - blockEccLen, eccBytes.Length);
                blocks[i] = block;
            }

            var result = new byte[rawCodewords];
            for (int i = 0, k = 0; i < blocks[0].Length; i++)
            {
                for (int j = 0; j < blocks.Length; j++)
                {
                    // Skip the padding byte present only in short blocks.
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    {
                        result[k] = blocks[j][i];
                        k++;
                    }
                }
            }
            return result;
        }

        void DrawCodewords(byte[] data)
        {
            int i = 0;
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
                        if (!_isFunction[y, x] && i < data.Length * 8)
                        {
                            _modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        case 7: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        default: throw new ArgumentOutOfRangeException(nameof(mask));
                    }
                    if (!_isFunction[y, x] && invert)
                        _modules[y, x] ^= true;
                }
            }
        }

        int GetPenaltyScore()
        {
            int result = 0;

            // Rows: adjacent same-color runs and finder-like patterns.
            for (int y = 0; y < Size; y++)
            {
                bool runColor = false;
                int runX = 0;
                var runHistory = new int[7];
                for (int x = 0; x < Size; x++)
                {
                    if (_modules[y, x] == runColor)
                    {
                        runX++;
                        if (runX == 5) result += PenaltyN1;
                        else if (runX > 5) result++;
                    }
                    else
                    {
                        FinderPenaltyAddHistory(runX, runHistory);
                        if (!runColor) result += FinderPenaltyCountPatterns(runHistory) * PenaltyN3;
                        runColor = _modules[y, x];
                        runX = 1;
                    }
                }
                result += FinderPenaltyTerminateAndCount(runColor, runX, runHistory) * PenaltyN3;
            }

            // Columns.
            for (int x = 0; x < Size; x++)
            {
                bool runColor = false;
                int runY = 0;
                var runHistory = new int[7];
                for (int y = 0; y < Size; y++)
                {
                    if (_modules[y, x] == runColor)
                    {
                        runY++;
                        if (runY == 5) result += PenaltyN1;
                        else if (runY > 5) result++;
                    }
                    else
                    {
                        FinderPenaltyAddHistory(runY, runHistory);
                        if (!runColor) result += FinderPenaltyCountPatterns(runHistory) * PenaltyN3;
                        runColor = _modules[y, x];
                        runY = 1;
                    }
                }
                result += FinderPenaltyTerminateAndCount(runColor, runY, runHistory) * PenaltyN3;
            }

            // 2x2 blocks of the same color.
            for (int y = 0; y < Size - 1; y++)
            {
                for (int x = 0; x < Size - 1; x++)
                {
                    bool color = _modules[y, x];
                    if (color == _modules[y, x + 1] && color == _modules[y + 1, x] && color == _modules[y + 1, x + 1])
                        result += PenaltyN2;
                }
            }

            // Balance of dark vs light modules.
            int dark = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (_modules[y, x]) dark++;
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            result += k * PenaltyN4;

            return result;
        }

        int FinderPenaltyCountPatterns(int[] runHistory)
        {
            int n = runHistory[1];
            bool core = n > 0 && runHistory[2] == n && runHistory[3] == n * 3 && runHistory[4] == n && runHistory[5] == n;
            return (core && runHistory[0] >= n * 4 && runHistory[6] >= n ? 1 : 0)
                 + (core && runHistory[6] >= n * 4 && runHistory[0] >= n ? 1 : 0);
        }

        int FinderPenaltyTerminateAndCount(bool currentRunColor, int currentRunLength, int[] runHistory)
        {
            if (currentRunColor)
            {
                FinderPenaltyAddHistory(currentRunLength, runHistory);
                currentRunLength = 0;
            }
            currentRunLength += Size;
            FinderPenaltyAddHistory(currentRunLength, runHistory);
            return FinderPenaltyCountPatterns(runHistory);
        }

        void FinderPenaltyAddHistory(int currentRunLength, int[] runHistory)
        {
            if (runHistory[0] == 0)
                currentRunLength += Size;
            Array.Copy(runHistory, 0, runHistory, 1, runHistory.Length - 1);
            runHistory[0] = currentRunLength;
        }

        // ---- Reed-Solomon ---------------------------------------------------

        static byte[] ReedSolomonComputeDivisor(int degree)
        {
            if (degree < 1 || degree > 255) throw new ArgumentOutOfRangeException(nameof(degree));
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)ReedSolomonMultiply(result[j] & 0xFF, root);
                    if (j + 1 < degree)
                        result[j] ^= result[j + 1];
                }
                root = ReedSolomonMultiply(root, 0x02);
            }
            return result;
        }

        static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = (b ^ result[0]) & 0xFF;
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++)
                    result[i] ^= (byte)ReedSolomonMultiply(divisor[i] & 0xFF, factor);
            }
            return result;
        }

        static int ReedSolomonMultiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        // ---- Capacity helpers -----------------------------------------------

        static int GetNumRawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int numAlign = version / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (version >= 7)
                    result -= 36;
            }
            return result;
        }

        static int GetNumDataCodewords(int version, QrEcc ecc)
        {
            int e = (int)ecc;
            return GetNumRawDataModules(version) / 8
                 - EccCodewordsPerBlock[e][version] * NumEccBlocks[e][version];
        }

        int[] GetAlignmentPatternPositions()
        {
            if (Version == 1)
                return Array.Empty<int>();

            int numAlign = Version / 7 + 2;
            int step = (Version == 32)
                ? 26
                : (Version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;

            var result = new int[numAlign];
            result[0] = 6;
            for (int i = result.Length - 1, pos = Size - 7; i >= 1; i--, pos -= step)
                result[i] = pos;
            return result;
        }

        static int ByteCharCountBits(int version) => version <= 9 ? 8 : 16;

        static int ByteSegmentBitLength(int version, int byteCount) => 4 + ByteCharCountBits(version) + 8 * byteCount;

        static int FormatBits(QrEcc ecc)
        {
            switch (ecc)
            {
                case QrEcc.Low: return 1;
                case QrEcc.Medium: return 0;
                case QrEcc.Quartile: return 3;
                case QrEcc.High: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(ecc));
            }
        }

        static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;

        // ---- Bit buffer -----------------------------------------------------

        sealed class BitBuffer
        {
            readonly List<bool> _bits = new List<bool>();

            public int Length => _bits.Count;

            public void AppendBits(int value, int count)
            {
                if (count < 0 || count > 31 || (count < 31 && (value >> count) != 0))
                    throw new ArgumentOutOfRangeException(nameof(value), "Value out of range for bit count.");
                for (int i = count - 1; i >= 0; i--)
                    _bits.Add(((value >> i) & 1) != 0);
            }

            public byte[] ToBytes()
            {
                var result = new byte[(_bits.Count + 7) / 8];
                for (int i = 0; i < _bits.Count; i++)
                    if (_bits[i])
                        result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                return result;
            }
        }
    }
}
