# ROADMAP.md — AttriDel 进度源

## 当前阶段

阶段一：核心开发（已完成）；PDF 支持扩展（已完成，待验收）

## 已完成

- [x] 需求对齐：属性定义（Office 元数据）、清理范围（全部清空、正文不动）、交互形态（窗口拖放为主 + 拖图标兼容）、支持格式（OOXML + 老格式 OLE）
- [x] 环境探明：.NET Framework 4.0.30319 可用，无 dotnet SDK；编译器需沙箱放行
- [x] 建立项目规范（CLAUDE.md）
- [x] 元数据清理核心库双路径
  - OOXML（docx/xlsx/pptx）：重写 docProps 的 core/app/custom XML、抹平 ZIP 条目时间戳到 1980-01-01、移除缩略图、清 rsid
  - 老格式 OLE（doc/xls/ppt）：IPropertySetStorage.Delete 删除三个属性集 + OleScrubber 物理擦除空闲扇区/MiniStream 空闲区
- [x] WinForms 窗口界面：拖放清单、字段预览、确认执行、结果报告、添加文件夹递归、.bak 备份；命令行快速模式（拖图标）+ --silent 测试开关
- [x] 单文件 exe 编译成功（33792 字节，零第三方依赖）
- [x] 文字渲染 + 高 DPI 布局修复：嵌入 DPI 感知 manifest（build/AttriDel.manifest，System DPI Aware），所有 Label 设不透明背景色启用 ClearType；MainForm 布局坐标按运行时 DPI 等比缩放（_uiScale = DpiX/96），解决 200% 缩放下文字撑破控件框重叠、按钮截断问题。已在 192 DPI 环境截图验收，窗口 1640×1300、控件对齐、状态栏完整。
- [x] 自定义图标设计 + 嵌入：build/make_icon.py 用纯 Python 标准库矢量渲染生成 7 尺寸 ICO（16~256），蓝色圆角底 + 白色文档 + 绿色对勾徽章；build.ps1 加 /win32icon 嵌入，MainForm 设 Icon=ExtractAssociatedIcon。exe 46KB。已用 ExtractIconEx 从 exe 提取图标确认嵌入成功、PrintWindow 截图确认标题栏显示自定义图标。
- [x] Win11 Fluent 风格界面美化：新增 src/UiStyle.cs（UiColors 配色 + RoundRect 圆角几何 + RoundedPanel 自绘圆角卡片 + RoundedButton 自绘按钮含悬停/按下态）；拖放区/文件列表/详情框改圆角卡片包裹，按钮改 Win11 圆角描边（主按钮品牌蓝 #0067C0），拖放区圆形加号徽章+虚线边框，标题加应用图标，状态栏加状态指示圆点，列表状态列按成功/跳过/失败着色，修复拖到子控件无响应。exe 52224 字节。已在 192 DPI 截图验收。
- [x] 按钮文字彩色杂点修复：RoundedButton 原用 TextRenderer.DrawText（GDI ClearType LCD 亚像素），自绘按钮上边缘渗出橙/青/紫彩色杂点（用户截图实测 743 个彩色像素）。改用 GDI+ DrawString + TextRenderingHint.AntiAliasGridFit（纯灰度抗锯齿，绕开 ClearType）。对照实验：TextRenderer+bg=736、DrawString GridFit=0、DrawString AntiAlias=0；真实按钮离屏渲染验证 0 彩色像素。
- [x] 加号徽章居中修复：原用全角字符"＋"（U+FF0B，微软雅黑字形留白不对称不居中）+ 手动 Pt(0,1) 偏移；改为 UiStyle.cs 新增 PlusBadge 自绘控件，圆底与圆头加号在同一 OnPaint 内基于 (Width-1)/2 中心绘制，严格同心。离屏渲染像素验证 dx=0 dy=0。exe 52736 字节。
- [x] 测试验证：六种格式全部通过物理字节级验证（身份信息残留为零），doc/docx 正文完整性用 LibreOffice 转换确认无损，OLE 结构有效

- [x] Git 仓库初始化并推送 GitHub：main 分支首次提交 1a4fdf2（18 文件/2595 行），公开仓库 https://github.com/chenjfly/AttriDel 已建并推送，.gitignore 排除 exe/测试样本/.workbuddy；gh CLI v2.105.0 已装（winget），登录账号 chenjfly。
- [x] PDF 属性清除（第三条路径）：新增 src/PdfCleaner.cs，跟随 xref /Prev 链收集最新对象、解包对象流、清空 Info 字典（UTF-16BE/GBK/UTF-8 编码全覆盖）、移除 XMP /Metadata、重生成 /ID、按最新 xref 重建经典 xref 表丢弃增量更新残留；加密与含数字签名的 PDF 跳过。范围经飞哥确认：注释/表单/JS/附件/超链接/书签/图层由人工处理，程序不动。exe 68608 字节，仍为零第三方依赖单文件。
- [x] PDF 测试样本与验证：tests/make_pdf_samples.py 生成四种样本（基础/增量更新/加密/签名），tests/verify_pdf.py 验证身份字段四编码零残留、XMP 标签清除、旧 ID 消失、增量旧版本字段消失、正文标记保留、startxref 偏移正确指向 xref。全量 10 文件回归：8 成功 2 跳过 0 失败，Office 六格式与 PDF 验证均 ALL PASS。
- [x] 非空属性只读预览：新增 src/InspectResult.cs（PropertyItem / InspectResult / 中文显示名映射），三类格式各实现 Inspect 只读抓取（OOXML 读 docProps、PDF 复用 xref 解析并解码 UTF-16BE/GBK/UTF-8、OLE 用 COM 只读枚举属性集），Cleaner.Inspect 统一分派。界面选中文件即展示抓取到的非空属性（值超 60 字截断、换行压平、按项缓存）；加密/签名/占用文件给出跳过提示。命令行 --inspect 输出到 dist/inspect.txt 供自动化验证。exe 81920 字节。
- [x] 预览验证：tests/verify_inspect.py 检查三类格式共 10 个样本的字段名与中文值（含三种 PDF 编码、OLE 编辑时长转换、加密与签名跳过提示），ALL PASS；清理全量回归 8 成功 2 跳过 0 失败，verify.py / verify_pdf.py 仍 ALL PASS。
- [x] 窗口布局自适应 + 两框可拖动：文件清单与详情框按比例吸收窗口高度变化，底部按钮/状态栏始终贴底，消除放大留白与缩小裁切；两框间新增可拖动分隔条（悬停/拖动蓝色高亮）单独调整高度比例。已用窗口 HWND 矩形验证放大、缩小、拖动、最小窗口四种场景。exe 84480 字节。
- [x] 多选详情改为树形分层：详情框从 TextBox 换成 TreeView，每个选中文件一个顶层节点（文件名 + 属性项数），属性/清理结果作为子节点；多选默认展开首个、其余折叠，避免多文件信息混在一起。自绘节点文字用 GDI+ 灰度抗锯齿。已验证两文件多选的节点数、属性归属与展开状态；清理回归 2/2 成功。exe 86016 字节。
- [x] 文件清单列宽自适应：三列按可用宽度 40% / 40% / 20% 分配，消除宽窗口/全屏时右侧大片空白。DPI 感知实测 1316 / 1556 / 2316px 三种宽度下比例恒定、铺满（仅 8~12px 边框余量），清理回归 2/2 成功。exe 86016 字节。
- [x] 清单框高度按文件行数自适应：默认表头 + 至少 2 行 + 留白，上限 70%，省下的高度给详情框，消除文件少时清单下方大片留空；添加文件后自动重算。手动拖分隔条后锁定用户尺寸，双击分隔条恢复自动。反射验证空列表/2 文件均 160px、详情 480px、多文件封顶 448px（70%）；清理回归 2/2 成功。exe 86528 字节。
- [x] 列表移除文件：选中行按 Delete 或右键「移除选中文件（Delete）」移除，支持多选批量；删除该路径的预览缓存与清理结果，重新加回同一路径必重新解析属性（飞哥明确要求，同名文件内容可能已变）。只改列表不动磁盘文件，状态栏/详情/清单高度同步更新。反射验证 Delete/右键两种入口、缓存与旧结果清零、重新加回重新解析；清理回归 2/2 成功。exe 88064 字节。

## 进行中

- [ ] 飞哥验收 exe 交互（窗口拖放、快速模式、窗口缩放与分隔条拖动）

## 待办

- [ ] 根据实际使用反馈调整（如有）

## 阻塞

- 无

## 最近验证

- 2026-11-25：列表移除验证——反射调用 ListView.OnKeyDown(Delete) 与右键菜单入口：删除后 items 1→0、inspectCache 1→0；注入旧 CleanResult 后菜单删除 items 0、旧结果 0、缓存 0；重新加回同一路径 items 1、缓存 1（重新解析）；`--silent` 清理回归 2 成功 0 失败。临时开关已还原，exe 88064 字节。
- 2026-11-25：清单框行数自适应验证——临时开关反射构造状态：飞哥要求最小行数从 4 改为 2 后，空列表/2 文件均 160px、详情 480px（原来约 420/220，清单大幅收缩、详情翻倍）、手动 50% 为 320/320、双击恢复自动、多文件 448/192（70% 封顶）；添加文件后自动重算生效，`--silent` 清理回归 2 成功 0 失败。临时开关已还原，exe 86528 字节。
- 2026-11-25：列宽自适应验证（DPI 感知模式）——列表物理宽 1316px 时列宽 523/523/262、1556px 时 617/617/310、2316px 时 923/923/462，比例恒定 40/40/20，列宽总和与列表宽度差 8~12px（边框），无右侧留白；`--silent` 清理回归 2 成功 0 失败。
- 2026-11-25：多选树形详情验证——两文件多选生成 2 个文件节点，各显示 26 项属性（28 子节点 = 路径 + 格式 + 26 属性），首节点展开、次节点折叠；清理后结果分支正常；`--silent` 清理回归 2 成功 0 失败。临时测试开关已还原，exe 86016 字节。
- 2026-11-25：窗口布局自适应验证——枚举子窗口 HWND 实测：820×650 初始清单 208px / 详情 90px；窗口 +240 宽 +200 高后清单 339px / 详情 159px、按钮行与状态栏同步下移 200px 贴底；分隔条上拖 60px 清单 339→279px、详情 159→219px；最小窗口 700×560 下按钮与状态栏仍在客户区内。无留白、无遮挡、无重叠。exe 84480 字节。
- 2026-11-25：非空属性预览全量验证——verify_inspect.py 检查 10 个样本：OOXML 三格式各 20+ 字段中文正确、OLE 三格式编辑时长「123 分钟」转换正确、PDF 三种编码（UTF-16BE hex / GBK / UTF-8）中文全部还原、加密与签名给出跳过提示且不泄露属性，ALL PASS。清理回归 8 成功 2 跳过 0 失败，三套验证脚本全绿。exe 81920 字节。
- 2026-11-25：修复预览解析三处关键 bug——PDF hex 无 BOM 时 UTF-8 严格解码把 UTF-16BE 中文误判为可读乱码（加 CJK 判据优先 UTF-16BE）；OLE `PROPSPEC` 联合体 64 位下少算 4 字节 padding、`STATPROPSTG.lpwstrName` 用 string 封送失败（改 IntPtr）；FieldNames 字典重复键导致 TypeInitializationException（去重）。
- 2026-11-25：PDF 支持全量回归——重新生成六种 Office + 四种 PDF 样本，快速模式一次清理 10 个文件：成功 8（doc/docx/xls/xlsx/ppt/pptx/pdf/test_incremental.pdf）、跳过 2（加密/签名 PDF）、失败 0；verify.py 与 verify_pdf.py 全部 ALL PASS。exe 编译 ExitCode=0，68608 字节。
- 2026-11-25：修复 PDF 解析关键 bug——`ReadObject` 内字典与流的偏移原本是相对整个文件的绝对位置，直接用于 `obj.Body.Substring` 导致越界异常（"索引和长度必须引用该字符串内的位置"）；改为写入 Body 前减去对象起始 offset。修复后基础与增量样本由失败转为成功。
- 2026-11-19：加号徽章居中——反射加载真实 exe 的 PlusBadge 离屏渲染 400px 放大，圆底中心 (199.5,199.5)、加号中心 (199.5,199.5)，偏移 dx=0 dy=0；目视确认圆头加号上下左右留白均匀。
- 2026-11-19：六种测试文档（docx/xlsx/pptx/doc/xls/ppt）全部 ALL PASS。验证维度：core/app XML 字段清空、custom 属性清空、ZIP 时间戳抹平、缩略图移除、rsid 清除、老格式物理字节搜索（UTF-8/UTF-16/GBK）零残留、OLE 头有效、正文完整。
- 2026-11-19：文字渲染修复后重新编译（33792 字节），确认 manifest 已嵌入 exe（dpiAware/WindowsSettings/Common-Controls 标记均在）；冒烟验证 doc/docx 清理仍干净。
- 2026-11-19：Win11 圆角界面编译（52224 字节），PrintWindow 截图逐项验收：拖放区/列表/详情框圆角卡片、按钮圆角描边、蓝色主按钮白字清晰、圆形加号徽章、状态栏圆点均正常；冒烟验证 doc/docx 清理仍干净。清单状态列着色逻辑随界面改动加入（成功绿/跳过橙/失败红）。
- 关键修复记录：IPropertySetStorage 的 IID 为 0000013A（非 0000010A），StgOpenStorageEx 的 stgfmt 用 STGFMT_ANY=4；删除属性集后需物理擦除扇区才能清除字节残留。
- 关键修复记录：文字锯齿 = 缺 DPI 感知（系统位图拉伸）+ Label 透明背景（ClearType 失效）；修复用 System DPI Aware manifest + Label 不透明背景。
- 关键修复记录：只加 DPI 感知不缩放布局会导致字体按 192 DPI 放大（9pt→约24px）而控件框仍是 96 DPI 像素，文字撑破框重叠、按钮截断。必须让控件 Location/Size/列宽/窗体尺寸同步 ×(DpiX/96)；窗体高度留白要够（650 基准），否则底部状态栏贴边。验收方式：ctypes + PrintWindow(hwnd, mdc, 0) 直接抓窗口像素，不受其他窗口遮挡（SetForegroundWindow 在此环境受前台锁定限制会失效）。
- 关键技术：无图像库时用纯 Python 标准库手写矢量渲染（超采样抗锯齿 + zlib 手写 PNG + ICO 封装）生成图标；徽章同层多半径颜色判断要先判内圈再判外圈，否则外圈纯色覆盖内部。验证 exe 图标用 shell32.ExtractIconEx(EXE) 拿到非空句柄即证明嵌入成功。
- 关键修复记录：自绘控件文字严禁用 TextRenderer.DrawText——它走 GDI ClearType（LCD 亚像素），在自绘背景上混合错误，文字边缘渗出橙(255,188,119)/青(221,255,255)/紫彩色杂点，用户放大截图可见。必须用 GDI+ Graphics.DrawString + TextRenderingHint.AntiAliasGridFit（灰度抗锯齿，零彩色）。注意：离屏 Bitmap 环境下 ClearType 被强制关闭，TextRenderer 不带背景色时统计也是 0，会造成"已修复"假象——必须用带背景色的重载（736 彩色）或真实屏幕/真实按钮渲染来复现问题。验证方法：C# 反射加载 exe 的 RoundedButton，离屏调 OnPaint，逐像素统计 max(r,g,b)-min(r,g,b)>40 的彩色点数。
