using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AttriDel
{
    internal class MainForm : Form
    {
        private ListView fileList;
        private ContextMenuStrip listMenu;
        private ToolStripMenuItem removeMenuItem;
        private TreeView detailTree;
        private RoundedPanel listCard;
        private RoundedPanel detailCard;
        private Panel splitter;
        private RoundedButton addBtn;
        private RoundedButton folderBtn;
        private CheckBox backupCheck;
        private Label statusLabel;
        private Label statusDot;
        private RoundedPanel dropZone;
        private Label subtitle;
        private RoundedButton runButton;
        private List<CleanResult> lastResults;
        // 属性预览缓存：同一文件只解析一次，避免反复选中大文件卡顿
        private readonly Dictionary<string, InspectResult> inspectCache =
            new Dictionary<string, InspectResult>(StringComparer.OrdinalIgnoreCase);

        // 运行时屏幕 DPI / 96。控件坐标按 96 DPI 基准硬编码，据此等比缩放，
        // 与 DPI 感知下字体的真实像素渲染保持一致，避免文字撑破控件框。
        private float _uiScale = 1f;

        // 基准窗口的客户区尺寸，用于把窗口缩放量转成内容区的伸缩量。
        private int _baseClientW;
        private int _baseClientH;
        // 文件清单占两个内容框总高度的比重，拖动分隔条时更新。
        private float _listRatio = 210f / 320f;
        // 默认按实际文件行数自动决定清单框高度；手动拖过分隔条后改为用户尺寸，双击恢复自动。
        private bool _autoListHeight = true;
        private bool _splitDragging;
        private bool _splitHover;
        // 文件节点用加粗字体突出层级，缓存避免每次重绘都创建 Font。
        private readonly Font _treeFileFont = new Font("Microsoft YaHei", 9F, FontStyle.Bold);

        public MainForm()
        {
            Initialize();
        }

        private int Scale(int v) { return (int)Math.Round(v * _uiScale); }
        private Point Pt(int x, int y) { return new Point(Scale(x), Scale(y)); }
        private Size Sz(int w, int h) { return new Size(Scale(w), Scale(h)); }

        private void Initialize()
        {
            // DPI 感知进程里 DpiX 为屏幕真实 DPI；若 manifest 未生效则为 96。
            using (Graphics g = this.CreateGraphics())
                _uiScale = g.DpiX / 96f;

            this.Text = "AttriDel — 文档属性清除工具";
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.Size = Sz(820, 650);
            _baseClientW = this.ClientSize.Width;
            _baseClientH = this.ClientSize.Height;
            this.MinimumSize = Sz(700, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UiColors.WindowBg;
            this.Font = new Font("Microsoft YaHei", 9F);

            // 标题区：应用图标 + 标题 + 副标题
            PictureBox appIcon = new PictureBox();
            using (Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                appIcon.Image = ico.ToBitmap();
            appIcon.Location = Pt(20, 12);
            appIcon.Size = Sz(30, 30);
            appIcon.SizeMode = PictureBoxSizeMode.Zoom;
            appIcon.BackColor = UiColors.WindowBg;
            this.Controls.Add(appIcon);

            Label title = new Label();
            title.Text = "AttriDel";
            title.Font = new Font("Microsoft YaHei", 18F, FontStyle.Bold);
            title.ForeColor = UiColors.Accent;
            title.BackColor = UiColors.WindowBg;
            title.Location = Pt(58, 10);
            title.Size = Sz(200, 34);
            this.Controls.Add(title);

            subtitle = new Label();
            subtitle.Text = "拖入文档，清除作者、公司、时间等属性，正文原封不动";
            subtitle.ForeColor = UiColors.TextSecondary;
            subtitle.BackColor = UiColors.WindowBg;
            subtitle.Location = Pt(22, 48);
            subtitle.Size = Sz(600, 20);
            this.Controls.Add(subtitle);

            // 拖放区：圆角卡片 + 圆形加号徽章
            dropZone = new RoundedPanel();
            dropZone.Location = Pt(20, 78);
            dropZone.Size = Sz(780, 92);
            dropZone.Radius = Scale(10);
            dropZone.BackColor = UiColors.Card;
            dropZone.BorderColor = UiColors.DropBorder;
            dropZone.Dashed = true;
            this.Controls.Add(dropZone);

            PlusBadge badge = new PlusBadge();
            badge.Location = Pt(20, 22);
            badge.Size = Sz(48, 48);
            dropZone.Controls.Add(badge);

            Label dropText = new Label();
            dropText.Text = "把文件拖到这里";
            dropText.Font = new Font("Microsoft YaHei", 12F);
            dropText.ForeColor = UiColors.Text;
            dropText.BackColor = UiColors.Card;
            dropText.Location = Pt(84, 24);
            dropText.Size = Sz(300, 26);
            dropZone.Controls.Add(dropText);

            Label dropHint = new Label();
            dropHint.Text = Cleaner.SupportHint + "，可多选";
            dropHint.ForeColor = UiColors.TextMuted;
            dropHint.BackColor = UiColors.Card;
            dropHint.Location = Pt(84, 52);
            dropHint.Size = Sz(500, 20);
            dropZone.Controls.Add(dropHint);

            BindDrop(dropZone);
            foreach (Control c in dropZone.Controls)
            {
                BindDrop(c);
                if (c is RoundedPanel)
                    foreach (Control sub in c.Controls)
                        BindDrop(sub);
            }

            // 文件清单：圆角卡片包裹 ListView
            listCard = new RoundedPanel();
            listCard.Location = Pt(20, 182);
            listCard.Size = Sz(780, 210);
            listCard.Radius = Scale(8);
            listCard.BackColor = UiColors.Card;
            listCard.Dashed = false;
            this.Controls.Add(listCard);

            fileList = new ListView();
            fileList.Location = Pt(1, 1);
            fileList.Size = Sz(778, 208);
            fileList.View = View.Details;
            fileList.FullRowSelect = true;
            fileList.MultiSelect = true;
            fileList.GridLines = false;
            fileList.BorderStyle = BorderStyle.None;
            fileList.BackColor = UiColors.Card;
            fileList.Columns.Add("文件", Scale(360));
            fileList.Columns.Add("路径", Scale(300));
            fileList.Columns.Add("状态", Scale(100));
            fileList.SelectedIndexChanged += FileList_SelectedIndexChanged;
            fileList.KeyDown += FileList_KeyDown;
            listCard.Controls.Add(fileList);

            // 右键菜单与 Delete 键都能移除选中文件，只改列表不动磁盘文件。
            listMenu = new ContextMenuStrip();
            listMenu.Font = this.Font;
            removeMenuItem = new ToolStripMenuItem("移除选中文件（Delete）");
            removeMenuItem.Click += RemoveMenuItem_Click;
            listMenu.Items.Add(removeMenuItem);
            listMenu.Opening += ListMenu_Opening;
            fileList.ContextMenuStrip = listMenu;

            // 两个内容框之间的可拖动分隔条：上下拖动可单独调整两框高度。
            splitter = new Panel();
            splitter.Cursor = Cursors.SizeNS;
            splitter.BackColor = UiColors.WindowBg;
            splitter.Paint += Splitter_Paint;
            splitter.MouseEnter += Splitter_MouseEnter;
            splitter.MouseLeave += Splitter_MouseLeave;
            splitter.MouseDown += Splitter_MouseDown;
            splitter.MouseMove += Splitter_MouseMove;
            splitter.MouseUp += Splitter_MouseUp;
            splitter.DoubleClick += Splitter_DoubleClick;
            this.Controls.Add(splitter);

            detailCard = new RoundedPanel();
            detailCard.Location = Pt(20, 402);
            detailCard.Size = Sz(780, 110);
            detailCard.Radius = Scale(8);
            detailCard.BackColor = UiColors.Card;
            detailCard.Dashed = false;
            this.Controls.Add(detailCard);

            // 详情框：每个选中文件一个顶层节点，属性作为子节点，避免多文件信息混在一起。
            detailTree = new TreeView();
            detailTree.Location = Pt(12, 10);
            detailTree.Size = Sz(756, 90);
            detailTree.BorderStyle = BorderStyle.None;
            detailTree.BackColor = UiColors.Card;
            detailTree.ForeColor = UiColors.Text;
            detailTree.HideSelection = true;
            detailTree.ShowLines = true;
            detailTree.LineColor = UiColors.CardBorder;
            detailTree.Indent = Scale(18);
            detailTree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            detailTree.DrawNode += DetailTree_DrawNode;
            detailCard.Controls.Add(detailTree);

            // 底部控制
            addBtn = new RoundedButton();
            addBtn.Text = "添加文件";
            addBtn.Location = Pt(20, 528);
            addBtn.Size = Sz(104, 36);
            addBtn.Radius = Scale(6);
            addBtn.Click += AddFile_Click;
            this.Controls.Add(addBtn);

            folderBtn = new RoundedButton();
            folderBtn.Text = "添加文件夹";
            folderBtn.Location = Pt(134, 528);
            folderBtn.Size = Sz(116, 36);
            folderBtn.Radius = Scale(6);
            folderBtn.Click += AddFolder_Click;
            this.Controls.Add(folderBtn);

            backupCheck = new CheckBox();
            backupCheck.Text = "保留 .bak 备份";
            backupCheck.Checked = true;
            backupCheck.ForeColor = UiColors.Text;
            backupCheck.BackColor = UiColors.WindowBg;
            backupCheck.Location = Pt(265, 534);
            backupCheck.Size = Sz(140, 24);
            this.Controls.Add(backupCheck);

            runButton = new RoundedButton();
            runButton.Text = "开始清除";
            runButton.Location = Pt(660, 528);
            runButton.Size = Sz(140, 36);
            runButton.Radius = Scale(6);
            runButton.Primary = true;
            runButton.Click += RunButton_Click;
            this.Controls.Add(runButton);

            // 状态栏：指示圆点 + 文字
            statusDot = new Label();
            statusDot.Text = "●";
            statusDot.Font = new Font("Microsoft YaHei", 9F);
            statusDot.ForeColor = UiColors.TextMuted;
            statusDot.BackColor = UiColors.WindowBg;
            statusDot.Location = Pt(20, 575);
            statusDot.Size = Sz(16, 18);
            this.Controls.Add(statusDot);

            statusLabel = new Label();
            statusLabel.Text = "就绪";
            statusLabel.ForeColor = UiColors.TextSecondary;
            statusLabel.BackColor = UiColors.WindowBg;
            statusLabel.Location = Pt(36, 575);
            statusLabel.Size = Sz(760, 18);
            this.Controls.Add(statusLabel);

            this.Resize += MainForm_Resize;
            ApplyLayout();
        }

        /// <summary>
        /// 按当前客户区尺寸重排：顶部标题/拖放区、底部按钮/状态栏位置固定贴底，
        /// 中间的文件清单与详情框吸收窗口高度变化；分隔条只改两框的高度分配。
        /// </summary>
        private void ApplyLayout()
        {
            int cw = this.ClientSize.Width;
            int ch = this.ClientSize.Height;
            int deltaH = ch - _baseClientH;
            int deltaW = cw - _baseClientW;

            // 顶部区域：宽度随窗口伸缩，高度固定。
            dropZone.Size = new Size(Scale(780) + deltaW, Scale(92));
            subtitle.Size = new Size(Scale(600) + deltaW, Scale(20));

            // 底部按钮行与状态栏贴底，不随窗口变高而下沉、变矮而被裁掉。
            int buttonY = Scale(528) + deltaH;
            int statusY = Scale(575) + deltaH;
            LayoutBottomButtons(buttonY, cw);
            statusDot.Location = new Point(Scale(20), statusY);
            statusLabel.Location = new Point(Scale(36), statusY);
            statusLabel.Size = new Size(cw - Scale(56), Scale(18));

            // 内容区：清单 + 分隔条 + 详情框，铺满顶部与按钮行之间的空间。
            int listTop = Scale(182);
            int gap = Scale(10);
            int splitterH = Scale(10);
            int bottomGap = Scale(16);
            int detailBottom = buttonY - bottomGap;
            int contentH = detailBottom - listTop - gap;

            int minList = Scale(80);
            int minDetail = Scale(80);
            if (contentH < minList + minDetail)
            {
                // 窗口被拖到极小时，优先保住两个框的可读高度。
                minList = contentH / 2;
                minDetail = contentH - minList;
            }
            int listH;
            if (_autoListHeight)
            {
                // 清单框按行数收缩：表头 + 至少 2 行 + 少量留白，上限 70%，
                // 省下的高度留给详情框，避免文件少时下方空一大片。
                int rowH = Scale(20);
                int ideal = Scale(24) + Math.Max(2, fileList.Items.Count) * rowH + Scale(12);
                int cap = Math.Min((int)(contentH * 0.7), contentH - minDetail);
                listH = Math.Min(ideal, cap);
            }
            else
            {
                listH = (int)Math.Round(contentH * _listRatio);
            }
            if (listH < minList) listH = minList;
            if (listH > contentH - minDetail) listH = contentH - minDetail;
            int detailH = contentH - listH;
            int cardW = Scale(780) + deltaW;

            listCard.Location = new Point(Scale(20), listTop);
            listCard.Size = new Size(cardW, listH);
            fileList.Size = new Size(cardW - Scale(2), listH - Scale(2));

            // 列宽随卡片宽度按 40% / 40% / 20% 分配，避免宽窗口右侧空出大片区域。
            int innerW = fileList.ClientSize.Width - Scale(4);
            int nameW = (int)(innerW * 0.4);
            int pathW = (int)(innerW * 0.4);
            fileList.Columns[0].Width = nameW;
            fileList.Columns[1].Width = pathW;
            fileList.Columns[2].Width = innerW - nameW - pathW;

            splitter.Location = new Point(Scale(20), listTop + listH);
            splitter.Size = new Size(cardW, splitterH);

            detailCard.Location = new Point(Scale(20), listTop + listH + gap);
            detailCard.Size = new Size(cardW, detailH);
            detailTree.Size = new Size(cardW - Scale(24), detailH - Scale(20));
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            ApplyLayout();
        }

        private void Splitter_Paint(object sender, PaintEventArgs e)
        {
            // 居中一条可抓取的细横线，悬停/拖动时高亮成主题蓝。
            int y = splitter.Height / 2;
            Color c = (_splitDragging || _splitHover) ? UiColors.Accent : UiColors.CardBorder;
            using (Pen pen = new Pen(c, Math.Max(1f, _uiScale * 1.5f)))
                e.Graphics.DrawLine(pen, Scale(8), y, splitter.Width - Scale(8), y);
        }

        private void Splitter_MouseEnter(object sender, EventArgs e)
        {
            _splitHover = true;
            splitter.Invalidate();
        }

        private void Splitter_MouseLeave(object sender, EventArgs e)
        {
            if (!_splitDragging)
            {
                _splitHover = false;
                splitter.Invalidate();
            }
        }

        private void Splitter_DoubleClick(object sender, EventArgs e)
        {
            _autoListHeight = true;
            ApplyLayout();
        }

        private void Splitter_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _autoListHeight = false;
            _splitDragging = true;
            splitter.Capture = true;
            splitter.Invalidate();
        }

        private void Splitter_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_splitDragging) return;
            int listTop = Scale(182);
            int gap = Scale(10);
            int splitterH = Scale(10);
            int buttonY = Scale(528) + (this.ClientSize.Height - _baseClientH);
            int contentH = buttonY - Scale(16) - listTop - gap;
            int minList = Scale(80), minDetail = Scale(80);
            if (contentH < minList + minDetail)
            {
                minList = contentH / 2;
                minDetail = contentH - minList;
            }

            // 鼠标当前位置即分隔条中心，换算成清单框的目标高度。
            int mouseY = this.PointToClient(Cursor.Position).Y;
            int listH = mouseY - listTop - splitterH / 2;
            if (listH < minList) listH = minList;
            if (listH > contentH - minDetail) listH = contentH - minDetail;
            _listRatio = (float)listH / contentH;
            ApplyLayout();
        }

        private void Splitter_MouseUp(object sender, MouseEventArgs e)
        {
            _splitDragging = false;
            splitter.Capture = false;
            splitter.Invalidate();
        }

        // 按钮集中在同一处做水平锚定，避免各处重复算右边距。
        private void LayoutBottomButtons(int y, int cw)
        {
            if (addBtn == null) return;
            addBtn.Location = new Point(Scale(20), y);
            folderBtn.Location = new Point(Scale(134), y);
            backupCheck.Location = new Point(Scale(265), y + Scale(6));
            runButton.Location = new Point(cw - Scale(20) - Scale(140), y);
        }

        private void BindDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += Drop_DragEnter;
            c.DragLeave += Drop_DragLeave;
            c.DragDrop += Drop_DragDrop;
        }

        private void SetDropActive(bool active)
        {
            dropZone.BackColor = active ? UiColors.DropActive : UiColors.Card;
            dropZone.BorderColor = active ? UiColors.Accent : UiColors.DropBorder;
            foreach (Control c in dropZone.Controls)
            {
                if (c is Label) c.BackColor = dropZone.BackColor;
            }
        }

        private void SetStatus(string text, Color dot)
        {
            statusLabel.Text = text;
            statusDot.ForeColor = dot;
        }

        private void Drop_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                SetDropActive(true);
            }
            else
                e.Effect = DragDropEffects.None;
        }

        private void Drop_DragLeave(object sender, EventArgs e)
        {
            // 只有真正离开整个拖放区才复原，避免移入子控件时闪烁。
            Point p = dropZone.PointToClient(Cursor.Position);
            if (dropZone.ClientRectangle.Contains(p)) return;
            SetDropActive(false);
        }

        private void Drop_DragDrop(object sender, DragEventArgs e)
        {
            SetDropActive(false);
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddPaths(paths);
        }

        private void AddFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Filter = "文档|*.docx;*.xlsx;*.pptx;*.doc;*.xls;*.ppt;*.pdf|所有文件|*.*";
            if (dlg.ShowDialog() == DialogResult.OK)
                AddPaths(dlg.FileNames);
        }

        private void AddFolder_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "选择文件夹（递归添加其中的文档）";
            if (dlg.ShowDialog() == DialogResult.OK)
                AddPaths(new string[] { dlg.SelectedPath });
        }

        private void AddPaths(string[] paths)
        {
            int added = 0;
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (string f in Directory.GetFiles(p, "*.*", SearchOption.AllDirectories))
                    {
                        if (Cleaner.IsSupported(f)) { AddOne(f); added++; }
                    }
                }
                else if (File.Exists(p) && Cleaner.IsSupported(p))
                {
                    AddOne(p);
                    added++;
                }
            }
            SetStatus("已添加 " + added + " 个文件，共 " + fileList.Items.Count + " 个",
                added > 0 ? UiColors.Accent : UiColors.TextMuted);
            if (added > 0 && _autoListHeight) ApplyLayout();
        }

        private void AddOne(string path)
        {
            foreach (ListViewItem item in fileList.Items)
                if (string.Equals(item.SubItems[1].Text, path, StringComparison.OrdinalIgnoreCase))
                    return;   // 去重

            ListViewItem row = new ListViewItem(Path.GetFileName(path));
            row.SubItems.Add(path);
            row.SubItems.Add("待处理");
            row.Tag = path;
            fileList.Items.Add(row);
        }

        private void FileList_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshDetail();
        }

        private void FileList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && fileList.SelectedItems.Count > 0)
            {
                e.Handled = true;
                RemoveSelected();
            }
        }

        private void ListMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            removeMenuItem.Enabled = fileList.SelectedItems.Count > 0;
        }

        private void RemoveMenuItem_Click(object sender, EventArgs e)
        {
            RemoveSelected();
        }

        /// <summary>
        /// 从列表移除选中文件，同时清掉这些路径的预览缓存和清理结果，
        /// 重新加回同一路径时必定重新解析属性，避免同名文件内容已变却显示旧值。
        /// </summary>
        private void RemoveSelected()
        {
            if (fileList.SelectedItems.Count == 0) return;

            ListViewItem[] selected = new ListViewItem[fileList.SelectedItems.Count];
            fileList.SelectedItems.CopyTo(selected, 0);
            HashSet<string> removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem item in selected)
            {
                string path = item.Tag as string;
                if (path != null) removed.Add(path);
                fileList.Items.Remove(item);
            }

            foreach (string path in removed)
                inspectCache.Remove(path);
            if (lastResults != null)
                lastResults.RemoveAll(r => removed.Contains(r.FilePath));

            SetStatus("已移除 " + selected.Length + " 个文件，共 " + fileList.Items.Count + " 个",
                UiColors.TextMuted);
            RefreshDetail();
            if (_autoListHeight) ApplyLayout();
        }

        /// <summary>
        /// 把当前选中的每个文件建成一个顶层节点，属性/结果作为子节点。
        /// 多选时默认只展开第一个文件，避免一次性刷屏。
        /// </summary>
        private void RefreshDetail()
        {
            detailTree.Nodes.Clear();
            if (fileList.SelectedItems.Count == 0)
            {
                detailTree.Nodes.Add("选中文件可查看处理详情。");
                return;
            }

            int index = 1;
            foreach (ListViewItem item in fileList.SelectedItems)
            {
                string path = item.Tag as string;
                if (path == null) continue;

                TreeNode fileNode = new TreeNode();
                string name = Path.GetFileName(path);
                fileNode.Nodes.Add("路径：" + Shorten(path, 100));
                fileNode.Nodes.Add("格式：" + Path.GetExtension(path).ToUpperInvariant());

                CleanResult result = FindResult(path);
                if (result != null)
                {
                    fileNode.Text = index + ". " + name + "  ·  " + StatusText(result.Status);
                    fileNode.Nodes.Add("说明：" + result.Message);
                    fileNode.Nodes.Add("路由：" + result.Route);
                    if (result.ClearedFields.Count > 0)
                    {
                        TreeNode fields = new TreeNode("清除的字段（" + result.ClearedFields.Count + " 项）：");
                        foreach (string f in result.ClearedFields)
                            fields.Nodes.Add(f);
                        fileNode.Nodes.Add(fields);
                    }
                }
                else
                {
                    InspectResult inspect;
                    if (!inspectCache.TryGetValue(path, out inspect))
                    {
                        inspect = Cleaner.Inspect(path);
                        inspectCache[path] = inspect;
                    }

                    if (!inspect.Success)
                    {
                        fileNode.Text = index + ". " + name + "  ·  无法预览";
                        fileNode.Nodes.Add("无法预览：" + inspect.Message);
                    }
                    else if (inspect.Items.Count == 0)
                    {
                        fileNode.Text = index + ". " + name + "  ·  0 项";
                        fileNode.Nodes.Add("未检测到可清除的属性。");
                    }
                    else
                    {
                        fileNode.Text = index + ". " + name + "  ·  " + inspect.Items.Count + " 项";
                        foreach (PropertyItem p in inspect.Items)
                        {
                            string v = Shorten(p.Value, 60).Replace("\r", " ").Replace("\n", " ");
                            fileNode.Nodes.Add(p.Name + "：" + v);
                        }
                    }
                }

                detailTree.Nodes.Add(fileNode);
                index++;
            }

            if (detailTree.Nodes.Count > 0)
                detailTree.Nodes[0].Expand();
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }

        private CleanResult FindResult(string path)
        {
            if (lastResults == null) return null;
            foreach (CleanResult r in lastResults)
                if (string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
        }

        private void DetailTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;
            bool isFile = e.Node.Level == 0;
            Font font = isFile ? _treeFileFont : this.Font;
            Color color = isFile ? UiColors.Accent : UiColors.Text;

            // 与自绘按钮同理：用 GDI+ 灰度抗锯齿，不用 TextRenderer 的 ClearType。
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush br = new SolidBrush(color))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                e.Graphics.DrawString(e.Node.Text, font, br, e.Bounds, sf);
            }
        }

        private static string StatusText(CleanStatus s)
        {
            if (s == CleanStatus.Success) return "成功";
            if (s == CleanStatus.Skipped) return "跳过";
            return "失败";
        }

        private void RunButton_Click(object sender, EventArgs e)
        {
            if (fileList.Items.Count == 0)
            {
                MessageBox.Show("请先添加文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "将对 " + fileList.Items.Count + " 个文件清除元数据。\n" +
                "正文不会被修改" + (backupCheck.Checked ? "，并保留 .bak 备份。" : "（未勾选备份，不可撤销）。"),
                "确认清除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            runButton.Enabled = false;
            lastResults = new List<CleanResult>();
            SetStatus("正在清除…", UiColors.Accent);
            this.Cursor = Cursors.WaitCursor;
            Application.DoEvents();

            foreach (ListViewItem item in fileList.Items)
            {
                string path = item.Tag as string;
                CleanResult r = Cleaner.Clean(path, backupCheck.Checked);
                lastResults.Add(r);
                item.SubItems[2].Text = StatusText(r.Status) + " — " + r.Message;
                item.SubItems[2].ForeColor = r.Status == CleanStatus.Success ? UiColors.Success
                    : r.Status == CleanStatus.Skipped ? UiColors.Warning : UiColors.Danger;
                Application.DoEvents();
            }

            this.Cursor = Cursors.Default;
            runButton.Enabled = true;

            int ok = 0, skip = 0, fail = 0;
            foreach (CleanResult r in lastResults)
            {
                if (r.Status == CleanStatus.Success) ok++;
                else if (r.Status == CleanStatus.Skipped) skip++;
                else fail++;
            }
            SetStatus("完成：成功 " + ok + "，跳过 " + skip + "，失败 " + fail,
                fail > 0 ? UiColors.Danger : (skip > 0 ? UiColors.Warning : UiColors.Success));
            // 选中项没变，选择事件不会重触发，这里手动把详情树切到清理结果。
            RefreshDetail();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("清除完成");
            sb.AppendLine("成功 " + ok + " 个，跳过 " + skip + " 个，失败 " + fail + " 个");
            if (backupCheck.Checked)
                sb.AppendLine("原文件已备份为同名 .bak");
            MessageBox.Show(sb.ToString(), "AttriDel 处理结果",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
