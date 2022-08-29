# NSMBWCompression
Compresses and decompresses files to/from LH and LZ formats.

# Usage
	NSMBWCompression.exe <mode> <source> <destination>

source:		Path to the source file to compress or decompress.

destination:	Path to the destination file to save the compressed or decompressed data to.

mode:

* -x		Decompresses a LZ or LH compressed source file.
* -lz		Compresses a source file to the LZ77 (Extended) format.
* -lh		Compresses a source file to the LH (LZ77+Huffman) format.

# Credits

LZ Decompression: https://github.com/RoadrunnerWMC/Reggie-Updated/blob/master/lz77.py

LH Decompression: https://github.com/aboood40091/LHDecompressor/blob/main/LHDecompressor.cpp

LZ & LH Compression: https://github.com/VNSMB/LHCompressor

C# Porting & wrapping as a standalone tool: [RedStoneMatt](https://github.com/RedStoneMatt/)
