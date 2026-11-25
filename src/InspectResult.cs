using System;
using System.Collections.Generic;

namespace AttriDel
{
    /// <summary>单个非空属性：字段名（中文显示名）→ 值</summary>
    public class PropertyItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public PropertyItem(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>只读预览结果：成功时给出非空属性列表；跳过/失败时给出说明</summary>
    public class InspectResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<PropertyItem> Items { get; set; }

        public InspectResult()
        {
            Items = new List<PropertyItem>();
            Message = string.Empty;
        }

        public static InspectResult Ok(List<PropertyItem> items)
        {
            return new InspectResult { Success = true, Items = items ?? new List<PropertyItem>() };
        }

        public static InspectResult Skip(string message)
        {
            return new InspectResult { Success = false, Message = message };
        }
    }

    /// <summary>字段名 → 中文显示名映射，三类格式共用一套称呼</summary>
    internal static class FieldNames
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 通用 / OOXML core / PDF Info
            { "title", "标题" },
            { "subject", "主题" },
            { "creator", "作者" },        // OOXML core
            { "author", "作者" },         // PDF Info
            { "keywords", "关键词" },
            { "description", "描述" },
            { "lastModifiedBy", "最后修改人" },
            { "revision", "修订次数" },
            { "category", "类别" },
            { "contentStatus", "内容状态" },
            { "lastPrinted", "最后打印时间" },
            { "created", "创建时间" },
            { "CreationDate", "创建时间" },
            { "modified", "修改时间" },
            { "ModDate", "修改时间" },
            { "identifier", "标识符" },
            { "language", "语言" },
            { "version", "版本" },
            // OOXML app
            { "Company", "公司" },
            { "Manager", "经理" },
            { "Application", "生成工具" },
            { "AppVersion", "应用版本" },
            { "Template", "模板" },
            { "TotalTime", "编辑时长" },
            { "Pages", "页数" },
            { "Words", "字数" },
            { "Characters", "字符数" },
            { "Paragraphs", "段落数" },
            { "Lines", "行数" },
            { "CharactersWithSpaces", "含空格字符数" },
            { "SharedDoc", "共享文档" },
            { "HyperlinkBase", "超链接基址" },
            { "PresentationFormat", "演示文稿格式" },
            { "ByteCount", "字节数" },
            { "SlideCount", "幻灯片数" },
            { "NoteCount", "备注数" },
            { "HiddenCount", "隐藏数" },
            { "MultimediaClipCount", "多媒体数" },
            { "ScaleCrop", "缩放裁剪" },
            // PDF Info
            { "Producer", "生成工具" },
            { "Trapped", "陷印" },
            // OLE SummaryInformation / DocSummaryInformation 的标准属性 ID
            { "pid:2", "标题" },
            { "pid:3", "主题" },
            { "pid:4", "作者" },
            { "pid:5", "关键词" },
            { "pid:6", "描述" },
            { "pid:7", "模板" },
            { "pid:8", "最后修改人" },
            { "pid:9", "修订次数" },
            { "pid:10", "编辑时长" },
            { "pid:11", "最后打印时间" },
            { "pid:12", "创建时间" },
            { "pid:13", "修改时间" },
            { "pid:14", "页数" },
            { "pid:15", "字数" },
            { "pid:16", "字符数" },
            { "pid:18", "生成工具" },
            { "pid:d2", "类别" },
            { "pid:d3", "演示文稿格式" },
            { "pid:d4", "字节数" },
            { "pid:d5", "行数" },
            { "pid:d6", "段落数" },
            { "pid:d7", "幻灯片数" },
            { "pid:d8", "备注数" },
            { "pid:d9", "隐藏数" },
            { "pid:d10", "经理" },
            { "pid:d11", "公司" },
            { "pid:d12", "超链接基址" },
            // LibreOffice 转出的 OLE 自定义属性名
            { "OOXMLCorePropertyCategory", "类别" },
            { "OOXMLCorePropertyContentStatus", "内容状态" },
            { "CustomSecret", "自定义字段" }
        };

        public static string Get(string rawName)
        {
            string name;
            if (Map.TryGetValue(rawName, out name)) return name;
            return rawName;   // 未映射的保留原名（如自定义属性名）
        }
    }
}
