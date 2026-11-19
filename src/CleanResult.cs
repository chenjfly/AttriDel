using System;
using System.Collections.Generic;

namespace AttriDel
{
    /// <summary>清理状态</summary>
    public enum CleanStatus
    {
        Success,    // 清理成功
        Skipped,    // 跳过（占用、加密、签名、格式不支持等）
        Failed      // 处理失败
    }

    /// <summary>单个文件的清理结果</summary>
    public class CleanResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public CleanStatus Status { get; set; }
        public string Message { get; set; }
        public List<string> ClearedFields { get; set; }
        public string Route { get; set; }   // "OOXML" / "OLE"

        public CleanResult()
        {
            ClearedFields = new List<string>();
        }

        public static CleanResult Ok(string path, string route, List<string> fields)
        {
            return new CleanResult
            {
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                Status = CleanStatus.Success,
                Message = "已清除元数据",
                Route = route,
                ClearedFields = fields ?? new List<string>()
            };
        }

        public static CleanResult Skip(string path, string message)
        {
            return new CleanResult
            {
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                Status = CleanStatus.Skipped,
                Message = message,
                Route = "-"
            };
        }

        public static CleanResult Fail(string path, string message)
        {
            return new CleanResult
            {
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                Status = CleanStatus.Failed,
                Message = message,
                Route = "-"
            };
        }
    }
}
