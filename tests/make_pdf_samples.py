# -*- coding: utf-8 -*-
"""用标准库构造带完整元数据的 PDF 测试样本：经典 xref + XMP + 增量更新。

中文身份字段按 PDF 规范写成三种编码，覆盖验证脚本的 UTF-8 / UTF-16BE / GBK 搜索：
  - UTF-16BE hex 字符串  <FEFF...>
  - GBK 原始字节括号字符串
  - UTF-8 原始字节括号字符串
"""
import os

DIR = r"F:\AI Project\AttriDel\tests\samples"
os.makedirs(DIR, exist_ok=True)

LATIN = "latin-1"


def L(s):
    return s.encode(LATIN)


def pdf_utf16be(s):
    return "<" + s.encode("utf-16-be").hex().upper() + ">"


def pdf_raw(s, enc):
    return "(" + s.encode(enc).decode(LATIN) + ")"


XMP_UTF8 = """<?xpacket begin="\ufeff" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
<rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:xmp="http://ns.adobe.com/xap/1.0/">
<dc:creator><rdf:Seq><rdf:li>作者_测试公司</rdf:li></rdf:Seq></dc:creator>
<dc:title><rdf:Alt><rdf:li xml:lang="x-default">测试标题</rdf:li></rdf:Alt></dc:title>
<xmp:CreatorTool>生成工具_测试</xmp:CreatorTool>
<dc:description>这是XMP备注描述</dc:description>
</rdf:Description></rdf:RDF></x:xmpmeta>
<?xpacket end="w"?>
"""
XMP_UTF16 = XMP_UTF8.encode("utf-16-be")

CONTENT = b"BT /F1 12 Tf 72 720 Td (PDF-CONTENT-MARKER-KEEP-THIS) Tj ET"


def build_basic(path, with_incremental=False, extra_info=""):
    """经典 xref 表 + Info 字典 + Catalog /Metadata + 一页正文。"""
    objs = {}

    objs[1] = b"<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>"
    objs[2] = b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>"
    objs[3] = b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 6 0 R >> >> /Contents 4 0 R >>"
    objs[4] = L("<< /Length %d >>\nstream\n" % len(CONTENT)) + CONTENT + L("\nendstream")
    xmp_bytes = XMP_UTF8.encode("utf-8") + XMP_UTF16
    objs[5] = L("<< /Type /Metadata /Subtype /XML /Length %d >>\nstream\n" % len(xmp_bytes)) + xmp_bytes + L("\nendstream")
    objs[6] = b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"

    info = ("<< /Title " + pdf_utf16be("测试标题") +
            " /Author " + pdf_raw("作者_测试公司", "gbk") +
            " /Subject " + pdf_utf16be("测试主题") +
            " /Keywords " + pdf_raw("关键词A", "utf-8") +
            " /Creator " + pdf_utf16be("生成工具_测试") +
            " /Producer " + pdf_raw("测试公司有限公司", "gbk") +
            " /CreationDate (D:20200102030405) /ModDate (D:20210607080910) "
            " /Company " + pdf_utf16be("经理_测试") +
            " /CustomSecret " + pdf_raw("绝密字段_PRJ2020", "gbk") + extra_info + " >>")
    objs[7] = L(info)

    out = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = {}
    for num in sorted(objs):
        offsets[num] = len(out)
        out += ("%d 0 obj\n" % num).encode("ascii") + objs[num] + b"\nendobj\n"

    xref_off = len(out)
    maxnum = max(objs)
    out += ("xref\n0 %d\n" % (maxnum + 1)).encode("ascii")
    out += b"0000000000 65535 f \n"
    for i in range(1, maxnum + 1):
        out += ("%010d 00000 n \n" % offsets[i]).encode("ascii")
    out += L("trailer\n<< /Size %d /Root 1 0 R /Info 7 0 R "
             "/ID [<0102030405060708090A0B0C0D0E0F10> <1112131415161718191A1B1C1D1E1F20>] >>\n"
             % (maxnum + 1))
    out += ("startxref\n%d\n%%%%EOF\n" % xref_off).encode("ascii")

    if with_incremental:
        # 增量更新：新版本 Info 对象（对象 7），旧版本字节仍留在文件前半部分
        new_info = L("<< /Title " + pdf_utf16be("二次修改标题") + " /Author " + pdf_utf16be("作者_测试公司") + " >>")
        inc_off = len(out)
        out += b"7 0 obj\n" + new_info + b"\nendobj\n"
        inc_xref = len(out)
        out += b"xref\n7 1\n%010d 00000 n \n" % inc_off
        out += L("trailer\n<< /Size %d /Root 1 0 R /Info 7 0 R /Prev %d "
                 "/ID [<A1A2A3A4A5A6A7A8A9AAABACADAEAFB0> <B1B2B3B4B5B6B7B8B9BABBBCBDBEBFC0>] >>\n"
                 % (maxnum + 1, xref_off))
        out += ("startxref\n%d\n%%%%EOF\n" % inc_xref).encode("ascii")

    with open(path, "wb") as f:
        f.write(out)


def build_encrypted(path):
    build_basic(path)
    with open(path, "rb") as f:
        data = f.read()
    data = data.replace(b"/Root 1 0 R /Info 7 0 R", b"/Root 1 0 R /Info 7 0 R /Encrypt 8 0 R")
    with open(path, "wb") as f:
        f.write(data)


def build_signed(path):
    """构造时就把签名字典写进 Info 对象，避免事后插入破坏 xref 偏移。"""
    build_basic(path, extra_info=" /SigTest << /Type /Sig /ByteRange [0 10 20 30] >>")


build_basic(os.path.join(DIR, "test.pdf"))
print("  test.pdf")
build_basic(os.path.join(DIR, "test_incremental.pdf"), with_incremental=True)
print("  test_incremental.pdf")
build_encrypted(os.path.join(DIR, "test_encrypted.pdf"))
print("  test_encrypted.pdf")
build_signed(os.path.join(DIR, "test_signed.pdf"))
print("  test_signed.pdf")
