# -*- coding: utf-8 -*-
"""验证 AttriDel 的只读属性抓取：检查各格式非空属性字段名与值是否正确解码。"""
import os, sys

DIST = r"F:\AI Project\AttriDel\dist"
EXE = os.path.join(DIST, "AttriDel.exe")
OUT = os.path.join(DIST, "inspect.txt")
SAMPLES = r"F:\AI Project\AttriDel\tests\samples"


def run_inspect(names):
    args = [EXE, "--inspect"] + [os.path.join(SAMPLES, n) for n in names]
    import subprocess
    subprocess.run(args, capture_output=True, timeout=60)
    if not os.path.exists(OUT):
        raise RuntimeError("inspect.txt 未生成")
    with open(OUT, "r", encoding="utf-8") as f:
        return f.read()


def parse_sections(text):
    """把输出按 === 文件名 === 分段，返回 {文件名: {字段: 值}}"""
    result = {}
    current = None
    for line in text.splitlines():
        line = line.strip()
        if line.startswith("===") and line.endswith("==="):
            current = line.strip("= ").split("===")[0].strip()
            result[current] = {}
            continue
        if current and "=" in line:
            k, v = line.split("=", 1)
            result[current][k.strip()] = v.strip()
    return result


def main():
    ok = True
    names = ["test.docx", "test.xlsx", "test.pptx", "test.doc", "test.xls", "test.ppt", "test.pdf",
             "test_incremental.pdf", "test_encrypted.pdf", "test_signed.pdf"]
    sections = parse_sections(run_inspect(names))

    # OOXML 三格式：应有作者、标题、公司、自定义字段等，中文正确
    for name in ["test.docx", "test.xlsx", "test.pptx"]:
        d = sections.get(name, {})
        expect = {
            "标题": "测试标题", "作者": "作者_测试公司", "公司": "测试公司有限公司",
            "经理": "经理_测试", "类别": "测试类别", "内容状态": "已发布", "机密字段": "绝密内容",
        }
        problems = [k + " 缺失或错误" for k, v in expect.items() if d.get(k) != v]
        if len(d) < 20:
            problems.append("属性项过少(%d)" % len(d))
        report(name, problems)
        ok = ok and not problems

    # OLE 三格式：应有中文解码字段、编辑时长为分钟、自定义字段
    for name in ["test.doc", "test.xls", "test.ppt"]:
        d = sections.get(name, {})
        expect = {
            "标题": "测试标题", "作者": "作者_测试公司", "公司": "测试公司有限公司",
            "经理": "经理_测试", "编辑时长": "123 分钟", "机密字段": "绝密内容",
            "创建时间": "2020-01-02 03:04:05",
        }
        problems = [k + " 缺失或错误(实际:" + str(d.get(k)) + ")" for k, v in expect.items() if d.get(k) != v]
        report(name, problems)
        ok = ok and not problems

    # PDF：hex UTF-16BE、GBK 括号串、UTF-8 括号串三种编码都要还原
    d = sections.get("test.pdf", {})
    expect = {
        "标题": "测试标题", "作者": "作者_测试公司", "主题": "测试主题",
        "关键词": "关键词A", "创建者": "生成工具_测试", "生成工具": "测试公司有限公司",
        "公司": "经理_测试", "自定义字段": "绝密字段_PRJ2020",
        "创建时间": "2020-01-02 03:04:05", "修改时间": "2021-06-07 08:09:10",
        "XMP 元数据": "存在，将一并移除",
    }
    problems = [k + " 缺失或错误(实际:" + str(d.get(k)) + ")" for k, v in expect.items() if d.get(k) != v]
    if "文档 ID" not in d:
        problems.append("文档 ID 缺失")
    report("test.pdf", problems)
    ok = ok and not problems

    # 增量更新 PDF：同样应抓到非空属性
    d = sections.get("test_incremental.pdf", {})
    problems = [] if d.get("作者") == "作者_测试公司" else ["作者抓取失败"]
    report("test_incremental.pdf", problems)
    ok = ok and not problems

    # 加密与签名：应给出无法预览提示，且不泄露属性
    for name, keyword in [("test_encrypted.pdf", "加密"), ("test_signed.pdf", "签名")]:
        text = sections.get(name, {})
        raw = run_inspect([name])
        problems = []
        if keyword not in raw:
            problems.append("未给出跳过提示（应含「" + keyword + "」）")
        if "作者_测试公司" in raw:
            problems.append("跳过后仍泄露属性")
        report(name, problems)
        ok = ok and not problems

    print("\nRESULT:", "ALL PASS" if ok else "HAS FAILURES")
    return 0 if ok else 1


def report(name, problems):
    if problems:
        print("[FAIL] %s" % name)
        for p in problems:
            print("       - " + p)
    else:
        print("[ OK ] %s  属性抓取正确" % name)


if __name__ == "__main__":
    sys.exit(main())
