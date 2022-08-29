using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NSMBWCompression
{
    // Stolen from https://github.com/VNSMB/LHCompressor/tree/d86fe2cae97d878e3d06199e49facac8ed5db620

    public unsafe class Compressor
    {
        //-----------------------------------------------
        // DEFINITIONS
        //-----------------------------------------------

        public const uint LZ_HEADER = 0x11;
        public const uint LH_HEADER = 0x40;

        public const byte LZ_OFFSET_BITS = 12;
        public const byte LH_OFFSET_BITS = 15;
        public const byte LH_OFFSET_TABLE_BITS = 5;

        public const byte LENGTH_BITS = 9;
        public const int OFFSET_SIZE_MAX = (1 << 15);

        public const int TABLE_SIZE = 256;

        //-------------------------------------------------------------
        //LZ structures
        //-------------------------------------------------------------

        public unsafe struct LZstruc
        {
            public ushort wPos;
            public ushort wLen;

            public fixed short offsetTable[OFFSET_SIZE_MAX];

            public fixed short byteTable[TABLE_SIZE];
            public fixed short endTable[TABLE_SIZE];
            public byte offsetBits;
        };
        public static LZstruc lz = new LZstruc();

        //-------------------------------------------------------------
        //Huffman structures
        //-------------------------------------------------------------

        public unsafe struct Node
        {
            public ushort id;
            public short parentId;
            public uint frequency; //The frequency of the node's appearance.
            public fixed short childrenId[2]; //The ids of the children.
            public ushort parentDepth; //Depth of the parent node.
            public ushort leafDepth; //Depth of the leaf.
            public uint huffmanCode;
            public ushort data;
            public ushort subTreeMem; //Amount of memory required to store the subtree rooted at this node.

            public Node(ushort id, short parentId, uint frequency, short[] childrenId, ushort parentDepth, ushort leafDepth, uint huffmanCode, ushort data, ushort subTreeMem)
            {
                this.id = id;
                this.parentId = parentId;
                this.frequency = frequency;
                this.parentDepth = parentDepth;
                this.leafDepth = leafDepth;
                this.huffmanCode = huffmanCode;
                this.data = data;
                this.subTreeMem = subTreeMem;

                fixed (Node* nd = &this)
                {
                    nd->childrenId[0] = childrenId[0];
                    nd->childrenId[1] = childrenId[1];
                }
            }
        };

        public struct NodeOffsetNeed
        {
            public byte offsetNeedL;
            public byte offsetNeedR;
            public ushort leftNode;
            public ushort rightNode;

            public NodeOffsetNeed(byte offsetNeedL, byte offsetNeedR, ushort leftNode, ushort rightNode)
            {
                this.offsetNeedL = offsetNeedL;
                this.offsetNeedR = offsetNeedR;
                this.leftNode = leftNode;
                this.rightNode = rightNode;
            }
        };

        public unsafe struct Huffman
        {
            public Node* table;
            public ushort* tree;
            public NodeOffsetNeed* offsetNeed;
            public ushort root;
            public byte size;
            public fixed byte padding_[1];
        };

        //-------------------------------------------------------------
        // LZ Compression
        //-------------------------------------------------------------

        static unsafe void init_LZ(ref LZstruc lz)
        {
            for (ushort i = 0; i < TABLE_SIZE; i++)
            {
                lz.byteTable[i] = -1;
                lz.endTable[i] = -1;
            }
            lz.wPos = 0;
            lz.wLen = 0;
        }

        static unsafe void slide(ref LZstruc lz, byte* data) {
            short offset;
            byte input = *data;
            ushort insertOffset;

            ushort windowPos = lz.wPos;
            ushort windowLen = lz.wLen;
            uint offsetSize = (uint)(1 << lz.offsetBits);


            if (windowLen == offsetSize ) {
                byte outData = *(data - offsetSize);
                if ((lz.byteTable[outData] = lz.offsetTable[lz.byteTable[outData]]) == -1) lz.endTable[outData] = -1;
                insertOffset = windowPos;
            }
            else insertOffset = windowLen;

            offset = lz.endTable[input];
            if (offset == -1) lz.byteTable[input] = (short)insertOffset;
            else lz.offsetTable[offset] = (short)insertOffset;

            lz.endTable[input] = (short)insertOffset;
            lz.offsetTable[insertOffset] = -1;

            if (windowLen == offsetSize) lz.wPos = (ushort) ((windowPos + 1) % offsetSize);
            else lz.wLen++;
        }

        static unsafe void n_slide(ref LZstruc info, byte* data, uint n)
        {
            for (uint i = 0; i < n; i++)
            {
                slide(ref info, data++);
            }
        }

        static unsafe ushort search(ref LZstruc lz, byte* searchIn, uint size, ushort* offset, ushort minOffset, uint maxLength)
        {
            byte* search;
            byte* head, searchHead;
            ushort currOffset = 0;
            ushort currLength = 2;
            ushort tmpLength;
            int windowOffset;

            if (size < 3) return 0;

            windowOffset = lz.byteTable[*searchIn];
            while (windowOffset != -1)
            {
                if (windowOffset < lz.wPos) search = searchIn - lz.wPos + windowOffset;
                else search = searchIn - lz.wLen - lz.wPos + windowOffset;

                if (*(search + 1) != *(searchIn + 1) || *(search + 2) != *(searchIn + 2))
                {
                    windowOffset = lz.offsetTable[windowOffset];
                    continue;
                }

                if (searchIn - search < minOffset) break;

                tmpLength = 3;
                searchHead = search + 3;
                head = searchIn + 3;

                while (((uint)(head - searchIn) < size) && (*head == *searchHead))
                {
                    head++;
                    searchHead++;
                    tmpLength++;

                    if (tmpLength == maxLength) break;
                }
                if (tmpLength > currLength)
                {
                    currLength = tmpLength;
                    currOffset = (ushort)(searchIn - search);
                    if (currLength == maxLength) break;

                }
                windowOffset = lz.offsetTable[windowOffset];
            }

            if (currLength < 3) return 0;
            *offset = currOffset;
            return currLength;
        }

        static unsafe uint LZCompress(byte* uncData, int size, byte* dest, byte searchOffset, byte offsetBits)
        {
            uint compressedBytes = 0; //Amount of compressed bytes.
            byte flags; //Flags to determine whether the data is being compressed or not.
            byte* flags_p;
            ushort lastOffset; //Offset to longest matching data so far.
            ushort lastLength; //The length of the longest matching data at the current point.

            const uint maxLength = 0xFF + 3;

            init_LZ(ref lz);
            lz.offsetBits = offsetBits;

            while (size > 0)
            {
                flags = 0;
                flags_p = dest++;
                compressedBytes++;

                //( times looped because 8-bit data
                for (byte j = 0; j < 8; j++)
                {
                    flags <<= 1;
                    if (size <= 0) continue;

                    if ((lastLength = search(ref lz, uncData, (uint)size, &lastOffset, searchOffset, maxLength)) != 0)
                    {
                        flags |= 0x1;

                        *dest++ = (byte)(lastLength - 3); //Offset is stored in the 4 upper and 8 lower bits.
                        *dest++ = (byte)((lastOffset - 1) & 0xff); //Little Endian
                        *dest++ = (byte)((lastOffset - 1) >> 8);
                        compressedBytes += 3;
                        n_slide(ref lz, uncData, lastLength);
                        uncData += lastLength;
                        size -= lastLength;
                    }
                    else
                    {
                        n_slide(ref lz, uncData, 1);
                        *dest++ = *uncData++;
                        size--;
                        compressedBytes++;
                    }
                }
                *flags_p = flags;
            }

            byte i = 0;
            while (((compressedBytes + i) & 0x3) > 0)
            {
                *dest++ = 0;
                i++;
            }

            return compressedBytes;
        }

        static unsafe uint LZExCompress(byte* uncData, int size, byte* dest, byte searchOffset, byte offsetBits)
        {
            uint compressedBytes = 0; //Amount of compressed bytes.
            byte flags; //Flags to determine whether the data is being compressed or not.
            byte* flags_p;
            ushort lastOffset; //Offset to longest matching data so far.
            ushort lastLength; //The length of the longest matching data at the current point.

            const uint maxLength = 0xFF + 3;

            init_LZ(ref lz);
            lz.offsetBits = offsetBits;

            while (size > 0)
            {
                flags = 0;
                flags_p = dest++;
                compressedBytes++;

                //( times looped because 8-bit data
                for (byte j = 0; j < 8; j++)
                {
                    flags <<= 1;
                    if (size <= 0) continue;

                    if ((lastLength = search(ref lz, uncData, (uint)size, &lastOffset, searchOffset, maxLength)) != 0)
                    {
                        int length;
                        flags |= 0x1;

                        if (lastLength >= 0xFF + 0xF + 3)
                        {
                            length = lastLength - 0xFF - 0xF - 3;
                            *dest++ = (byte)(0x10 | (length >> 12));
                            *dest++ = (byte)(length >> 4);
                            compressedBytes += 2;
                        }
                        else if (lastLength >= 0xF + 2)
                        {
                            length = lastLength - 0xF - 2;
                            *dest++ = (byte)(length >> 4);
                            compressedBytes += 1;
                        }
                        else
                        {
                            length = lastLength - 1;
                        }

                        *dest++ = (byte)(length << 4 | (lastOffset - 1) >> 8);
                        *dest++ = (byte)((lastOffset - 1) & 0xFF);
                        compressedBytes += 2;
                        n_slide(ref lz, uncData, lastLength);
                        uncData += lastLength;
                        size -= lastLength;
                    }
                    else
                    {
                        n_slide(ref lz, uncData, 1);
                        *dest++ = *uncData++;
                        size--;
                        compressedBytes++;
                    }
                }
                *flags_p = flags;
            }

            byte i = 0;
            while (((compressedBytes + i) & 0x3) > 0)
            {
                *dest++ = 0;
                i++;
            }

            return compressedBytes;
        }

        //-----------------------------------------------------------
        // Huffman Compression
        //-----------------------------------------------------------

        static void initHuffman(Huffman* h, byte bitSize)
        {
            uint tableSize = (uint)(1 << bitSize);

            Node[] htable = new Node[tableSize * 2];
            fixed (Node* htbl = htable)
            {
                h->table = htbl;

                ushort[] htree = new ushort[tableSize * 2];
                fixed (ushort* htr = htree)
                {
                    h->tree = htr;


                    NodeOffsetNeed[] hoffsetneed = new NodeOffsetNeed[tableSize];
                    fixed (NodeOffsetNeed* hofssnd = hoffsetneed) {
                        h->offsetNeed = hofssnd;

                        h->root = 1;
                        h->size = bitSize;

                        Node initNode = new Node(0, 0, 0, new short[] { -1, -1 }, 0, 0, 0, 0, 0 );
                        for (uint i = 0; i < tableSize * 2; i++)
                        {
                            h->table[i] = initNode;
                            h->table[i].id = (ushort)i;
                        }

                        NodeOffsetNeed initNodeOff = new NodeOffsetNeed(1, 1, 0, 0);
                        for (uint i = 0; i < tableSize; i++)
                        {
                            h->tree[i * 2] = 0;
                            h->tree[i * 2 + 1] = 0;
                            h->offsetNeed[i] = initNodeOff;
                        }
                    }
                }
            }
        }

        static void LZ_huffMem(byte* data, uint size, Huffman* bit8, Huffman* bit16)
        {
            uint processed = 0;

            while (processed < size)
            {
                byte flags = data[processed++]; //Whether compression is applied or not.
                for (uint i = 0; i < 8; i++)
                {
                    if ((flags & 0x80) > 0)
                    {
                        byte length = data[processed++];
                        ushort offset = data[processed++]; //Little endian.
                        offset |= (ushort)(data[processed++] << 8);

                        bit8->table[length | 0x100].frequency++;

                        uint offset_bit = 0;
                        while (offset != 0)
                        {
                            ++offset_bit;
                            offset >>= 1;
                        }
                        bit16->table[offset_bit].frequency++;
                    }
                    else
                    {
                        byte b = data[processed++];
                        bit8->table[b].frequency++;
                    }
                    flags <<= 1;
                    if (processed >= size) break;
                }
            }
        }

        static void constructTree(Huffman* h, byte bitSize)
        {
            Node* table = h->table;
            ushort root = makeNode(table, bitSize);

            addCodeToTable(table, root, 0x00);
            neededSpaceForTree(table, root);

            makeTree(h, root);
            h->root--;
        }

        static void makeTree(Huffman* h, ushort root)
        {
            short memSub, tempMemSub;
            short sizeOffsetNeed, tempSizeOffsetNeed;
            short sizeMaxKey;
            bool sizeMaxRightFlag = false;
            ushort offsetNeedNum;
            bool tempRightFlag;
            uint maxSize = 1u << (h->size - 2);

            h->root = 1;
            sizeOffsetNeed = 0;

            h->offsetNeed[0].offsetNeedL = 0;
            h->offsetNeed[0].rightNode = root;

            while (true)
            {
                offsetNeedNum = 0;
                for (short i = 0; i < h->root; i++)
                {
                    if (h->offsetNeed[i].offsetNeedL > 0) offsetNeedNum++;
                    if (h->offsetNeed[i].offsetNeedR > 0) offsetNeedNum++;
                }

                memSub = -1;
                sizeMaxKey = -1;

                for (short i = 0; i < h->root; i++)
                {
                    tempSizeOffsetNeed = (short)(h->root - i);

                    if (h->offsetNeed[i].offsetNeedL > 0)
                    {
                        tempMemSub = (short)h->table[h->offsetNeed[i].leftNode].subTreeMem;

                        if ((uint)(tempMemSub + offsetNeedNum) > maxSize) goto leftCostEvaluationEnd;
                        if (!subTreeExpansionPossible(h, (ushort)tempMemSub)) goto leftCostEvaluationEnd;

                        if (tempMemSub > memSub)
                        {
                            sizeMaxKey = i;
                            sizeMaxRightFlag = false;
                        }
                        else if ((tempMemSub == memSub) && (tempSizeOffsetNeed > sizeOffsetNeed))
                        {
                            sizeMaxKey = i;
                            sizeMaxRightFlag = false;
                        }
                    }
                    leftCostEvaluationEnd: { }

                    if (h->offsetNeed[i].offsetNeedR > 0)
                    {
                        tempMemSub = (short)h->table[h->offsetNeed[i].rightNode].subTreeMem;

                        if ((uint)(tempMemSub + offsetNeedNum) > maxSize)
                        {
                            goto rightCostEvaluationEnd;
                        }
                        if (!subTreeExpansionPossible(h, (ushort)tempMemSub))
                        {
                            goto rightCostEvaluationEnd;
                        }
                        if (tempMemSub > memSub)
                        {
                            sizeMaxKey = i;
                            sizeMaxRightFlag = true;
                        }
                        else if ((tempMemSub == memSub) && (tempSizeOffsetNeed > sizeOffsetNeed))
                        {
                            sizeMaxKey = i;
                            sizeMaxRightFlag = true;
                        }
                    }
                    rightCostEvaluationEnd: { }
                }

                if (sizeMaxKey >= 0)
                {
                    makeSubTree(h, (ushort)sizeMaxKey, sizeMaxRightFlag);
                    goto nextTreeMaking;
                }
                else
                {
                    for (short i = 0; i < h->root; i++)
                    {
                        ushort tmp = 0;
                        tempRightFlag = false;
                        if (h->offsetNeed[i].offsetNeedL > 0)
                        {
                            tmp = h->table[h->offsetNeed[i].leftNode].subTreeMem;
                        }
                        if (h->offsetNeed[i].offsetNeedR > 0)
                        {
                            if (h->table[h->offsetNeed[i].rightNode].subTreeMem > tmp)
                            {
                                tempRightFlag = true;
                            }
                        }
                        if ((tmp != 0) || (tempRightFlag))
                        {
                            createNoteTable(h, (ushort)i, tempRightFlag);
                            goto nextTreeMaking;
                        }
                    }
                }
                return;
            nextTreeMaking: { }
            }
        }

        static void makeSubTree(Huffman* h, ushort nodeId, bool rightNodeFlag)
        {
            ushort i = h->root;
            createNoteTable(h, nodeId, rightNodeFlag);

            if (rightNodeFlag) h->offsetNeed[nodeId].offsetNeedR = 0;
            else h->offsetNeed[nodeId].offsetNeedL = 0;


            while (i < h->root)
            {
                if (h->offsetNeed[i].offsetNeedL > 0)
                {
                    createNoteTable(h, i, false);
                    h->offsetNeed[i].offsetNeedL = 0;
                }
                if (h->offsetNeed[i].offsetNeedR > 0)
                {
                    createNoteTable(h, i, true);
                    h->offsetNeed[i].offsetNeedR = 0;
                }
                i++;
            }
        }

        static bool subTreeExpansionPossible(Huffman* h, ushort expandBy)
        {
            short capacity;
            uint maxSize = 1u << (h->size - 2);

            capacity = (short)(maxSize - expandBy);

            for (ushort i = 0; i < h->root; i++)
            {
                if (h->offsetNeed[i].offsetNeedL > 0)
                {
                    if ((h->root - i) <= capacity) capacity--;
                    else return false;
                }
                if (h->offsetNeed[i].offsetNeedR > 0)
                {
                    if ((h->root - i) <= capacity) capacity--;
                    else return false;
                }
            }

            return true;
        }

        static void createNoteTable(Huffman* h, ushort nodeId, bool rightNodeFlag)
        {
            ushort nodeNo;
            ushort offsetData = 0;

            if (rightNodeFlag)
            {
                nodeNo = h->offsetNeed[nodeId].rightNode;
                h->offsetNeed[nodeId].offsetNeedR = 0;
            }
            else
            {
                nodeNo = h->offsetNeed[nodeId].leftNode;
                h->offsetNeed[nodeId].offsetNeedL = 0;
            }

            if (h->table[h->table[nodeNo].childrenId[0]].leafDepth == 0)
            {
                offsetData |= 0x8000;
                h->tree[h->root * 2 + 0] = (ushort)h->table[nodeNo].childrenId[0];
                h->offsetNeed[h->root].leftNode = (ushort)h->table[nodeNo].childrenId[0];
                h->offsetNeed[h->root].offsetNeedL = 0;
            }
            else
            {
                h->offsetNeed[h->root].leftNode = (ushort)h->table[nodeNo].childrenId[0];
            }

            if (h->table[h->table[nodeNo].childrenId[1]].leafDepth == 0)
            {
                offsetData |= 0x4000;
                h->tree[h->root * 2 + 1] = (ushort)h->table[nodeNo].childrenId[1];
                h->offsetNeed[h->root].rightNode = (ushort)h->table[nodeNo].childrenId[1];
                h->offsetNeed[h->root].offsetNeedR = 0;
            }
            else
            {
                h->offsetNeed[h->root].rightNode = (ushort)h->table[nodeNo].childrenId[1];
            }

            offsetData |= (ushort)(h->root - nodeId - 1);
            h->tree[nodeId * 2 + (rightNodeFlag ? 1 : 0)] = offsetData;

            h->root++;
        }

        static ushort makeNode(Node* table, byte bitSize)
        {
            ushort dataNum = (ushort)(1u << bitSize);
            ushort tableTop = (ushort)dataNum;

            uint i;
            int leftNo, rightNo;
            ushort rootNo;

            leftNo = -1;
            rightNo = -1;
            while (true)
            {
                for (i = 0; i < tableTop; i++)
                {
                    if ((table[i].frequency == 0) || (table[i].parentId != 0))
                    {
                        continue;
                    }
                    if (leftNo < 0)
                    {
                        leftNo = (int)i;
                    }
                    else if (table[i].frequency < table[leftNo].frequency)
                    {
                        leftNo = (int)i;
                    }
                }

                for (i = 0; i < tableTop; i++)
                {
                    if ((table[i].frequency == 0) || (table[i].parentId != 0) ||
                        (i == leftNo))
                    {
                        continue;
                    }

                    if (rightNo < 0)
                    {
                        rightNo = (int)i;
                    }
                    else if (table[i].frequency < table[rightNo].frequency)
                    {
                        rightNo = (int)i;
                    }
                }

                if (rightNo < 0)
                {
                    if (tableTop == dataNum)
                    {
                        table[tableTop].frequency = table[leftNo].frequency;
                        table[tableTop].childrenId[0] = (short)leftNo;
                        table[tableTop].childrenId[1] = (short)leftNo;
                        table[tableTop].leafDepth = 1;
                        table[leftNo].parentId = (short)tableTop;
                        table[leftNo].data = 0;
                        table[leftNo].parentDepth = 1;
                    }
                    else
                    {
                        tableTop--;
                    }
                    rootNo = tableTop;
                    return rootNo;
                }

                table[tableTop].frequency = table[leftNo].frequency + table[rightNo].frequency;
                table[tableTop].childrenId[0] = (short)leftNo;
                table[tableTop].childrenId[1] = (short)rightNo;
                if (table[leftNo].leafDepth > table[rightNo].leafDepth)
                {
                    table[tableTop].leafDepth = (ushort)(table[leftNo].leafDepth + 1);
                }
                else
                {
                    table[tableTop].leafDepth = (ushort)(table[rightNo].leafDepth + 1);
                }

                table[leftNo].parentId = table[rightNo].parentId = (short)(tableTop);
                table[leftNo].data = 0;
                table[rightNo].data = 1;

                addParentDepthToTable(table, (ushort)leftNo, (ushort)rightNo);

                tableTop++;
                leftNo = rightNo = -1;
            }
        }

        static void addParentDepthToTable(Node* table, ushort leftNo, ushort rightNo)
        {
            table[leftNo].parentDepth++;
            table[rightNo].parentDepth++;

            if (table[leftNo].leafDepth != 0)
            {
                addParentDepthToTable(table, (ushort)table[leftNo].childrenId[0], (ushort)table[leftNo].childrenId[1]);
            }
            if (table[rightNo].leafDepth != 0)
            {
                addParentDepthToTable(table, (ushort)table[rightNo].childrenId[0], (ushort)table[rightNo].childrenId[1]);
            }
        }

        static void addCodeToTable(Node* table, ushort node, uint paHuffCode)
        {
            table[node].huffmanCode = (paHuffCode << 1) | table[node].data;

            if (table[node].leafDepth != 0)
            {
                addCodeToTable(table, (ushort)table[node].childrenId[0], table[node].huffmanCode);
                addCodeToTable(table, (ushort)table[node].childrenId[1], table[node].huffmanCode);
            }
        }

        static ushort neededSpaceForTree(Node* table, ushort node)
        {
            ushort leftHWord, rightHWord;

            switch (table[node].leafDepth)
            {
                case 0:
                    return 0;
                case 1:
                    leftHWord = rightHWord = 0;
                    break;
                default:
                    leftHWord = neededSpaceForTree(table, (ushort)table[node].childrenId[0]);
                    rightHWord = neededSpaceForTree(table, (ushort)table[node].childrenId[1]);
                    break;
            }

            table[node].subTreeMem = (ushort)(leftHWord + rightHWord + 1);
            return (ushort)(leftHWord + rightHWord + 1);
        }

        static void LZ_makeTree(byte* data, uint size, Huffman* bit8, Huffman* bit16)
        {
            initHuffman(bit8, LENGTH_BITS);
            initHuffman(bit16, LH_OFFSET_TABLE_BITS);

            LZ_huffMem(data, size, bit8, bit16);

            constructTree(bit8, LENGTH_BITS);
            constructTree(bit16, LH_OFFSET_TABLE_BITS);
        }

        static uint writeHuffman(byte* dest, Huffman* h, byte bitSize)
        {
            BStream stream = new BStream();
            byte* pSize;
            uint tblSize;

            stream.initStream(dest);

            pSize = dest;
            uint roundUp = (uint)((bitSize + 7) & ~7);
            stream.writeStream(0, roundUp);

            for (uint i = 1; i < (ushort)((h->root + 1) * 2); i++)
            {
                ushort flags = (ushort)(h->tree[i] & 0xC000);
                uint data = (uint)(h->tree[i] | (flags >> (16 - bitSize)));
                stream.writeStream(data, bitSize);
            }
            stream.closeStream(4);

            tblSize = (stream.size / 4) - 1;
            if (roundUp == 8)
            {
                if (tblSize >= 0x100)
                {
                    //fprintf(stderr, "Table ended!\n");
                }
                *pSize = (byte)(tblSize);
            }
            else
            {
                if (tblSize >= 0x10000)
                {
                    //fprintf(stderr, "Table ended!\n");
                }
                *(ushort*)pSize = (ushort)(tblSize);
            }
            return stream.size;
        }

        static void ConvertHuff(Huffman* h, ushort data, ref BStream stream)
        {
            ushort width = h->table[data].parentDepth;
            uint code = h->table[data].huffmanCode;

            stream.writeStream(code, width);
        }

        static uint LZConvertHuffData(byte* data, uint srcSize, byte* dest, Huffman* bit8, Huffman* bit16)
        {
            uint srcCnt = 0;

            BStream stream = new BStream();

            stream.initStream(dest);

            while (srcCnt < srcSize)
            {
                uint i;
                byte compFlags = data[srcCnt++];

                for (i = 0; i < 8; i++)
                {
                    if ((compFlags & 0x80) > 0)
                    {
                        byte length = data[srcCnt++];
                        ushort offset = data[srcCnt++];
                        offset |= (ushort)(data[srcCnt++] << 8);

                        ConvertHuff(bit8, (ushort)(length | 0x100), ref stream);

                        ushort offset_bit = 0;
                        ushort offset_tmp = offset;
                        while (offset_tmp > 0)
                        {
                            offset_tmp >>= 1;
                            ++offset_bit;
                        }
                        ConvertHuff(bit16, offset_bit, ref stream);
                        stream.writeStream((uint)(offset & ~(1 << (offset_bit - 1))), (uint)(offset_bit - 1));
                    }
                    else
                    {
                        byte b = data[srcCnt++];
                        ConvertHuff(bit8, b, ref stream);
                    }
                    compFlags <<= 1;
                    if (srcCnt >= srcSize) break;

                }
            }

            stream.closeStream(4);
            return stream.size;
        }

        //-------------------------------------------------------------
        //Compression calling
        //-------------------------------------------------------------

        unsafe static uint LH(byte[] dat, int size, ref byte[] des) {
            Huffman bit8;
            Huffman bit16;

            uint tmpSize;
            uint dstSize;
            fixed (byte* tmpBuf = new byte[size * 3])
            {
                fixed (byte* dest = des)
                {
                    fixed (byte* data = dat)
                    {
                        tmpSize = LZCompress(data, size, tmpBuf, 2, LH_OFFSET_BITS);

                        LZ_makeTree(tmpBuf, tmpSize, &bit8, &bit16);

                        if (size < 0x1000000 && size > 0)
                        {
                            *(uint*)dest = (uint)(LH_HEADER | (size << 8));
                            dstSize = 4;
                        }
                        else
                        {
                            *(uint*)dest = LH_HEADER;
                            *(uint*)&dest[4] = (uint)size;
                            dstSize = 8;
                        }

                        dstSize += writeHuffman(&dest[dstSize], &bit8, LENGTH_BITS);
                        dstSize += writeHuffman(&dest[dstSize], &bit16, LH_OFFSET_TABLE_BITS);


                        dstSize += LZConvertHuffData(tmpBuf, tmpSize, &dest[dstSize], &bit8, &bit16);

                        return dstSize;
                    }
                }
            }
        }

        unsafe static uint LZ(byte[] dat, int size, ref byte[] des) {
            uint tmpSize;
            uint dstSize;
            fixed (byte* tmpBuf = new byte[size * 3])
            {
                fixed (byte* dest = des)
                {
                    fixed (byte* data = dat)
                    {
                        tmpSize = LZExCompress(data, size, tmpBuf, 2, LZ_OFFSET_BITS);

                        if (size < 0x1000000 && size > 0)
                        {
                            *(uint*)dest = (uint)(LZ_HEADER | (size << 8));
                            dstSize = 4;
                        }
                        else
                        {
                            *(uint*)dest = LZ_HEADER;
                            *(uint*)&dest[4] = (uint)size;
                            dstSize = 8;
                        }

                        for(int i = 0; i < tmpSize; i++)
                        {
                            dest[i + dstSize] = tmpBuf[i];
                        }

                        dstSize += tmpSize;

                        return dstSize;
                    }
                }
            }
        }

        public static byte[] compLH(byte[] src)
        {
            byte[] destBuf = new byte[src.Length * 3 + 512];
            uint destSize = LH(src, src.Length, ref destBuf);

            return destBuf.Take((int)destSize).ToArray();
        }

        public static byte[] compLZ(byte[] src)
        {
            byte[] destBuf = new byte[src.Length * 3 + 512];
            uint destSize = LZ(src, src.Length, ref destBuf);

            return destBuf.Take((int)destSize).ToArray();
        }
    }

    public unsafe struct BStream
    {
        //-----------------------------------------------
        // DEFINITIONS
        //-----------------------------------------------

        public byte* dest;
        public uint size;
        public uint stream;
        public uint streamSize;

        public void initStream(byte* dest)
        {
            this.dest = dest;
            this.size = 0;
            this.stream = 0;
            this.streamSize = 0;
        }

        public void writeStream(uint data, uint width)
        {
            uint stream = this.stream;
            uint size = this.size;
            uint streamSize = this.streamSize;
            uint mask = (1u << (int)width) - 1;

            if (width == 0) return;

            stream = (stream << (int)width) | (data & mask);
            streamSize += width;

            for (uint i = 0; i < streamSize / 8; i++)
            {
                this.dest[size++] = (byte)(stream >> (int)(streamSize - (i + 1) * 8));
            }
            streamSize %= 8;

            this.stream = stream;
            this.size = size;
            this.streamSize = streamSize;
        }

        public void closeStream(uint align)
        {
            uint stream = this.stream;
            uint size = this.size;
            uint streamSize = this.streamSize;

            if (streamSize > 0)
            {
                stream <<= (int)(8 - streamSize);

                if (this.streamSize != 0)
                {
                    this.dest[size++] = (byte)(stream);
                }
            }

            while (size % align > 0)
            {
                this.dest[size++] = 0;
            }
            this.size = size;
            this.streamSize = 0;
        }
    }
}
