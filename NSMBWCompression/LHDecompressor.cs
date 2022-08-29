using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NSMBWCompression
{
    // Stolen from https://github.com/aboood40091/LHDecompressor/blob/e3de4eac048de566827dd29c55e74cc77d385302/LHDecompressor.cpp

    public static class LHMisc
    {
        // Buffer where the length && offset huffman tables will be stored
        public static unsafe ushort[] WorkBuffer = new ushort[1024 + 64];

        public static uint Swap16(ushort x)
        {
            return (uint)((x << 8 | x >> 8) & 0xFFFF);
        }

        public static uint Swap32(uint x)
        {
            return (uint)(x << 24 |
                  (x & 0xFF00) << 8 |
                   x >> 24 |
                   x >> 8 & 0xFF00);
        }
    }

    public class BitReader
    {
        public int readS32(byte nBits)
        {
            while (bitStreamLen < nBits)
            {
                if (srcCount == 0)
                    return -1;

                bitStream <<= 8;
                bitStream += srcp[srcpIdx];
                srcpIdx += 1;
                srcCount -= 1;
                bitStreamLen += 8;
            }

            int ret = (int)(bitStream >> (int)bitStreamLen - nBits & (1 << nBits) - 1);
            bitStreamLen -= nBits;

            return ret;
        }

        public long readS64(byte nBits)
        {
            byte overflow = 0;

            while (bitStreamLen < nBits)
            {
                if (srcCount == 0)
                    return -1;

                if (bitStreamLen > 24)
                    overflow = (byte)(bitStream >> 24);

                bitStream <<= 8;
                bitStream += srcp[srcpIdx];
                srcpIdx += 1;
                srcCount -= 1;
                bitStreamLen += 8;
            }

            long ret = bitStream;
            ret |= (long)overflow << 32;
            ret = (long)(ret >> (int)bitStreamLen - nBits & ((long)1 << nBits) - 1);
            bitStreamLen -= nBits;

            return ret;
        }

        public byte[] srcp;
        public int srcpIdx = 0;
        public uint srcCount;
        public uint bitStream;
        public uint bitStreamLen;
    }

    public static class LHDecompressor
    {
        public static uint getDecompSize(byte[] src)
        {
            // Assumes little-endian host
            /*uint size = src.GetUInt32(0) >> 8;
            if (size == 0)
                size = src.GetUInt32(4);*/

            uint size = (uint)((src[3] << 16) | (src[2] << 8) | src[1]);
            if (size == 0)
                size = (uint)((src[7] << 16) | (src[6] << 8) | src[5]);

            return size;
        }

        public unsafe static int decomp(ref byte[] dst, byte[] src)
        {
            uint srcSize = (uint)src.Length;
            fixed (ushort* WorkBuffer = LHMisc.WorkBuffer)
            {
                int dstIdx = 0;
                int bits32, destCount, huffLen;
                long bits64;
                uint sizeAndMagic, currentIdx, offset;
                ushort* lengthHuffTbl;
                ushort* offsetHuffTbl;
                ushort* currentNode;
                ushort copyLen, n, lzOffset;
                byte shift;
                sbyte nOffsetBits;

                BitReader reader = new BitReader();
                reader.srcp = src;
                reader.srcCount = srcSize;
                reader.bitStream = 0;
                reader.bitStreamLen = 0;

                bits64 = reader.readS64(32);
                if (bits64 < 0)
                    return -1;

                sizeAndMagic = LHMisc.Swap32((uint)bits64);
                if ((sizeAndMagic & 0xF0) != 0x40)
                    return -1;

                destCount = (int)(sizeAndMagic >> 8);
                if (destCount == 0)
                {
                    bits64 = reader.readS64(32);
                    if (bits64 < 0)
                        return -1;

                    destCount = (int)LHMisc.Swap32((uint)bits64);
                }

                bits32 = reader.readS32(16);
                if (bits32 < 0)
                {
                    if (destCount == 0 && 0x20 < reader.bitStreamLen)
                        return -3;

                    return destCount;
                }

                lengthHuffTbl = WorkBuffer;
                currentIdx = 1;
                huffLen = (int)((LHMisc.Swap16((ushort)bits32) + 1 << 5) - 16);

                while (huffLen >= 9)
                {
                    bits32 = reader.readS32(9);
                    if (bits32 < 0)
                    {
                        if (destCount == 0 && 0x20 < reader.bitStreamLen)
                            return -3;

                        return destCount;
                    }

                    lengthHuffTbl[currentIdx] = (ushort)bits32;
                    currentIdx += 1;
                    huffLen -= 9;
                }

                if (huffLen > 0)
                {
                    bits32 = reader.readS32((byte)huffLen);
                    if (bits32 < 0)
                    {
                        if (destCount == 0 && 0x20 < reader.bitStreamLen)
                            return -3;

                        return destCount;
                    }

                    huffLen = 0;
                }

                bits32 = reader.readS32(8);
                if (bits32 < 0)
                {
                    if (destCount == 0 && 0x20 < reader.bitStreamLen)
                        return -3;

                    return destCount;
                }

                offsetHuffTbl = WorkBuffer + 1024;
                currentIdx = 1;
                huffLen = ((ushort)bits32 + 1 << 5) - 8;

                while (huffLen >= 5)
                {
                    bits32 = reader.readS32(5);
                    if (bits32 < 0)
                    {
                        if (destCount == 0 && 0x20 < reader.bitStreamLen)
                            return -3;

                        return destCount;
                    }

                    offsetHuffTbl[currentIdx] = (ushort)bits32;
                    currentIdx += 1;
                    huffLen -= 5;
                }

                if (huffLen > 0)
                {
                    bits32 = reader.readS32((byte)huffLen);
                    if (bits32 < 0)
                    {
                        if (destCount == 0 && 0x20 < reader.bitStreamLen)
                            return -3;

                        return destCount;
                    }

                    huffLen = 0;
                }

                while (destCount > 0)
                {
                    currentNode = lengthHuffTbl + 1;

                    while (true)
                    {
                        bits32 = reader.readS32(1);
                        if (bits32 < 0)
                        {
                            if (destCount == 0 && 0x20 < reader.bitStreamLen)
                                return -3;

                            return destCount;
                        }

                        shift = (byte)(bits32 & 1);
                        offset = (((uint)currentNode[0] & 0x7F) + 1 << 1) + shift;

                        if ((currentNode[0] & 0x100 >> shift) > 0)
                        {
                            copyLen = ((ushort*)((uint)currentNode & ~3u))[offset];
                            currentNode = offsetHuffTbl + 1;
                            break;
                        }

                        else
                            currentNode = (ushort*)((uint)currentNode & ~3u) + offset;
                    }

                    if (copyLen < 0x100)
                    {
                        dst[dstIdx] = (byte)copyLen;
                        dstIdx += 1;
                        destCount -= 1;
                    }

                    else
                    {
                        n = (ushort)((copyLen & 0xFF) + 3);

                        while (true)
                        {
                            bits32 = reader.readS32(1);
                            if (bits32 < 0)
                            {
                                if (destCount == 0 && 0x20 < reader.bitStreamLen)
                                    return -3;

                                return destCount;
                            }

                            shift = (byte)(bits32 & 1);
                            offset = (((uint)currentNode[0] & 7) + 1 << 1) + shift;

                            if ((currentNode[0] & 0x10 >> shift) > 0)
                            {
                                currentNode = (ushort*)((uint)currentNode & ~3u);
                                nOffsetBits = (sbyte)currentNode[offset];
                                break;
                            }

                            else
                                currentNode = (ushort*)((uint)currentNode & ~3u) + offset;
                        }

                        if (nOffsetBits <= 1)
                            bits32 = nOffsetBits;

                        else
                        {
                            bits32 = reader.readS32((byte)(nOffsetBits - 1));
                            if (bits32 < 0)
                            {
                                if (destCount == 0 && 0x20 < reader.bitStreamLen)
                                    return -3;

                                return destCount;
                            }
                        }

                        if (nOffsetBits >= 2)
                            bits32 |= 1 << nOffsetBits - 1;

                        nOffsetBits = -1;
                        lzOffset = (ushort)(bits32 + 1);

                        if (destCount < n)
                            n = (ushort)destCount;

                        destCount -= n;
                        while (n-- > 0)
                        {
                            dst[dstIdx] = dst[dstIdx - lzOffset];
                            dstIdx += 1;
                        }
                    }
                }

                if (0x20 < reader.bitStreamLen)
                    return -3;

                return 0;
            }
        }
    }
}
