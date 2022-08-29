using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NSMBWCompression
{
    // Stolen from https://github.com/RoadrunnerWMC/Reggie-Updated/blob/28cbfdadc2d8be4bb55a657593e1e6ab7ac5f673/lz77.py

    class LZDecompressor
    {
        public static uint getDecompSize(byte[] src)
        {
            int offs = 0;
            return getDecompSize(src, ref offs);
        }

        public static uint getDecompSize(byte[] src, ref int offset)
        {
            uint size = (uint)((src[3] << 16) | (src[2] << 8) | src[1]);
            offset += 4;
            if (size == 0)
            {
                size = (uint)((src[7] << 16) | (src[6] << 8) | src[5]);
                offset += 4;
            }

            return size;
        }

        public static int decomp(ref byte[] dst, byte[] src)
        {
            int offset = 0;

            if (src[offset] != 0x11) return -1; // Magic

            uint decomp_size = getDecompSize(src, ref offset);

            int curr_size = 0;
            int lenFileIn = src.Length;

            while (curr_size < decomp_size && offset < lenFileIn)
            {
                byte flags = src[offset];
                offset += 1;

                for (int i = 0; i < 8; i++)
                {
                    int x = 7 - i;
                    if (curr_size >= decomp_size) break;

                    if ((flags & (1 << x)) > 0)
                    {
                        byte first = src[offset];
                        offset += 1;
                        byte second = src[offset];
                        offset += 1;

                        int copylen = 0;
                        int pos = 0;
                        if (first < 0x20)
                        {
                            byte third = src[offset];
                            offset += 1;

                            if (first >= 0x10)
                            {
                                byte fourth = src[offset];
                                offset += 1;

                                pos = (((third & 0xF) << 8) | fourth) + 1;
                                copylen = ((second << 4) | ((first & 0xF) << 12) | (third >> 4)) + 273;
                            }
                            else
                            {
                                pos = (((second & 0xF) << 8) | third) + 1;
                                copylen = (((first & 0xF) << 4) | (second >> 4)) + 17;
                            }
                        }
                        else
                        {
                            pos = (((first & 0xF) << 8) | second) + 1;
                            copylen = (first >> 4) + 1;
                        }

                        //byte[] copyBuf = dst.Skip(curr_size - pos).Take(copylen).ToArray();
                        /*
                        int copyBufLen = copyBuf.Length;
                        while (copyBufLen < copylen)
                        {
                            copyBuf = copyBuf.Concat(copyBuf).ToArray();
                            copyBufLen *= 2;
                        }
                        if (copyBufLen > copylen)
                        {
                            copyBuf = copyBuf.Take(copylen).ToArray();
                        }
                        */
                        //foreach (byte b in copyBuf) outdata.Add(b);
                        //dst = dst.Concat(copyBuf).ToArray();
                        for (int j = 0; j < copylen; j++)
                        {
                            dst[curr_size + j] = dst[curr_size - pos + j];
                        }

                        curr_size += copylen;
                    }
                    else
                    {
                        dst[curr_size++] = src[offset];
                        offset += 1;
                    }

                    if (offset >= src.Length || curr_size >= decomp_size) break;
                }
            }

            return 0;
        }
    }
}
