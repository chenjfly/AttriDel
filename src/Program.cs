using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace AttriDel
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 命令行模式：拖文件到 exe 图标上时，Windows 把路径作为参数传入
            if (args != null && args.Length > 0)
            {
                // --silent 开关供自动化测试使用，不弹窗
                bool silent = false;
                List<string> files = new List<string>();
                foreach (string a in args)
                {
                    if (a == "--silent") silent = true;
                    else files.Add(a);
                }
                RunQuickMode(files.ToArray(), silent);
                return;
            }

            Application.Run(new MainForm());
        }

        private static void RunQuickMode(string[] args, bool silent)
        {
            List<CleanResult> results = new List<CleanResult>();
            foreach (string arg in args)
            {
                string path = arg.Trim('"');
                if (!Cleaner.IsSupported(path))
                {
                    results.Add(CleanResult.Skip(path, "不支持的格式"));
                    continue;
                }
                results.Add(Cleaner.Clean(path, true));
            }

            StringBuilder sb = new StringBuilder();
            int ok = 0, skip = 0, fail = 0;
            foreach (CleanResult r in results)
            {
                if (r.Status == CleanStatus.Success) ok++;
                else if (r.Status == CleanStatus.Skipped) skip++;
                else fail++;
            }
            sb.AppendLine("AttriDel 清理完成");
            sb.AppendLine("成功 " + ok + " 个，跳过 " + skip + " 个，失败 " + fail + " 个");
            sb.AppendLine();
            foreach (CleanResult r in results)
            {
                string mark = r.Status == CleanStatus.Success ? "[成功] " :
                              (r.Status == CleanStatus.Skipped ? "[跳过] " : "[失败] ");
                sb.AppendLine(mark + r.FileName + " — " + r.Message);
            }

            if (silent)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result.txt"),
                    sb.ToString());
            }
            else
            {
                MessageBox.Show(sb.ToString(), "AttriDel 处理结果",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
