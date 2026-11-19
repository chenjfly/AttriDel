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
        private TextBox detailBox;
        private CheckBox backupCheck;
        private Label statusLabel;
        private Label statusDot;
        private RoundedPanel dropZone;
        private RoundedButton runButton;
        private List<CleanResult> lastResults;

        // 运行时屏幕 DPI / 96。控件坐标按 96 DPI 基准硬编码，据此等比缩放，
        // 与 DPI 感知下字体的真实像素渲染保持一致，避免文字撑破控件框。
        private float _uiScale = 1f;

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

            Label subtitle = new Label();
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
            RoundedPanel listCard = new RoundedPanel();
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
            listCard.Controls.Add(fileList);

            // 详情框：圆角卡片包裹无边框 TextBox
            RoundedPanel detailCard = new RoundedPanel();
            detailCard.Location = Pt(20, 402);
            detailCard.Size = Sz(780, 110);
            detailCard.Radius = Scale(8);
            detailCard.BackColor = UiColors.Card;
            detailCard.Dashed = false;
            this.Controls.Add(detailCard);

            detailBox = new TextBox();
            detailBox.Location = Pt(12, 10);
            detailBox.Size = Sz(756, 90);
            detailBox.Multiline = true;
            detailBox.ReadOnly = true;
            detailBox.ScrollBars = ScrollBars.Vertical;
            detailBox.BorderStyle = BorderStyle.None;
            detailBox.BackColor = UiColors.Card;
            detailBox.ForeColor = UiColors.Text;
            detailBox.Text = "选中文件可查看处理详情。";
            detailCard.Controls.Add(detailBox);

            // 底部控制
            RoundedButton addFile = new RoundedButton();
            addFile.Text = "添加文件";
            addFile.Location = Pt(20, 528);
            addFile.Size = Sz(104, 36);
            addFile.Radius = Scale(6);
            addFile.Click += AddFile_Click;
            this.Controls.Add(addFile);

            RoundedButton addFolder = new RoundedButton();
            addFolder.Text = "添加文件夹";
            addFolder.Location = Pt(134, 528);
            addFolder.Size = Sz(116, 36);
            addFolder.Radius = Scale(6);
            addFolder.Click += AddFolder_Click;
            this.Controls.Add(addFolder);

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
            dlg.Filter = "Office 文档|*.docx;*.xlsx;*.pptx;*.doc;*.xls;*.ppt|所有文件|*.*";
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
            if (fileList.SelectedItems.Count == 0) return;
            string path = fileList.SelectedItems[0].Tag as string;
            if (path == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("文件：" + Path.GetFileName(path));
            sb.AppendLine("路径：" + path);
            sb.AppendLine("格式：" + Path.GetExtension(path).ToUpperInvariant());

            if (lastResults != null)
            {
                foreach (CleanResult r in lastResults)
                {
                    if (string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("状态：" + StatusText(r.Status));
                        sb.AppendLine("说明：" + r.Message);
                        sb.AppendLine("路由：" + r.Route);
                        if (r.ClearedFields.Count > 0)
                        {
                            sb.AppendLine("清除的字段：");
                            foreach (string f in r.ClearedFields)
                                sb.AppendLine("  · " + f);
                        }
                        detailBox.Text = sb.ToString();
                        return;
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("将清除：作者、标题、主题、关键词、描述、最后修改人、修订次数、类别、内容状态、创建/修改/打印时间、公司、经理、模板、应用信息、字数统计、自定义属性");
            sb.AppendLine("并抹平 ZIP 时间戳、移除缩略图、清除 rsid；正文不动");
            detailBox.Text = sb.ToString();
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
