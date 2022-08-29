using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NSMBWCompression
{
    class Program
    {
        static void Main(string[] args)
        {
            int mode = -1;
            string ipath = "";
            string opath = "";
            Console.WriteLine("NSMBWCompression - Compresses and decompresses files to/from LH and LZ formats.");
            Console.WriteLine("Credits:");
            Console.WriteLine("\tLZ Decompression: https://github.com/RoadrunnerWMC/Reggie-Updated/blob/master/lz77.py");
            Console.WriteLine("\tLH Decompression: https://github.com/aboood40091/LHDecompressor/blob/main/LHDecompressor.cpp");
            Console.WriteLine("\tLZ & LH Compression: https://github.com/VNSMB/LHCompressor");
            Console.WriteLine("\tC# Porting & wrapping as a standalone tool: RedStoneMatt");
            Console.WriteLine();
            Console.WriteLine();

            if (args.Length == 0)
            {
                Console.WriteLine("No arguments were provided.");
                Console.WriteLine("Please run \"" + System.AppDomain.CurrentDomain.FriendlyName + " -h\" to see available arguments");
                Console.WriteLine();

                while (true)
                {
                    Console.Write("Mode? (0 = decomp; 1 = LZ; 2 = LH): ");
                    try
                    {
                        mode = Convert.ToInt32(Console.ReadLine());

                        if (mode >= 0 && mode <= 2)
                            break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Invalid option");
                    }
                }

                while (true)
                {
                    Console.Write("Input file? ");
                    ipath = Console.ReadLine();
                    if (File.Exists(ipath)) break;
                    Console.WriteLine("Error: no such file or directory.");
                }

                Console.Write("Output file? ");
                opath = Console.ReadLine();
            }
            else if (args[0] == "-h" || args[0] == "--help" || args[0] == "/?")
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("\t" + System.AppDomain.CurrentDomain.FriendlyName + " <mode> <source> <destination>");
                Console.WriteLine();
                Console.WriteLine("\tsource:\t\tPath to the source file to compress or decompress.");
                Console.WriteLine("\tdestination:\tPath to the destination file to save the compressed or decompressed data to.");
                Console.WriteLine("\tmode:");
                Console.WriteLine("\t\t-x\t\t\tDecompresses a LZ or LH compressed source file.");
                Console.WriteLine("\t\t-lz\t\t\tCompresses a source file to the LZ77 (Extended) format.");
                Console.WriteLine("\t\t-lh\t\t\tCompresses a source file to the LH (LZ77+Huffman) format.");
                Console.WriteLine();
                return;
            }
            else if(args.Length == 3)
            {
                if (args[0] == "-x") mode = 0;
                else if (args[0] == "-lz") mode = 1;
                else if (args[0] == "-lh") mode = 2;
                else
                {
                    Console.WriteLine("Invalid argument: \"" + args[0] + "\" is not a valid mode");
                    return;
                }

                if (File.Exists(args[1])) ipath = args[1];
                else
                {
                    Console.WriteLine("Invalid argument: Can't open \"" + args[1] + "\": no such file or directory");
                    return;
                }

                opath = args[2];
            }
            else
            {
                Console.Write("Extraneous argument" + ((args.Length > 4) ? "s" : "") + ": ");
                for(int i = 3; i < args.Length; i++)
                {
                    Console.Write("\"" + args[i] + "\"");
                    if (i < args.Length - 1) Console.Write(", ");
                }
                Console.WriteLine();

                return;
            }


            byte[] src = File.ReadAllBytes(ipath);
            if (mode == 0) // Decompress
            {
                if (src[0] == 0x11) // LZ
                {
                    long btime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                    byte[] data = new byte[LZDecompressor.getDecompSize(src)];
                    int error = LZDecompressor.decomp(ref data, src);

                    long atime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                    if (error == 0)
                        Console.WriteLine("Decompressed " + src.Length + " bytes into " + data.Length + " bytes in " + writeResult(ref data, opath, atime - btime));
                    else
                        Console.WriteLine("Couldn't decompress file: an error occured (" + error + ").");
                }
                else if (src[0] == 0x40) // LH
                {
                    long btime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                    byte[] data = new byte[LHDecompressor.getDecompSize(src)];
                    int error = LHDecompressor.decomp(ref data, src);

                    long atime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                    if (error == 0)
                        Console.WriteLine("Decompressed " + src.Length + " bytes into " + data.Length + " bytes in " + writeResult(ref data, opath, atime - btime));
                    else
                        Console.WriteLine("Couldn't decompress file: an error occured (" + error + ").");
                }
                else
                {
                    Console.WriteLine("Invalid compression format.");
                }
            }
            else if (mode == 1) // LZ Compress
            {
                long btime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                byte[] data = Compressor.compLZ(src);

                long atime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                Console.WriteLine("Compressed " + src.Length + " bytes into " + data.Length + " bytes in " + writeResult(ref data, opath, atime - btime));
            }
            else if (mode == 2) // LH Compress
            {
                long btime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                byte[] data = Compressor.compLH(src);

                long atime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                Console.WriteLine("Compressed " + src.Length + " bytes into " + data.Length + " bytes in " + writeResult(ref data, opath, atime - btime));
            }
        }

        public static string writeResult(ref byte[] outdata, string outpath, long time)
        {
            TimeSpan t = TimeSpan.FromMilliseconds(time);

            string answer = string.Format("{0:D2}h:{1:D2}m:{2:D2}s:{3:D3}ms",
                            t.Hours,
                            t.Minutes,
                            t.Seconds,
                            t.Milliseconds);

            File.WriteAllBytes(outpath, outdata);

            return answer;
        }
    }

    public static class Extensions { 


        // Stolen from https://www.csharp411.com/check-valid-file-path-in-c/
        /// <summary>
        /// Gets whether the specified path is a valid absolute file path.
        /// </summary>
        /// <param name="path">Any path. OK if null or empty.</param>
        static public bool IsValidPath(string path)
        {
            Regex r = new Regex(@"^(([a-zA-Z]:)|(\))(\{1}|((\{1})[^\]([^/:*?<>""|]*))+)$");
            return r.IsMatch(path);
        }
    }
}
