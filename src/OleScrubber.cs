using System;
using System.Collections.Generic;
using System.IO;

namespace AttriDel
{
    /// <summary>
    /// OLE 复合文档物理擦除器。
    /// 删除属性集后，原属性数据所在扇区变为空闲，但字节仍残留在文件中，
    /// 用十六进制工具可搜到公司名等敏感信息。本类把空闲扇区和 MiniStream
    /// 空闲 mini 扇区填零，活跃流（正文）字节完全不动。
    /// 对含 DIFAT 的大文件（>7MB）保守跳过，避免破坏结构。
    /// </summary>
    internal static class OleScrubber
    {
        private const uint FREESECT   = 0xFFFFFFFF;
        private const uint ENDOFCHAIN = 0xFFFFFFFE;
        private const uint FATSECT    = 0xFFFFFFFD;
        private const uint DIFSECT    = 0xFFFFFFFC;

        public static int Scrub(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 512) return 0;

            // OLE 签名
            byte[] sig = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
            for (int i = 0; i < 8; i++)
                if (data[i] != sig[i]) return 0;

            int sectorShift = BitConverter.ToUInt16(data, 0x1E);
            int miniShift   = BitConverter.ToUInt16(data, 0x20);
            int sectorSize  = 1 << sectorShift;      // 通常 512
            int miniSize    = 1 << miniShift;        // 通常 64
            uint numFat     = BitConverter.ToUInt32(data, 0x2C);
            uint firstDir   = BitConverter.ToUInt32(data, 0x30);
            uint cutoff     = BitConverter.ToUInt32(data, 0x38);
            uint firstMiniFat = BitConverter.ToUInt32(data, 0x3C);
            uint numDifat   = BitConverter.ToUInt32(data, 0x48);

            // 含 DIFAT 的大文件保守跳过
            if (numDifat > 0) return 0;

            int totalSectors = (data.Length - sectorSize) / sectorSize;
            if (totalSectors <= 0) return 0;

            Func<uint, int> secOff = s => (int)((s + 1) * (uint)sectorSize);

            // DIFAT（header 内 109 项）
            List<uint> fatSectors = new List<uint>();
            for (int i = 0; i < 109; i++)
            {
                uint v = BitConverter.ToUInt32(data, 0x4C + i * 4);
                if (v == FREESECT) break;
                fatSectors.Add(v);
            }
            if (fatSectors.Count < numFat) return 0;

            // FAT 数组
            List<uint> fat = new List<uint>();
            foreach (uint fs in fatSectors)
            {
                int off = secOff(fs);
                for (int i = 0; i < sectorSize / 4; i++)
                    fat.Add(BitConverter.ToUInt32(data, off + i * 4));
            }

            HashSet<uint> active = new HashSet<uint>();
            foreach (uint fs in fatSectors) active.Add(fs);

            Func<uint, List<uint>> chain = start =>
            {
                List<uint> outList = new List<uint>();
                uint s = start;
                HashSet<uint> seen = new HashSet<uint>();
                while (s != FREESECT && s != ENDOFCHAIN && s != FATSECT &&
                       s != DIFSECT && s < (uint)totalSectors)
                {
                    if (!seen.Add(s)) break;
                    outList.Add(s);
                    s = s < fat.Count ? fat[(int)s] : ENDOFCHAIN;
                }
                return outList;
            };

            // 目录链
            List<uint> dirChain = chain(firstDir);
            foreach (uint s in dirChain) active.Add(s);

            // 解析目录项
            uint rootStart = FREESECT;
            long rootSize = 0;
            List<KeyValuePair<uint, long>> streams = new List<KeyValuePair<uint, long>>();
            foreach (uint ds in dirChain)
            {
                int off = secOff(ds);
                for (int i = 0; i < sectorSize / 128; i++)
                {
                    int e = off + i * 128;
                    if (e + 128 > data.Length) break;
                    byte objType = data[e + 0x42];
                    if (objType != 1 && objType != 2 && objType != 5) continue;
                    uint start = BitConverter.ToUInt32(data, e + 0x74);
                    long size = BitConverter.ToInt64(data, e + 0x78);
                    if (objType == 5) { rootStart = start; rootSize = size; }
                    else streams.Add(new KeyValuePair<uint, long>(start, size));
                }
            }

            // 大流链
            foreach (var kv in streams)
                if (kv.Value >= cutoff)
                    foreach (uint s in chain(kv.Key)) active.Add(s);

            // MiniFAT
            List<uint> miniFatChain = chain(firstMiniFat);
            foreach (uint s in miniFatChain) active.Add(s);
            List<uint> miniFat = new List<uint>();
            foreach (uint ms in miniFatChain)
            {
                int off = secOff(ms);
                for (int i = 0; i < sectorSize / 4; i++)
                    miniFat.Add(BitConverter.ToUInt32(data, off + i * 4));
            }

            // MiniStream 链（root 的流）
            List<uint> miniStreamChain = rootStart != FREESECT ? chain(rootStart) : new List<uint>();
            foreach (uint s in miniStreamChain) active.Add(s);

            int scrubbed = 0;

            // 1) FAT 中空闲扇区填零
            for (int s = 0; s < totalSectors && s < fat.Count; s++)
            {
                if (fat[s] != FREESECT || active.Contains((uint)s)) continue;
                int off = secOff((uint)s);
                int end = Math.Min(off + sectorSize, data.Length);
                if (off >= data.Length) continue;
                for (int p = off; p < end; p++) data[p] = 0;
                scrubbed += end - off;
            }

            // 2) MiniStream 中空闲 mini 扇区填零
            if (miniFat.Count > 0 && miniStreamChain.Count > 0)
            {
                for (int k = 0; k < miniFat.Count; k++)
                {
                    if (miniFat[k] != FREESECT) continue;
                    long byteOff = (long)k * miniSize;
                    if (byteOff >= rootSize) continue;
                    int idx = (int)(byteOff / sectorSize);
                    int within = (int)(byteOff % sectorSize);
                    if (idx >= miniStreamChain.Count) continue;
                    int off = secOff(miniStreamChain[idx]) + within;
                    int end = Math.Min(off + miniSize, data.Length);
                    if (off >= data.Length) continue;
                    for (int p = off; p < end; p++) data[p] = 0;
                    scrubbed += end - off;
                }
            }

            if (scrubbed > 0)
                File.WriteAllBytes(path, data);
            return scrubbed;
        }
    }
}
