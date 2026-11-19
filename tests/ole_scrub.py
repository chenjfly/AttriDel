# -*- coding: utf-8 -*-
"""
OLE 复合文档物理擦除：把空闲扇区和 MiniStream 空闲 mini 扇区填零，
活跃流（正文）字节完全不动。仅处理无 DIFAT 的常规文档（<7MB）。
返回 (是否处理, 清零字节数, 说明)。
"""
import struct

FREESECT   = 0xFFFFFFFF
ENDOFCHAIN = 0xFFFFFFFE
FATSECT    = 0xFFFFFFFD
DIFSECT    = 0xFFFFFFFC
NOSTREAM   = 0xFFFFFFFF

def scrub(path):
    with open(path, "r+b") as f:
        data = bytearray(f.read())

    if data[:8] != bytes.fromhex("d0cf11e0a1b11ae1"):
        return False, 0, "not OLE"

    sector_shift = struct.unpack_from("<H", data, 0x1E)[0]
    mini_shift   = struct.unpack_from("<H", data, 0x20)[0]
    sector_size  = 1 << sector_shift          # 通常 512
    mini_size    = 1 << mini_shift            # 通常 64
    num_fat      = struct.unpack_from("<I", data, 0x2C)[0]
    first_dir    = struct.unpack_from("<I", data, 0x30)[0]
    cutoff       = struct.unpack_from("<I", data, 0x38)[0]
    first_minifat= struct.unpack_from("<I", data, 0x3C)[0]
    num_minifat  = struct.unpack_from("<I", data, 0x40)[0]
    num_difat    = struct.unpack_from("<I", data, 0x48)[0]

    if num_difat > 0:
        return False, 0, "has DIFAT, skip"   # 大文件，保守跳过

    def sec_off(s):
        return (s + 1) * sector_size

    total_sectors = (len(data) - sector_size) // sector_size
    if total_sectors < 0:
        return False, 0, "bad size"

    # 读 DIFAT（header 内 109 项）
    fat_sectors = []
    for i in range(109):
        v = struct.unpack_from("<I", data, 0x4C + i * 4)[0]
        if v == FREESECT:
            break
        fat_sectors.append(v)
    if len(fat_sectors) < num_fat:
        return False, 0, "incomplete DIFAT"

    # 组装 FAT 数组
    fat = []
    for fs in fat_sectors:
        off = sec_off(fs)
        for i in range(sector_size // 4):
            fat.append(struct.unpack_from("<I", data, off + i * 4)[0])

    def chain(start, table, limit):
        out = []
        s = start
        seen = set()
        while s not in (FREESECT, ENDOFCHAIN, FATSECT, DIFSECT, NOSTREAM) and s < limit:
            if s in seen:
                break
            seen.add(s)
            out.append(s)
            s = table[s] if s < len(table) else ENDOFCHAIN
        return out

    # 活跃扇区集合：所有 FAT 链（目录、MiniFAT、MiniStream、各流）
    active = set()
    for fs in fat_sectors:
        active.add(fs)

    # 目录链
    dir_chain = chain(first_dir, fat, total_sectors)
    active.update(dir_chain)

    # 解析目录项
    root_start = None
    root_size = 0
    stream_entries = []
    for ds in dir_chain:
        off = sec_off(ds)
        for i in range(sector_size // 128):
            e = off + i * 128
            if e + 128 > len(data):
                break
            obj_type = data[e + 0x42]
            if obj_type in (1, 2, 5):
                start = struct.unpack_from("<I", data, e + 0x74)[0]
                size  = struct.unpack_from("<I", data, e + 0x78)[0]
                if obj_type == 5:  # root
                    root_start = start
                    root_size = size
                else:
                    stream_entries.append((start, size))

    # 各流链
    for start, size in stream_entries:
        if size >= cutoff:
            active.update(chain(start, fat, total_sectors))

    # MiniFAT 链
    minifat_chain = chain(first_minifat, fat, total_sectors)
    active.update(minifat_chain)
    minifat = []
    for ms in minifat_chain:
        off = sec_off(ms)
        for i in range(sector_size // 4):
            minifat.append(struct.unpack_from("<I", data, off + i * 4)[0])

    # MiniStream 链（root 的流）
    ministream_chain = []
    if root_start is not None:
        ministream_chain = chain(root_start, fat, total_sectors)
    active.update(ministream_chain)

    scrubbed = 0

    # 1) 清零 FAT 中标记为空闲的扇区
    for s in range(total_sectors):
        if s >= len(fat):
            break
        if fat[s] == FREESECT and s not in active:
            off = sec_off(s)
            end = min(off + sector_size, len(data))
            if off < len(data):
                scrubbed += (end - off)
                data[off:end] = b"\x00" * (end - off)

    # 2) 清零 MiniStream 中空闲的 mini 扇区
    #    mini sector k 的物理位置 = MiniStream 第 k*mini_size 字节
    def mini_to_physical(k):
        byte_off = k * mini_size
        if byte_off >= root_size:
            return None
        # 找到 byte_off 落在 ministream_chain 的哪个扇区
        idx = byte_off // sector_size
        within = byte_off % sector_size
        if idx >= len(ministream_chain):
            return None
        return sec_off(ministream_chain[idx]) + within, mini_size

    if minifat:
        for k in range(len(minifat)):
            if minifat[k] == FREESECT:
                loc = mini_to_physical(k)
                if loc:
                    off, sz = loc
                    end = min(off + sz, len(data))
                    if off < len(data):
                        scrubbed += (end - off)
                        data[off:end] = b"\x00" * (end - off)

    with open(path, "wb") as f:
        f.write(data)
    return True, scrubbed, "ok"
