# -*- coding: utf-8 -*-
"""验证 AttriDel 清理效果：OOXML 用 zipfile 查 XML，老格式用 olefile 思路的简易 OLE 属性流扫描。"""
import os, sys, zipfile, re

DIR = r"F:\AI Project\AttriDel\tests\samples"

CORE_KEYS = ["dc:creator", "dc:title", "dc:subject", "cp:keywords", "dc:description",
             "cp:lastModifiedBy", "cp:revision", "cp:category", "cp:contentStatus",
             "dcterms:created", "dcterms:modified", "cp:lastPrinted"]
APP_KEYS = ["Company", "Manager", "Application", "AppVersion", "Template", "TotalTime",
            "Pages", "Words", "Characters", "Paragraphs", "Lines", "CharactersWithSpaces"]

def check_ooxml(path):
    name = os.path.basename(path)
    problems = []
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        # 缩略图应被移除
        thumbs = [n for n in names if "thumbnail" in n.lower()]
        if thumbs:
            problems.append("缩略图仍存在: " + str(thumbs))
        # 时间戳应抹平到 1980
        bad = []
        for info in z.infolist():
            if info.date_time[:3] != (1980, 1, 1):
                bad.append(info.filename + "=" + str(info.date_time))
        if bad:
            problems.append("时间戳未抹平(%d个): %s" % (len(bad), bad[:3]))
        # core.xml
        if "docProps/core.xml" in names:
            core = z.read("docProps/core.xml").decode("utf-8", "ignore")
            for k in CORE_KEYS:
                m = re.search(r"<"+re.escape(k)+r"[^>]*>(.*?)</"+re.escape(k)+r">", core, re.S)
                if m and m.group(1).strip():
                    problems.append("core.xml " + k + " 未清空: " + m.group(1).strip()[:30])
            # rsid 不在 core，另查
        # app.xml
        if "docProps/app.xml" in names:
            app = z.read("docProps/app.xml").decode("utf-8", "ignore")
            for k in APP_KEYS:
                m = re.search(r"<"+re.escape(k)+r"\b[^>]*>(.*?)</"+re.escape(k)+r">", app, re.S)
                if m and m.group(1).strip():
                    problems.append("app.xml " + k + " 未清空: " + m.group(1).strip()[:30])
        # custom.xml 自定义属性
        if "docProps/custom.xml" in names:
            custom = z.read("docProps/custom.xml").decode("utf-8", "ignore")
            if "property" in custom and "<property" in custom:
                problems.append("custom.xml 仍有自定义属性")
        # rsid
        if "word/settings.xml" in names:
            s = z.read("word/settings.xml").decode("utf-8", "ignore")
            if "<w:rsid " in s or "<w:rsidRoot" in s:
                problems.append("settings.xml 仍有 rsid")
        # 正文字段校验
        if "word/document.xml" in names:
            doc = z.read("word/document.xml").decode("utf-8", "ignore")
            if "正文内容测试" not in doc:
                problems.append("正文内容丢失！")
        if "xl/worksheets/sheet1.xml" in names:
            doc = z.read("xl/worksheets/sheet1.xml").decode("utf-8", "ignore")
            if "正文数据测试" not in doc:
                problems.append("正文数据丢失！")
        if "ppt/slides/slide1.xml" in names:
            doc = z.read("ppt/slides/slide1.xml").decode("utf-8", "ignore")
            if "正文标题测试" not in doc:
                problems.append("正文标题丢失！")
    return problems

def check_ole(path):
    """老格式检查：物理字节搜索身份关键词（UTF-8/UTF-16/GBK 全编码）。"""
    name = os.path.basename(path)
    data = open(path, "rb").read()
    problems = []
    for kw in ["作者_测试公司", "测试公司有限公司", "最后修改人_测试", "经理_测试",
               "测试标题", "测试主题", "关键词A", "绝密内容", "PRJ-2020",
               "内容状态", "已发布", "测试类别"]:
        for enc in ["utf-16-le", "utf-8", "gbk"]:
            try:
                pat = kw.encode(enc)
            except Exception:
                continue
            if pat in data:
                problems.append("残留身份信息(%s): %s" % (enc, kw))
                break
    # OLE 头有效性
    if not data[:8].hex().startswith("d0cf11e0"):
        problems.append("OLE 结构损坏")
    return problems

def main():
    success = True
    for f in sorted(os.listdir(DIR)):
        path = os.path.join(DIR, f)
        if f.endswith(".bak") or ".attributedel" in f:
            continue
        ext = os.path.splitext(f)[1].lower()
        if ext in (".docx", ".xlsx", ".pptx"):
            problems = check_ooxml(path)
        elif ext in (".doc", ".xls", ".ppt"):
            problems = check_ole(path)
        else:
            continue
        if problems:
            success = False
            print("[FAIL] %s" % f)
            for p in problems:
                print("       - " + p)
        else:
            print("[ OK ] %s  清理干净" % f)
    print("\nRESULT:", "ALL PASS" if success else "HAS FAILURES")
    return 0 if success else 1

if __name__ == "__main__":
    sys.exit(main())
