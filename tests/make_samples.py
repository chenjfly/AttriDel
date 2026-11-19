# -*- coding: utf-8 -*-
"""用标准库构造带完整元数据的 OOXML 测试文档，并用 LibreOffice 转出老格式。"""
import os, zipfile, shutil, subprocess

DIR = r"F:\AI Project\AttriDel\tests\samples"
os.makedirs(DIR, exist_ok=True)

CORE = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
<dc:title>测试标题</dc:title><dc:subject>测试主题</dc:subject><dc:creator>作者_测试公司</dc:creator>
<cp:keywords>关键词A,关键词B</cp:keywords><dc:description>这是备注描述</dc:description>
<cp:lastModifiedBy>最后修改人_测试</cp:lastModifiedBy><cp:revision>7</cp:revision>
<dcterms:created xsi:type="dcterms:W3CDTF">2020-01-02T03:04:05Z</dcterms:created>
<dcterms:modified xsi:type="dcterms:W3CDTF">2021-06-07T08:09:10Z</dcterms:modified>
<cp:category>测试类别</cp:category><cp:contentStatus>已发布</cp:contentStatus>
<cp:lastPrinted>2019-05-05T05:05:05Z</cp:lastPrinted></cp:coreProperties>'''

APP_COMMON = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
<Application>Microsoft Office Word</Application><AppVersion>16.0000</AppVersion>
<Company>测试公司有限公司</Company><Manager>经理_测试</Manager><Template>Normal.dotm</Template>
<TotalTime>123</TotalTime><Pages>3</Pages><Words>456</Words><Characters>789</Characters>
<Paragraphs>12</Paragraphs><Lines>34</Lines><CharactersWithSpaces>900</CharactersWithSpaces>
</Properties>'''

CUSTOM = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
<property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="机密字段"><vt:lpwstr>绝密内容</vt:lpwstr></property>
<property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="3" name="项目代号"><vt:lpwstr>PRJ-2020</vt:lpwstr></property>
</Properties>'''

RELS_ROOT = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
<Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>
<Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/thumbnail" Target="docProps/thumbnail.jpeg"/>
</Relationships>'''

CONTENT_TYPES_DOCX = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Default Extension="jpeg" ContentType="image/jpeg"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
<Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>
</Types>'''

DOCUMENT_XML = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
<w:p><w:pPr><w:rsidR w:val="00AA1122"/></w:pPr><w:r><w:t>正文内容测试：这是一段不能被动过的正文。</w:t></w:r></w:p>
<w:p><w:r><w:t>第二行正文。</w:t></w:r></w:p>
</w:body></w:document>'''

SETTINGS_XML = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:rsids><w:rsidRoot w:val="00112233"/><w:rsid w:val="00AA1122"/><w:rsid w:val="00BB3344"/><w:rsid w:val="00CC5566"/></w:rsids>
</w:settings>'''

RELS_DOC = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
</Relationships>'''

# 1x1 JPEG
THUMB = bytes.fromhex(
 'ffd8ffe000104a46494600010100000100010000ffdb004300ffffffffffffffffffffffffffffffff'
 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'
 'ffc00011080001000103011100021101031101ffc4001f0000010501010101010100000000000000'
 '000102030405060708090a0bffda000c03010002110311003f00fefd7fff00ffd9')

def build_docx(path):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        def w(name, data):
            zi = zipfile.ZipInfo(name, date_time=(2022, 3, 4, 5, 6, 8))  # 非 1980 时间
            zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, data.encode('utf-8') if isinstance(data, str) else data)
        w('[Content_Types].xml', CONTENT_TYPES_DOCX)
        w('_rels/.rels', RELS_ROOT)
        w('docProps/core.xml', CORE)
        w('docProps/app.xml', APP_COMMON)
        w('docProps/custom.xml', CUSTOM)
        w('docProps/thumbnail.jpeg', THUMB)
        w('word/document.xml', DOCUMENT_XML)
        w('word/settings.xml', SETTINGS_XML)
        w('word/_rels/document.xml.rels', RELS_DOC)

# xlsx / pptx 用 LibreOffice 从 docx 无法直接转，这里用最小 OOXML 结构 + 相同 docProps
SHEET = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
<row r="1"><c r="A1" t="inlineStr"><is><t>正文数据测试</t></is></c></row>
<row r="2"><c r="A2"><v>12345</v></c></row></sheetData></worksheet>'''
CT_XLSX = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Default Extension="jpeg" ContentType="image/jpeg"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
<Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>
</Types>'''
RELS_ROOT_XLSX = RELS_ROOT.replace('word/document.xml', 'xl/workbook.xml')
WORKBOOK = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'''
RELS_WB = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>'''

def build_xlsx(path):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        def w(name, data):
            zi = zipfile.ZipInfo(name, date_time=(2022, 3, 4, 5, 6, 8))
            zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, data.encode('utf-8') if isinstance(data, str) else data)
        w('[Content_Types].xml', CT_XLSX)
        w('_rels/.rels', RELS_ROOT_XLSX)
        w('docProps/core.xml', CORE)
        w('docProps/app.xml', APP_COMMON)
        w('docProps/custom.xml', CUSTOM)
        w('docProps/thumbnail.jpeg', THUMB)
        w('xl/workbook.xml', WORKBOOK)
        w('xl/_rels/workbook.xml.rels', RELS_WB)
        w('xl/worksheets/sheet1.xml', SHEET)

print("Building OOXML samples...")
build_docx(os.path.join(DIR, "test.docx"))
print("  test.docx")
build_xlsx(os.path.join(DIR, "test.xlsx"))
print("  test.xlsx")

# LibreOffice 从 docx/xlsx 转出 pptx 不合适，单独构造 pptx
SLIDE = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>正文标题测试</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>'''
CT_PPTX = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Default Extension="jpeg" ContentType="image/jpeg"/>
<Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
<Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
<Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>
</Types>'''
PRES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst></p:presentation>'''
RELS_ROOT_PPTX = RELS_ROOT.replace('word/document.xml', 'ppt/presentation.xml')
RELS_PRES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
</Relationships>'''

def build_pptx(path):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        def w(name, data):
            zi = zipfile.ZipInfo(name, date_time=(2022, 3, 4, 5, 6, 8))
            zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, data.encode('utf-8') if isinstance(data, str) else data)
        w('[Content_Types].xml', CT_PPTX)
        w('_rels/.rels', RELS_ROOT_PPTX)
        w('docProps/core.xml', CORE)
        w('docProps/app.xml', APP_COMMON)
        w('docProps/custom.xml', CUSTOM)
        w('docProps/thumbnail.jpeg', THUMB)
        w('ppt/presentation.xml', PRES)
        w('ppt/_rels/presentation.xml.rels', RELS_PRES)
        w('ppt/slides/slide1.xml', SLIDE)

build_pptx(os.path.join(DIR, "test.pptx"))
print("  test.pptx")

# 用 LibreOffice 转出老格式（带元数据一起转换）
print("Converting to legacy formats via LibreOffice...")
soffice = r"C:\Program Files\LibreOffice\program\soffice.exe"
for src, outdir_ext in [("test.docx", "doc"), ("test.xlsx", "xls"), ("test.pptx", "ppt")]:
    srcpath = os.path.join(DIR, src)
    subprocess.run([soffice, "--headless", "--convert-to", outdir_ext, "--outdir", DIR, srcpath],
                   capture_output=True, timeout=120)
print("  done")

print("--- samples ---")
for f in sorted(os.listdir(DIR)):
    print("  %-14s %d bytes" % (f, os.path.getsize(os.path.join(DIR, f))))
