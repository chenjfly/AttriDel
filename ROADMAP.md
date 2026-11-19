# ROADMAP.md — AttriDel 进度源

## 当前阶段

阶段一：核心开发（已完成，待验收）

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

## 进行中

- [ ] 飞哥验收 exe 交互（窗口拖放、快速模式）

## 待办

- [ ] 根据实际使用反馈调整（如有）

## 阻塞

- 无

## 最近验证

- 2026-11-19：加号徽章居中——反射加载真实 exe 的 PlusBadge 离屏渲染 400px 放大，圆底中心 (199.5,199.5)、加号中心 (199.5,199.5)，偏移 dx=0 dy=0；目视确认圆头加号上下左右留白均匀。
- 2026-11-19：六种测试文档（docx/xlsx/pptx/doc/xls/ppt）全部 ALL PASS。验证维度：core/app XML 字段清空、custom 属性清空、ZIP 时间戳抹平、缩略图移除、rsid 清除、老格式物理字节搜索（UTF-8/UTF-16/GBK）零残留、OLE 头有效、正文完整。
- 2026-11-19：文字渲染修复后重新编译（33792 字节），确认 manifest 已嵌入 exe（dpiAware/WindowsSettings/Common-Controls 标记均在）；冒烟验证 doc/docx 清理仍干净。
- 2026-11-19：Win11 圆角界面编译（52224 字节），PrintWindow 截图逐项验收：拖放区/列表/详情框圆角卡片、按钮圆角描边、蓝色主按钮白字清晰、圆形加号徽章、状态栏圆点均正常；冒烟验证 doc/docx 清理仍干净。清单状态列着色逻辑随界面改动加入（成功绿/跳过橙/失败红）。
- 关键修复记录：IPropertySetStorage 的 IID 为 0000013A（非 0000010A），StgOpenStorageEx 的 stgfmt 用 STGFMT_ANY=4；删除属性集后需物理擦除扇区才能清除字节残留。
- 关键修复记录：文字锯齿 = 缺 DPI 感知（系统位图拉伸）+ Label 透明背景（ClearType 失效）；修复用 System DPI Aware manifest + Label 不透明背景。
- 关键修复记录：只加 DPI 感知不缩放布局会导致字体按 192 DPI 放大（9pt→约24px）而控件框仍是 96 DPI 像素，文字撑破框重叠、按钮截断。必须让控件 Location/Size/列宽/窗体尺寸同步 ×(DpiX/96)；窗体高度留白要够（650 基准），否则底部状态栏贴边。验收方式：ctypes + PrintWindow(hwnd, mdc, 0) 直接抓窗口像素，不受其他窗口遮挡（SetForegroundWindow 在此环境受前台锁定限制会失效）。
- 关键技术：无图像库时用纯 Python 标准库手写矢量渲染（超采样抗锯齿 + zlib 手写 PNG + ICO 封装）生成图标；徽章同层多半径颜色判断要先判内圈再判外圈，否则外圈纯色覆盖内部。验证 exe 图标用 shell32.ExtractIconEx(EXE) 拿到非空句柄即证明嵌入成功。
- 关键修复记录：自绘控件文字严禁用 TextRenderer.DrawText——它走 GDI ClearType（LCD 亚像素），在自绘背景上混合错误，文字边缘渗出橙(255,188,119)/青(221,255,255)/紫彩色杂点，用户放大截图可见。必须用 GDI+ Graphics.DrawString + TextRenderingHint.AntiAliasGridFit（灰度抗锯齿，零彩色）。注意：离屏 Bitmap 环境下 ClearType 被强制关闭，TextRenderer 不带背景色时统计也是 0，会造成"已修复"假象——必须用带背景色的重载（736 彩色）或真实屏幕/真实按钮渲染来复现问题。验证方法：C# 反射加载 exe 的 RoundedButton，离屏调 OnPaint，逐像素统计 max(r,g,b)-min(r,g,b)>40 的彩色点数。
