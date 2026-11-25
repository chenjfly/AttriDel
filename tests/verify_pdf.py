# -*- coding: utf-8 -*-
"""验证 AttriDel 的 PDF 清理效果。"""
import os, sys

DIR = r"F:\AI Project\AttriDel\tests\samples"

# 身份关键词，覆盖三种编码
KEYWORDS = [
    "测试标题", "作者_测试公司", "测试主题", "关键词A", "生成工具_测试",
    "测试公司有限公司", "最后修改人_测试", "经理_测试", "绝密字段_PRJ2020",
    "二次修改标题", "增量更新公司",
]
ENCODINGS = ["utf-8", "utf-16-be", "utf-16-le", "gbk"]
OLD_IDS = [
    b"0102030405060708090A0B0C0D0E0F10", b"1112131415161718191A1B1C1D1E1F20",
    b"A1A2A3A4A5A6A7A8A9AAABACADAEAFB0", b"B1B2B3B4B5B6B7B8B9BABBBCBDBEBFC0",
]
CONTENT_MARKER = b"PDF-CONTENT-MARKER-KEEP-THIS"


def check_cleaned(path):
    data = open(path, "rb").read()
    problems = []

    # 1. 身份字段三编码零残留
    for kw in KEYWORDS:
        for enc in ENCODINGS:
            try:
                pat = kw.encode(enc)
            except Exception:
                continue
            if pat in data:
                problems.append("残留身份信息(%s): %s" % (enc, kw))
                break

    # 2. XMP 元数据应清除
    for tag in [b"xmpmeta", b"xpacket", b"rdf:Description", b"CreatorTool"]:
        if tag in data:
            problems.append("XMP 标签残留: " + tag.decode("ascii"))

    # 3. 旧文档 ID 应消失
    for oid in OLD_IDS:
        if oid in data:
            problems.append("旧文档 ID 残留")

    # 4. 正文内容标记仍在
    if CONTENT_MARKER not in data:
        problems.append("页面内容标记丢失！")

    # 5. 结构有效性：%PDF 头 + startxref + xref + trailer
    if not data.startswith(b"%PDF-"):
        problems.append("PDF 头损坏")
    if b"startxref" not in data or b"trailer" not in data or b"xref\n" not in data:
        problems.append("xref/trailer 结构缺失")
    # startxref 指向的偏移应落在 xref 处
    tail = data[-2048:]
    sx = tail.rfind(b"startxref")
    if sx >= 0:
        num = tail[sx + 9:].strip().split(b"\n")[0].strip()
        try:
            off = int(num)
            if data[off:off + 4] != b"xref":
                problems.append("startxref 偏移未指向 xref 表（指向 %r）" % data[off:off + 12])
        except Exception as e:
            problems.append("startxref 解析失败: " + str(e))

    return problems


def check_skipped(path):
    """跳过类样本应保持原样（身份信息仍在，说明未被修改）。"""
    data = open(path, "rb").read()
    problems = []
    if b"\xe4\xbd\x9c\xe8\x80\x85" not in data and "作者".encode("gbk") not in data:
        problems.append("跳过样本被意外修改")
    return problems


def main():
    ok = True
    for name in ["test.pdf", "test_incremental.pdf"]:
        path = os.path.join(DIR, name)
        problems = check_cleaned(path)
        if problems:
            ok = False
            print("[FAIL] %s" % name)
            for p in problems:
                print("       - " + p)
        else:
            print("[ OK ] %s  清理干净、结构有效、正文完整" % name)

    for name in ["test_encrypted.pdf", "test_signed.pdf"]:
        path = os.path.join(DIR, name)
        problems = check_skipped(path)
        if problems:
            ok = False
            print("[FAIL] %s (应跳过)" % name)
            for p in problems:
                print("       - " + p)
        else:
            print("[ OK ] %s  已正确跳过、原文件未改动" % name)

    print("\nRESULT:", "ALL PASS" if ok else "HAS FAILURES")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
