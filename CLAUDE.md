# CLAUDE.md — AttriDel 项目规范

## 项目定位

AttriDel 是一个 Windows 桌面小工具，用于清除 Office 文档元数据（身份信息、时间戳、自定义属性等），正文和视觉表现保持原样。程序名 AttriDel。

## 技术约束

- **语言 / 框架**：C#，.NET Framework 4.0（目标 `v4.0`），WinForms 窗口界面。
- **零第三方依赖**：不使用 NuGet 包、不使用 NPOI / Open XML SDK 等外部库。只用 .NET Framework 自带程序集 + Windows 系统 COM 组件。
- **交付形态**：单个 exe，`csc /target:winexe` 编译，Release 配置，无旁车 dll。用户删掉 exe 即干净卸载，不写注册表、不写系统目录。
- **编译器**：`/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe`（沙箱可能拦截，需在编译环节申请放行）。

## 两条清理路径

| 路径 | 格式 | 机制 | 说明 |
|------|------|------|------|
| OOXML | docx / xlsx / pptx | ZIP 包内重写 `docProps/*.xml` | 抹平 ZIP 条目时间戳、移除缩略图、清 rsid |
| OLE 复合文档 | doc / xls / ppt | `IPropertySetStorage.Delete` + 物理扇区擦除 | 删三个属性集，再把释放的空闲扇区/MiniStream 空闲区填零 |
| PDF | pdf | 解析 xref 链 + 对象流解包 + 重建文件 | 清 Info 字段、移除 XMP、重生成 ID；按最新 xref 重建，旧版本对象不写入 |

三条路径对使用者无感：统一走"拖入 → 清单 → 确认 → 报告"，按扩展名自动路由。

另有一条**只读预览路径**：`Cleaner.Inspect(path)` → `InspectResult`（非空属性列表 / 跳过说明），三类格式各实现 `Inspect`，全程不写文件。界面选中文件时展示抓取到的非空属性，加密 / 签名 / 占用文件给出跳过提示。命令行 `--inspect` 把结果写入 dist/inspect.txt 供自动化验证。

## 清理规则

**必清（身份与时间痕迹）**
- 作者、标题、主题、描述、关键词、最后修改人、修订次数、类别、内容状态
- 创建 / 修改 / 最后打印 / 保存时间、编辑时长
- 公司、经理、模板、应用名与版本、页数 / 字数 / 字符数 / 段落数 / 行数
- 全部自定义属性（custom.xml）
- ZIP 条目时间戳抹平到 1980-01-01；缩略图部件及引用；`settings.xml` 的 rsid
- PDF：Info 字典全部字段（含 UTF-16BE hex 字符串、GBK/UTF-8 原始字节）、XMP（`/Metadata` 引用与流对象）、文档 `/ID`、增量更新旧版本对象

**保留（正文原封不动）**
- 批注作者、修订记录作者名、隐藏文字等一切正文内容
- OLE 活跃流字节（正文数据）完全不动
- PDF：页面内容流、字体、图片资源原样复制；注释、表单、JS、附件、超链接、书签、图层不动（由人工处理）

**PDF 关键技术点（踩坑记录）**
- 解析全程在 Latin-1 字符串上进行（0-255 双射，二进制流可无损往返）；ASCII 直引号写语法
- xref 必须跟随 `/Prev` 链，每个对象号取首次遇到的版本（最新），旧版本字节在重建时自然丢弃
- `ReadObject` 内字典与流的偏移是相对整个文件的绝对位置，写入 `PdfObj.Body` 前必须减去对象起始 offset 转成相对位置，否则 `Substring` 越界
- 对象流（type 2 条目）需解压 `/ObjStm`，按 `/N`、`/First` 解包成独立对象；解压失败直接跳过整个文件，不能输出缺页面的 PDF
- 加密（trailer `/Encrypt`）与签名（`/ByteRange`、`/Type /Sig`）必须跳过，重建会破坏字节范围签名
- 重建统一输出经典 xref 表（无 `/Prev`），header 保留原版本号，新 `/ID` 为两组随机 16 字节 hex

**只读预览关键技术点**
- PDF hex 字符串无 BOM 时也要优先按 UTF-16BE 解；用「含 CJK 字符」或「高字节全 0」判据区分 UTF-16BE 与 UTF-8，否则 UTF-8 严格解码会把 UTF-16BE 中文误判为可读乱码
- OLE 的 `PROPSPEC` 联合体在 64 位下按 8 字节对齐，`ulKind` 后须补 4 字节 padding；`STATPROPSTG.lpwstrName` 用 IntPtr 接收再 `PtrToStringUni`，不能用 string 直接封送
- OLE 的 `TotalTime`（pid:10）以 VT_FILETIME 存放但语义是时长，转换为分钟；创建/修改/打印时间（pid:11/12/13）才按 FILETIME 格式化
- `FieldNames` 字典用 OrdinalIgnoreCase，禁止重复键（重复会抛 TypeInitializationException，且异常信息会误导为业务错误）
- 预览用 `FileShare.ReadWrite` 只读打开，与清理路径的独占检测隔离；结果缓存到 `inspectCache`，避免反复选中大文件重复解析

**老格式关键技术点（踩坑记录）**
- `IPropertySetStorage` 的 IID 是 `0000013A-...`，不是 `0000010A`
- `StgOpenStorageEx` 的 `stgfmt` 必须用 `STGFMT_ANY=4`
- 仅 `DeleteMultiple` 删属性项会残留字节；必须 `pss.Delete(fmtid)` 删整个属性集再 Commit，然后用 OleScrubber 清零空闲扇区，才能通过物理字节搜索
- 含 DIFAT 的大文件（>7MB）OleScrubber 保守跳过，只删属性集不擦除

## 边界处理

- 被占用 / 加密 / 带数字签名 / 非支持格式：跳过并记录原因，不中断其余文件。
- 原地覆盖，同目录留 `.bak` 备份。
- 报告统一列出成功 / 跳过 / 失败明细；老格式不适用项标注"不适用"，不报错。

## 交互设计

- **主模式**：窗口拖放区 → 文件清单 + 字段预览 → 确认 → 执行 → 报告。支持"添加文件夹"递归批量。
- **快速模式**：拖文件到 exe 图标，命令行参数直接进入清理（跳过预览），保留应急用途。
- 不做右键菜单集成，避免注册表写入。

## 编码约定

- 中文注释与 UI 文案；代码标识符用英文。
- 字符串用 ASCII 直引号；自然语言用中文全角标点。
- 精准修改，不顺手重构无关代码。
- 密钥 / token 不进代码。
- 每个清理动作都要可验证：改完写测试文档实际跑一遍，检查属性流 / XML / 正文 / 备份。

## 目录结构

```
AttriDel/
  CLAUDE.md         本规范
  ROADMAP.md        进度源
  src/              源码（Program / MainForm / Cleaner / CleanResult / OoxmlCleaner / OleCleaner / OleScrubber / PdfCleaner / UiStyle）
  build/            编译中间产物
  dist/             最终交付 exe
  tests/            测试文档与验证脚本
```
