using System;
using System.Collections.Generic;
using System.IO;

namespace AttriDel
{
    /// <summary>统一调度：按扩展名路由到对应清理器</summary>
    internal static class Cleaner
    {
        private static readonly HashSet<string> OoxmlExt =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".xlsx", ".pptx" };

        private static readonly HashSet<string> OleExt =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".doc", ".xls", ".ppt" };

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path);
            return OoxmlExt.Contains(ext) || OleExt.Contains(ext);
        }

        public static string SupportHint
        {
            get { return "支持 docx / xlsx / pptx / doc / xls / ppt"; }
        }

        public static CleanResult Clean(string path, bool createBackup)
        {
            if (!File.Exists(path))
                return CleanResult.Skip(path, "文件不存在");

            string ext = Path.GetExtension(path);
            if (OoxmlExt.Contains(ext))
                return OoxmlCleaner.Clean(path, createBackup);
            if (OleExt.Contains(ext))
                return OleCleaner.Clean(path, createBackup);
            return CleanResult.Skip(path, "不支持的格式：" + ext + "（" + SupportHint + "）");
        }
    }
}
