using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AttriDel
{
    /// <summary>
    /// OOXML 文档清理器（docx / xlsx / pptx）。
    /// OOXML 本质是 ZIP 包，元数据集中在 docProps/*.xml。
    /// 采用"读出全部条目到内存 -> 处理 -> 重建 ZIP"的方式，
    /// 以便完全控制时间戳抹平、缩略图移除和条目顺序。
    /// </summary>
    internal static class OoxmlCleaner
    {
        // core.xml 需要清空的字段（按 localName）
        private static readonly string[] CoreFields =
        {
            "title", "subject", "creator", "keywords", "description",
            "lastModifiedBy", "revision", "category", "contentStatus",
            "lastPrinted", "created", "modified", "identifier", "language", "version"
        };

        // app.xml 需要清空的简单文本字段（按 localName）
        private static readonly string[] AppFields =
        {
            "Company", "Manager", "Application", "AppVersion", "Template",
            "TotalTime", "Pages", "Words", "Characters", "Paragraphs",
            "Lines", "CharactersWithSpaces", "SharedDoc", "HyperlinkBase",
            "PresentationFormat", "ByteCount", "SlideCount", "NoteCount",
            "HiddenCount", "MultimediaClipCount", "ScaleCrop"
        };

        private class ZipItem
        {
            public string Name;
            public byte[] Data;
        }

        public static CleanResult Clean(string path, bool createBackup)
        {
            List<string> cleared = new List<string>();
            string tmp = path + ".attributedel.tmp";

            try
            {
                // 1. 以独占方式读取（占用检测）
                byte[] raw;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    raw = new byte[fs.Length];
                    fs.Read(raw, 0, raw.Length);
                }

                List<ZipItem> items;
                try
                {
                    items = ReadZip(raw);
                }
                catch (InvalidDataException)
                {
                    // 加密的 OOXML 实际是 OLE 容器，不是 ZIP
                    return CleanResult.Skip(path, "文件已加密或不是有效的 OOXML 包");
                }

                // 2. 加密 / 签名检测
                foreach (ZipItem it in items)
                {
                    string n = it.Name.ToLowerInvariant();
                    if (n.StartsWith("encryptioninfo") || n.StartsWith("encryptedpackage"))
                        return CleanResult.Skip(path, "文件已加密，跳过");
                    if (n.StartsWith("_xmlsignatures/") || n.Contains("signature"))
                        return CleanResult.Skip(path, "文档含数字签名，清除元数据会使签名失效，已跳过");
                }

                // 3. 处理各部件
                HashSet<string> removeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                ProcessCore(items, cleared, removeSet);
                ProcessApp(items, cleared, removeSet);
                ProcessCustom(items, cleared, removeSet);
                ProcessSettings(items, cleared);
                ProcessThumbnail(items, cleared, removeSet);
                ProcessRelsAndTypes(items, removeSet);

                // 4. 移除标记为删除的条目
                items.RemoveAll(x => removeSet.Contains(x.Name));

                // 5. 重建 ZIP（时间戳全部抹平到 1980-01-01）
                WriteZip(tmp, items);

                // 6. 结构校验：能重新打开且关键部件在
                if (!ValidateZip(tmp))
                    throw new InvalidDataException("清理后文件结构校验失败");

                // 7. 备份并替换
                CommitReplace(path, tmp, createBackup);

                return CleanResult.Ok(path, "OOXML", cleared);
            }
            catch (IOException ex)
            {
                TryDelete(tmp);
                return CleanResult.Skip(path, "文件被占用或无权限：" + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDelete(tmp);
                return CleanResult.Skip(path, "无权限访问：" + ex.Message);
            }
            catch (Exception ex)
            {
                TryDelete(tmp);
                return CleanResult.Fail(path, ex.Message);
            }
        }

        private static List<ZipItem> ReadZip(byte[] raw)
        {
            List<ZipItem> items = new List<ZipItem>();
            using (MemoryStream ms = new MemoryStream(raw))
            using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    using (Stream s = e.Open())
                    using (MemoryStream buf = new MemoryStream())
                    {
                        s.CopyTo(buf);
                        items.Add(new ZipItem { Name = e.FullName, Data = buf.ToArray() });
                    }
                }
            }
            return items;
        }

        private static void WriteZip(string path, List<ZipItem> items)
        {
            DateTimeOffset epoch = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (ZipItem it in items)
                {
                    ZipArchiveEntry e = zip.CreateEntry(it.Name, CompressionLevel.Optimal);
                    e.LastWriteTime = epoch;
                    using (Stream s = e.Open())
                        s.Write(it.Data, 0, it.Data.Length);
                }
            }
        }

        private static bool ValidateZip(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    return zip.Entries.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static ZipItem Find(List<ZipItem> items, string name)
        {
            foreach (ZipItem it in items)
                if (string.Equals(it.Name, name, StringComparison.OrdinalIgnoreCase))
                    return it;
            return null;
        }

        private static void SaveXml(ZipItem item, XmlDocument doc)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = new UTF8Encoding(false);
                settings.OmitXmlDeclaration = false;
                using (XmlWriter w = XmlWriter.Create(ms, settings))
                    doc.Save(w);
                item.Data = ms.ToArray();
            }
        }

        private static void ProcessCore(List<ZipItem> items, List<string> cleared, HashSet<string> removeSet)
        {
            ZipItem it = Find(items, "docProps/core.xml");
            if (it == null) return;

            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(new MemoryStream(it.Data));

            bool changed = false;
            if (doc.DocumentElement != null)
            {
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element) continue;
                    string ln = node.LocalName;
                    foreach (string f in CoreFields)
                    {
                        if (string.Equals(ln, f, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(node.InnerText))
                            {
                                node.InnerText = string.Empty;
                                cleared.Add("core:" + ln);
                                changed = true;
                            }
                            break;
                        }
                    }
                }
            }
            if (changed) SaveXml(it, doc);
        }

        private static void ProcessApp(List<ZipItem> items, List<string> cleared, HashSet<string> removeSet)
        {
            ZipItem it = Find(items, "docProps/app.xml");
            if (it == null) return;

            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(new MemoryStream(it.Data));

            bool changed = false;
            if (doc.DocumentElement != null)
            {
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element) continue;
                    // 只清没有子元素的简单文本节点，避免破坏 vt:vector 等结构
                    bool hasChildElement = false;
                    foreach (XmlNode c in node.ChildNodes)
                        if (c.NodeType == XmlNodeType.Element) { hasChildElement = true; break; }
                    if (hasChildElement) continue;

                    string ln = node.LocalName;
                    foreach (string f in AppFields)
                    {
                        if (string.Equals(ln, f, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(node.InnerText))
                            {
                                node.InnerText = string.Empty;
                                cleared.Add("app:" + ln);
                                changed = true;
                            }
                            break;
                        }
                    }
                }
            }
            if (changed) SaveXml(it, doc);
        }

        private static void ProcessCustom(List<ZipItem> items, List<string> cleared, HashSet<string> removeSet)
        {
            ZipItem it = Find(items, "docProps/custom.xml");
            if (it == null) return;

            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(new MemoryStream(it.Data));

            int n = 0;
            if (doc.DocumentElement != null)
            {
                List<XmlNode> toRemove = new List<XmlNode>();
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    if (node.NodeType == XmlNodeType.Element) toRemove.Add(node);
                foreach (XmlNode node in toRemove)
                {
                    doc.DocumentElement.RemoveChild(node);
                    n++;
                }
            }
            if (n > 0)
            {
                SaveXml(it, doc);
                cleared.Add("custom:全部自定义属性(" + n + "项)");
            }
        }

        private static void ProcessSettings(List<ZipItem> items, List<string> cleared)
        {
            ZipItem it = Find(items, "word/settings.xml");
            if (it == null) return;   // xlsx / pptx 无此部件

            XmlDocument doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(new MemoryStream(it.Data));

            int rsidCount = 0;
            List<XmlNode> toRemove = new List<XmlNode>();
            foreach (XmlNode node in doc.GetElementsByTagName("rsids", "*"))
                toRemove.Add(node);
            foreach (XmlNode node in toRemove)
            {
                rsidCount += node.ChildNodes.Count;
                if (node.ParentNode != null) node.ParentNode.RemoveChild(node);
            }
            if (rsidCount > 0)
            {
                SaveXml(it, doc);
                cleared.Add("word:rsid(" + rsidCount + "项)");
            }
        }

        private static void ProcessThumbnail(List<ZipItem> items, List<string> cleared, HashSet<string> removeSet)
        {
            foreach (ZipItem it in items)
            {
                if (it.Name.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase))
                {
                    removeSet.Add(it.Name);
                    cleared.Add("缩略图部件");
                }
            }
        }

        private static void ProcessRelsAndTypes(List<ZipItem> items, HashSet<string> removeSet)
        {
            // .rels 中移除 thumbnail / custom 关系
            foreach (ZipItem it in items)
            {
                if (!it.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
                XmlDocument doc = new XmlDocument();
                doc.PreserveWhitespace = true;
                try { doc.Load(new MemoryStream(it.Data)); }
                catch { continue; }

                List<XmlNode> toRemove = new List<XmlNode>();
                foreach (XmlNode node in doc.GetElementsByTagName("Relationship", "*"))
                {
                    if (node.Attributes == null) continue;
                    XmlAttribute type = node.Attributes["Type"];
                    XmlAttribute target = node.Attributes["Target"];
                    string t = type != null ? type.Value : string.Empty;
                    string g = target != null ? target.Value : string.Empty;
                    if (t.EndsWith("/thumbnail", StringComparison.OrdinalIgnoreCase) ||
                        t.EndsWith("/custom-properties", StringComparison.OrdinalIgnoreCase) ||
                        g.IndexOf("thumbnail", StringComparison.OrdinalIgnoreCase) >= 0)
                        toRemove.Add(node);
                }
                if (toRemove.Count > 0)
                {
                    foreach (XmlNode node in toRemove)
                        if (node.ParentNode != null) node.ParentNode.RemoveChild(node);
                    SaveXml(it, doc);
                }
            }

            // [Content_Types].xml 中移除 custom.xml 的 Override
            ZipItem ct = Find(items, "[Content_Types].xml");
            if (ct != null)
            {
                XmlDocument doc = new XmlDocument();
                doc.PreserveWhitespace = true;
                try { doc.Load(new MemoryStream(ct.Data)); }
                catch { return; }
                List<XmlNode> toRemove = new List<XmlNode>();
                foreach (XmlNode node in doc.GetElementsByTagName("Override", "*"))
                {
                    if (node.Attributes == null) continue;
                    XmlAttribute p = node.Attributes["PartName"];
                    if (p != null && p.Value.IndexOf("custom.xml", StringComparison.OrdinalIgnoreCase) >= 0)
                        toRemove.Add(node);
                }
                if (toRemove.Count > 0)
                {
                    foreach (XmlNode node in toRemove)
                        if (node.ParentNode != null) node.ParentNode.RemoveChild(node);
                    SaveXml(ct, doc);
                }
            }
        }

        private static void CommitReplace(string path, string tmp, bool createBackup)
        {
            if (createBackup)
            {
                string bak = path + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Copy(path, bak);
            }
            File.Copy(tmp, path, true);
            TryDelete(tmp);
        }

        /// <summary>只读预览：抓取 core/app/custom 中的非空属性，不修改文件。</summary>
        public static InspectResult Inspect(string path)
        {
            List<PropertyItem> items = new List<PropertyItem>();
            try
            {
                byte[] raw;
                // 预览用只读共享模式：文件被其他程序打开时也能查看属性
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    raw = new byte[fs.Length];
                    int read = 0;
                    while (read < raw.Length)
                    {
                        int n = fs.Read(raw, read, raw.Length - read);
                        if (n == 0) break;
                        read += n;
                    }
                }

                List<ZipItem> zipItems;
                try { zipItems = ReadZip(raw); }
                catch (InvalidDataException)
                {
                    return InspectResult.Skip("文件已加密或不是有效的 OOXML 包");
                }

                foreach (ZipItem it in zipItems)
                {
                    string n = it.Name.ToLowerInvariant();
                    if (n.StartsWith("encryptioninfo") || n.StartsWith("encryptedpackage"))
                        return InspectResult.Skip("文件已加密，无法读取属性");
                }

                InspectCore(zipItems, items);
                InspectApp(zipItems, items);
                InspectCustom(zipItems, items);

                return InspectResult.Ok(items);
            }
            catch (IOException ex)
            {
                return InspectResult.Skip("文件无法读取：" + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return InspectResult.Skip("无权限访问：" + ex.Message);
            }
            catch (Exception ex)
            {
                return InspectResult.Skip("无法读取属性：" + ex.Message);
            }
        }

        private static void InspectCore(List<ZipItem> items, List<PropertyItem> result)
        {
            ZipItem it = Find(items, "docProps/core.xml");
            if (it == null) return;

            XmlDocument doc = new XmlDocument();
            doc.Load(new MemoryStream(it.Data));
            if (doc.DocumentElement == null) return;

            foreach (string f in CoreFields)
            {
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element) continue;
                    if (!string.Equals(node.LocalName, f, StringComparison.OrdinalIgnoreCase)) continue;
                    string v = node.InnerText.Trim();
                    if (v.Length > 0)
                        result.Add(new PropertyItem(FieldNames.Get(f), v));
                    break;
                }
            }
        }

        private static void InspectApp(List<ZipItem> items, List<PropertyItem> result)
        {
            ZipItem it = Find(items, "docProps/app.xml");
            if (it == null) return;

            XmlDocument doc = new XmlDocument();
            doc.Load(new MemoryStream(it.Data));
            if (doc.DocumentElement == null) return;

            foreach (string f in AppFields)
            {
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element) continue;
                    bool hasChildElement = false;
                    foreach (XmlNode c in node.ChildNodes)
                        if (c.NodeType == XmlNodeType.Element) { hasChildElement = true; break; }
                    if (hasChildElement) continue;
                    if (!string.Equals(node.LocalName, f, StringComparison.OrdinalIgnoreCase)) continue;
                    string v = node.InnerText.Trim();
                    if (v.Length > 0)
                        result.Add(new PropertyItem(FieldNames.Get(f), v));
                    break;
                }
            }
        }

        private static void InspectCustom(List<ZipItem> items, List<PropertyItem> result)
        {
            ZipItem it = Find(items, "docProps/custom.xml");
            if (it == null) return;

            XmlDocument doc = new XmlDocument();
            doc.Load(new MemoryStream(it.Data));
            if (doc.DocumentElement == null) return;

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                XmlAttribute nameAttr = node.Attributes["name"];
                string name = nameAttr != null && nameAttr.Value.Trim().Length > 0
                    ? nameAttr.Value.Trim() : "自定义属性";
                string v = string.Empty;
                foreach (XmlNode c in node.ChildNodes)
                {
                    if (c.NodeType == XmlNodeType.Element && c.InnerText.Trim().Length > 0)
                    {
                        v = c.InnerText.Trim();
                        break;
                    }
                }
                if (v.Length > 0)
                    result.Add(new PropertyItem(name, v));
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
