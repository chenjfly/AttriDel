using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace AttriDel
{
    /// <summary>
    /// PDF 清理器。只清元数据，不动页面内容：
    /// 1) 清空 trailer /Info 指向对象的全部字典字段；
    /// 2) 移除所有对象里的 /Metadata（XMP）引用并丢弃其流对象；
    /// 3) 重建文档 /ID 为随机值；
    /// 4) 按最新 xref 链重建文件，增量更新中的旧版本对象不写入，残留字节自然消失。
    /// 页面内容流、字体、图片资源原样复制。加密与含数字签名的文件跳过。
    /// 整个解析在 Latin-1 字符串上进行（0-255 双射，二进制流无损往返）。
    /// </summary>
    internal static class PdfCleaner
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private class SkipException : Exception
        {
            public SkipException(string msg) : base(msg) { }
        }

        private class PdfVal
        {
            public string Kind;          // num / ref / name / str / hex / array / dict / other
            public long Number;
            public int RefNum, RefGen;
            public string Name;
            public List<long> Array;
            public int Start, End;       // 在对象 Body 内的范围
        }

        private class PdfObj
        {
            public int Num;
            public int Gen;
            public string Body;          // "N G obj ... endobj"
            public int DictStart = -1;   // Body 内 '<<' 位置
            public int DictEnd = -1;     // Body 内匹配 '>>' 之后
            public bool HasStream;
            public int StreamStart = -1;
            public int StreamEnd = -1;
            public Dictionary<string, PdfVal> Dict = new Dictionary<string, PdfVal>();
        }

        private class XrefEntry
        {
            public int Type;             // 0 free / 1 in-use offset / 2 in-use objstm
            public long F2;
            public int F3;
            public XrefEntry(int t, long f2, int f3) { Type = t; F2 = f2; F3 = f3; }
        }

        private class XrefSection
        {
            public Dictionary<int, XrefEntry> Entries = new Dictionary<int, XrefEntry>();
            public string TrailerDict = string.Empty;
            public int StreamObjNum = -1;
        }

        private class Lexer
        {
            public string S;
            public int I;

            public Lexer(string s, int i) { S = s; I = i; }

            public static bool IsWs(char c)
            {
                return c == 0 || c == 9 || c == 10 || c == 12 || c == 13 || c == 32;
            }

            public static bool IsDelim(char c)
            {
                return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' ||
                       c == '{' || c == '}' || c == '/' || c == '%';
            }

            public void SkipWs()
            {
                while (I < S.Length && IsWs(S[I])) I++;
                while (I < S.Length && S[I] == '%')
                {
                    while (I < S.Length && S[I] != '\r' && S[I] != '\n') I++;
                    while (I < S.Length && IsWs(S[I])) I++;
                }
            }

            public string ReadName()
            {
                if (I >= S.Length || S[I] != '/') return string.Empty;
                I++;
                int st = I;
                while (I < S.Length && !IsWs(S[I]) && !IsDelim(S[I])) I++;
                return "/" + S.Substring(st, I - st);
            }

            public string ReadToken()
            {
                SkipWs();
                int st = I;
                while (I < S.Length && !IsWs(S[I]) && !IsDelim(S[I])) I++;
                if (st == I && I < S.Length) { I++; return S.Substring(st, 1); }
                return S.Substring(st, I - st);
            }

            public long ReadInt()
            {
                SkipWs();
                int st = I;
                if (I < S.Length && (S[I] == '+' || S[I] == '-')) I++;
                while (I < S.Length && S[I] >= '0' && S[I] <= '9') I++;
                if (st == I) return 0;
                long v;
                return long.TryParse(S.Substring(st, I - st), out v) ? v : 0;
            }

            public void ReadLiteralString()
            {
                if (I >= S.Length || S[I] != '(') return;
                int depth = 0;
                while (I < S.Length)
                {
                    char c = S[I++];
                    if (c == '\\') { if (I < S.Length) I++; continue; }
                    if (c == '(') depth++;
                    else if (c == ')') { depth--; if (depth == 0) return; }
                }
            }

            public void SkipNestedDict()
            {
                if (I + 1 >= S.Length || S[I] != '<' || S[I + 1] != '<') return;
                int depth = 0;
                while (I < S.Length)
                {
                    if (I + 1 < S.Length && S[I] == '<' && S[I + 1] == '<') { depth++; I += 2; continue; }
                    if (I + 1 < S.Length && S[I] == '>' && S[I + 1] == '>') { depth--; I += 2; if (depth == 0) return; continue; }
                    if (S[I] == '(') { ReadLiteralString(); continue; }
                    I++;
                }
            }

            public PdfVal ReadValue()
            {
                SkipWs();
                if (I >= S.Length) return new PdfVal { Kind = "other" };
                char c = S[I];
                if (c == '/')
                    return new PdfVal { Kind = "name", Name = ReadName() };
                if (c == '(')
                {
                    ReadLiteralString();
                    return new PdfVal { Kind = "str" };
                }
                if (c == '<' && I + 1 < S.Length && S[I + 1] == '<')
                {
                    SkipNestedDict();
                    return new PdfVal { Kind = "dict" };
                }
                if (c == '<')
                {
                    I++;
                    while (I < S.Length && S[I] != '>') I++;
                    if (I < S.Length) I++;
                    return new PdfVal { Kind = "hex" };
                }
                if (c == '[')
                {
                    I++;
                    PdfVal arr = new PdfVal { Kind = "array", Array = new List<long>() };
                    while (I < S.Length && S[I] != ']')
                    {
                        char d = S[I];
                        if (d == '(') { ReadLiteralString(); continue; }
                        if (d == '<') { if (I + 1 < S.Length && S[I + 1] == '<') SkipNestedDict(); else { I++; while (I < S.Length && S[I] != '>') I++; if (I < S.Length) I++; } continue; }
                        if (d == '/') { ReadName(); continue; }
                        if ((d >= '0' && d <= '9') || d == '-' || d == '+')
                        {
                            long n = ReadInt();
                            int save = I;
                            long g = ReadInt();
                            SkipWs();
                            if (I < S.Length && S[I] == 'R') { I++; }
                            else I = save;
                            arr.Array.Add(n);
                            continue;
                        }
                        I++;
                    }
                    if (I < S.Length) I++;
                    return arr;
                }
                if ((c >= '0' && c <= '9') || c == '-' || c == '+')
                {
                    long n = ReadInt();
                    int save = I;
                    SkipWs();
                    if (I < S.Length && S[I] >= '0' && S[I] <= '9')
                    {
                        long g = ReadInt();
                        SkipWs();
                        if (I < S.Length && S[I] == 'R')
                        {
                            I++;
                            return new PdfVal { Kind = "ref", RefNum = (int)n, RefGen = (int)g };
                        }
                    }
                    I = save;
                    return new PdfVal { Kind = "num", Number = n };
                }
                ReadToken();
                return new PdfVal { Kind = "other" };
            }
        }

        /// <summary>只读预览：抓取 Info 字典非空字段、XMP、文档 ID，解码中文后返回，不修改文件。</summary>
        public static InspectResult Inspect(string path)
        {
            List<PropertyItem> items = new List<PropertyItem>();
            try
            {
                byte[] raw;
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

                string pdf = Latin1.GetString(raw);
                if (!pdf.StartsWith("%PDF-", StringComparison.Ordinal))
                    return InspectResult.Skip("不是有效的 PDF 文件");

                Dictionary<int, XrefEntry> entries = new Dictionary<int, XrefEntry>();
                XrefSection rootSection;
                try
                {
                    rootSection = FollowXrefChain(pdf, entries);
                }
                catch
                {
                    return InspectResult.Skip("PDF 结构异常，无法读取属性");
                }

                if (rootSection.TrailerDict.IndexOf("/Encrypt", StringComparison.OrdinalIgnoreCase) >= 0)
                    return InspectResult.Skip("PDF 已加密，无法读取属性");

                // 读取 Info 对象
                int infoNum = -1;
                Match im = Regex.Match(rootSection.TrailerDict, @"/Info\s+(\d+)\s+(\d+)\s+R", RegexOptions.IgnoreCase);
                if (im.Success) infoNum = int.Parse(im.Groups[1].Value);
                XrefEntry infoEntry;
                if (infoNum >= 0 && entries.TryGetValue(infoNum, out infoEntry) && infoEntry.Type == 1)
                {
                    PdfObj info = ReadObject(pdf, (int)infoEntry.F2);
                    if (info != null && info.DictStart >= 0)
                    {
                        int lo = info.DictStart;
                        int hi = info.DictEnd > lo ? info.DictEnd : info.Body.Length;
                        string dictText = info.Body.Substring(lo, hi - lo);
                        CollectPdfFields(dictText, items);
                    }
                }

                // XMP 元数据：扫描所有对象字典
                bool hasXmp = false;
                foreach (KeyValuePair<int, XrefEntry> kv in entries)
                {
                    if (kv.Value.Type != 1) continue;
                    PdfObj obj = ReadObject(pdf, (int)kv.Value.F2);
                    if (obj == null || obj.DictStart < 0) continue;
                    int lo = obj.DictStart;
                    int hi = obj.DictEnd > lo ? obj.DictEnd : obj.Body.Length;
                    if (Regex.IsMatch(obj.Body.Substring(lo, hi - lo), @"/Metadata\s+\d+\s+\d+\s+R", RegexOptions.IgnoreCase))
                    { hasXmp = true; break; }
                }
                if (hasXmp) items.Add(new PropertyItem("XMP 元数据", "存在，将一并移除"));

                // 文档 ID
                Match idm = Regex.Match(rootSection.TrailerDict, @"/ID\s*\[\s*<([0-9A-Fa-f]+)>", RegexOptions.IgnoreCase);
                if (idm.Success)
                {
                    string id = idm.Groups[1].Value.ToUpperInvariant();
                    if (id.Length > 16) id = id.Substring(0, 8) + "…" + id.Substring(id.Length - 8);
                    items.Add(new PropertyItem("文档 ID", id));
                }

                // 签名检测（与清理路径保持一致）
                foreach (KeyValuePair<int, XrefEntry> kv in entries)
                {
                    if (kv.Value.Type != 1) continue;
                    PdfObj obj = ReadObject(pdf, (int)kv.Value.F2);
                    if (obj != null && Regex.IsMatch(obj.Body, @"/ByteRange\b|/Type\s*/Sig\b", RegexOptions.IgnoreCase))
                        return InspectResult.Skip("PDF 含数字签名，属性不可安全读取");
                }

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

        /// <summary>从 Info 字典文本中按字段取值并解码：hex(UTF-16BE) / 括号串(GBK、UTF-8)</summary>
        private static void CollectPdfFields(string dictText, List<PropertyItem> items)
        {
            // 逐个字段名扫描，值要么是 <hex> 要么是 (literal)
            MatchCollection matches = Regex.Matches(dictText,
                @"/([A-Za-z][\w]*)\s*(<([0-9A-Fa-f\s]*)>|\(([^()]*)\))", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                string rawName = m.Groups[1].Value;
                string value;
                if (m.Groups[3].Success)      // hex 字符串
                    value = DecodePdfHex(m.Groups[3].Value);
                else                          // 括号字符串
                    value = DecodePdfLiteral(m.Groups[4].Value);

                if (value == null || value.Trim().Length == 0) continue;
                // 日期规范化：D:YYYYMMDDHHmmSS -> YYYY-MM-DD HH:mm:ss
                value = NormalizePdfDate(value.Trim());
                // PDF 的 Creator 指创建应用，与 OOXML core 的作者语义不同
                string display = rawName.Equals("Creator", StringComparison.OrdinalIgnoreCase)
                    ? "创建者" : FieldNames.Get(rawName);
                items.Add(new PropertyItem(display, value));
            }
        }

        private static string DecodePdfHex(string hex)
        {
            string h = Regex.Replace(hex, @"\s", string.Empty);
            if (h.Length < 2 || h.Length % 2 != 0) return string.Empty;
            byte[] bytes = new byte[h.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);

            // UTF-16BE：有 BOM 去掉 BOM，无 BOM 也优先尝试（PDF 中文 hex 串绝大多数是 UTF-16BE）
            byte[] body = bytes;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                body = new byte[bytes.Length - 2];
                Array.Copy(bytes, 2, body, 0, body.Length);
            }
            if (body.Length >= 2 && body.Length % 2 == 0)
            {
                try
                {
                    string u = Encoding.BigEndianUnicode.GetString(body);
                    if (IsReadable(u) && LooksLikeUtf16(body)) return u;
                }
                catch { }
            }
            return DecodeBest(bytes);
        }

        private static string DecodePdfLiteral(string literal)
        {
            byte[] bytes = new byte[literal.Length];
            for (int i = 0; i < literal.Length; i++)
                bytes[i] = (byte)literal[i];   // Latin-1 往返
            return DecodeBest(bytes);
        }

        /// <summary>按 UTF-8 → GBK → UTF-16BE → Latin-1 依次尝试，取第一个可解码且非乱码的结果</summary>
        private static string DecodeBest(byte[] bytes)
        {
            if (bytes.Length == 0) return string.Empty;
            Encoding[] candidates = new Encoding[]
            {
                new UTF8Encoding(false, true),
                Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                new UnicodeEncoding(true, false, true)
            };
            foreach (Encoding enc in candidates)
            {
                try
                {
                    string s = enc.GetString(bytes);
                    if (IsReadable(s)) return s;
                }
                catch { }
            }
            // 兜底：Latin-1 单字节映射，至少不抛异常
            return Latin1.GetString(bytes);
        }

        /// <summary>UTF-16BE 特征：中文场景下奇数位字节多为 0（ASCII）或低位非零（汉字）</summary>
        private static bool LooksLikeUtf16(byte[] bytes)
        {
            if (bytes.Length < 4) return false;
            int zeroHigh = 0;
            for (int i = 0; i < bytes.Length - 1; i += 2)
                if (bytes[i] == 0) zeroHigh++;
            // 纯中文 UTF-16BE 的高字节不为 0；含 ASCII 时高字节为 0
            // 关键判据：解出的字符串含 CJK 字符，或高字节全为 0（纯 ASCII）
            string s = Encoding.BigEndianUnicode.GetString(bytes);
            int cjk = 0;
            foreach (char c in s) if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
            return cjk > 0 || zeroHigh * 2 >= bytes.Length / 2;
        }

        /// <summary>判断解码结果是否可读（控制字符比例低、无大量替换符）</summary>
        private static bool IsReadable(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int bad = 0;
            foreach (char c in s)
                if (c == '\uFFFD' || (c < 32 && c != '\r' && c != '\n' && c != '\t')) bad++;
            return bad * 2 < s.Length;
        }

        private static string NormalizePdfDate(string value)
        {
            Match m = Regex.Match(value, @"^D?:?(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})");
            if (!m.Success) return value;
            return m.Groups[1].Value + "-" + m.Groups[2].Value + "-" + m.Groups[3].Value + " " +
                   m.Groups[4].Value + ":" + m.Groups[5].Value + ":" + m.Groups[6].Value;
        }

        public static CleanResult Clean(string path, bool createBackup)
        {
            List<string> cleared = new List<string>();
            string tmp = path + ".attributedel.tmp";

            try
            {
                byte[] raw;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
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

                string pdf = Latin1.GetString(raw);
                if (!pdf.StartsWith("%PDF-", StringComparison.Ordinal))
                    throw new SkipException("不是有效的 PDF 文件（缺少 %PDF 头）");

                // 1. 跟随 xref /Prev 链，收集当前有效的对象条目（每个对象号取最新版本）
                Dictionary<int, XrefEntry> entries = new Dictionary<int, XrefEntry>();
                XrefSection rootSection = FollowXrefChain(pdf, entries);

                if (rootSection.TrailerDict.IndexOf("/Encrypt", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new SkipException("PDF 已加密，跳过");

                // 2. 解析 xref stream 对象（如有），用于对象流解包
                Dictionary<int, PdfObj> objects = new Dictionary<int, PdfObj>();
                HashSet<int> excluded = new HashSet<int>();
                if (rootSection.StreamObjNum >= 0) excluded.Add(rootSection.StreamObjNum);

                // 3. 解包对象流：type 2 条目 -> 独立对象
                UnpackObjectStreams(pdf, entries, objects, excluded);

                // 4. 读取普通 in-use 对象的原始字节
                foreach (KeyValuePair<int, XrefEntry> kv in entries)
                {
                    if (kv.Value.Type != 1 || objects.ContainsKey(kv.Key) || excluded.Contains(kv.Key)) continue;
                    PdfObj obj = ReadObject(pdf, (int)kv.Value.F2);
                    if (obj != null && obj.Num == kv.Key) objects[kv.Key] = obj;
                }

                if (objects.Count == 0)
                    throw new InvalidDataException("未解析到任何有效对象");

                // 5. 签名检测（重建会破坏字节范围签名）
                foreach (PdfObj obj in objects.Values)
                {
                    if (Regex.IsMatch(obj.Body, @"/ByteRange\b|/Type\s*/Sig\b", RegexOptions.IgnoreCase))
                        throw new SkipException("PDF 含数字签名，清除元数据会使签名失效，已跳过");
                }

                // 6. 清理 Info 字典
                int infoNum = -1;
                Match im = Regex.Match(rootSection.TrailerDict, @"/Info\s+(\d+)\s+(\d+)\s+R", RegexOptions.IgnoreCase);
                if (im.Success) infoNum = int.Parse(im.Groups[1].Value);
                if (infoNum >= 0 && objects.ContainsKey(infoNum))
                {
                    PdfObj info = objects[infoNum];
                    int n = CountDictKeys(info);
                    if (n > 0)
                    {
                        replacedWithEmptyDict(info);
                        cleared.Add("Info:全部字段(" + n + "项)");
                    }
                }

                // 7. 移除 XMP（/Metadata 引用 + 目标流对象）
                HashSet<int> metadataTargets = new HashSet<int>();
                int removedRefs = 0;
                foreach (PdfObj obj in objects.Values)
                {
                    if (obj.DictStart < 0) continue;
                    int lo = obj.DictStart;
                    int hi = obj.DictEnd > lo ? obj.DictEnd : obj.Body.Length;
                    string dictText = obj.Body.Substring(lo, hi - lo);
                    MatchCollection mm = Regex.Matches(dictText, @"/Metadata\s+(\d+)\s+(\d+)\s+R", RegexOptions.IgnoreCase);
                    if (mm.Count == 0) continue;
                    foreach (Match m in mm) metadataTargets.Add(int.Parse(m.Groups[1].Value));
                    string cleanedDict = Regex.Replace(dictText, @"/Metadata\s+\d+\s+\d+\s+R\s*", string.Empty, RegexOptions.IgnoreCase);
                    obj.Body = obj.Body.Substring(0, lo) + cleanedDict + obj.Body.Substring(hi);
                    removedRefs += mm.Count;
                }
                if (removedRefs > 0)
                {
                    cleared.Add("XMP:移除元数据引用(" + removedRefs + "处)");
                    foreach (int t in metadataTargets)
                    {
                        if (t != infoNum && objects.Remove(t))
                            cleared.Add("XMP:丢弃元数据流对象(" + t + ")");
                    }
                }

                // 8. 生成新 ID
                byte[] id1 = new byte[16];
                byte[] id2 = new byte[16];
                new Random().NextBytes(id1);
                new Random(Guid.NewGuid().GetHashCode()).NextBytes(id2);
                string newId = "<" + ToHex(id1) + "><" + ToHex(id2) + ">";

                // 9. 重建文件（经典 xref 表，无 /Prev，增量旧版本不写入）
                string rebuilt = Rebuild(pdf, rootSection, objects, infoNum, newId);
                byte[] outBytes = Latin1.GetBytes(rebuilt);

                if (!ValidatePdf(outBytes))
                    throw new InvalidDataException("清理后文件结构校验失败");

                File.WriteAllBytes(tmp, outBytes);

                // 10. 备份并替换
                if (createBackup)
                {
                    string bak = path + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Copy(path, bak);
                }
                File.Copy(tmp, path, true);
                TryDelete(tmp);

                return CleanResult.Ok(path, "PDF", cleared);
            }
            catch (SkipException ex)
            {
                TryDelete(tmp);
                return CleanResult.Skip(path, ex.Message);
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

        private static int CountDictKeys(PdfObj obj)
        {
            int n = 0;
            foreach (string k in obj.Dict.Keys)
                if (!k.Equals("/Length", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("/Type", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("/Filter", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        private static void replacedWithEmptyDict(PdfObj obj)
        {
            string head = obj.Num + " " + obj.Gen + " obj\n<< >>\nendobj\n";
            obj.Body = head;
            obj.Dict = new Dictionary<string, PdfVal>();
            obj.DictStart = -1;
            obj.DictEnd = -1;
            obj.HasStream = false;
        }

        private static string ToHex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("X2"));
            return sb.ToString();
        }

        private static XrefSection FollowXrefChain(string pdf, Dictionary<int, XrefEntry> all)
        {
            int pos = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
            if (pos < 0) throw new InvalidDataException("找不到 startxref");

            Lexer lx = new Lexer(pdf, pos + 9);
            long offset = lx.ReadInt();
            if (offset < 0 || offset >= pdf.Length) throw new InvalidDataException("startxref 偏移越界");

            XrefSection root = null;
            HashSet<long> visited = new HashSet<long>();
            long cur = offset;
            while (cur >= 0 && cur < pdf.Length && visited.Add(cur))
            {
                XrefSection sec = ParseXrefAt(pdf, (int)cur);
                if (root == null) root = sec;
                foreach (KeyValuePair<int, XrefEntry> kv in sec.Entries)
                    if (!all.ContainsKey(kv.Key)) all[kv.Key] = kv.Value;  // 首次遇到 = 最新版本

                long prev = -1;
                Match pm = Regex.Match(sec.TrailerDict, @"/Prev\s+(\d+)", RegexOptions.IgnoreCase);
                if (pm.Success) prev = long.Parse(pm.Groups[1].Value);

                // hybrid-reference：/XRefStm 补充条目（不覆盖已有）
                Match xm = Regex.Match(sec.TrailerDict, @"/XRefStm\s+(\d+)", RegexOptions.IgnoreCase);
                if (xm.Success)
                {
                    long xs = long.Parse(xm.Groups[1].Value);
                    if (xs >= 0 && xs < pdf.Length)
                    {
                        XrefSection xsec = ParseXrefAt(pdf, (int)xs);
                        foreach (KeyValuePair<int, XrefEntry> kv in xsec.Entries)
                            if (!all.ContainsKey(kv.Key)) all[kv.Key] = kv.Value;
                    }
                }

                cur = prev;
            }
            if (root == null) throw new InvalidDataException("无法解析 xref");
            return root;
        }

        private static XrefSection ParseXrefAt(string pdf, int offset)
        {
            Lexer lx = new Lexer(pdf, offset);
            lx.SkipWs();

            if (lx.I + 4 <= pdf.Length && pdf.Substring(lx.I, 4) == "xref")
                return ParseXrefTable(pdf, lx.I + 4);

            // xref stream：间接对象
            PdfObj obj = ReadObject(pdf, offset);
            if (obj == null) throw new InvalidDataException("xref 位置无有效对象");
            XrefSection sec = new XrefSection();
            sec.TrailerDict = obj.DictStart >= 0
                ? obj.Body.Substring(obj.DictStart, obj.DictEnd - obj.DictStart)
                : "<< >>";
            sec.StreamObjNum = obj.Num;

            PdfVal wv, iv;
            if (!obj.Dict.TryGetValue("/W", out wv) || wv.Kind != "array" || wv.Array.Count < 3)
                throw new InvalidDataException("xref stream 缺少 /W");
            int w1 = (int)wv.Array[0], w2 = (int)wv.Array[1], w3 = (int)wv.Array[2];
            int rowLen = w1 + w2 + w3;
            if (rowLen <= 0) throw new InvalidDataException("xref stream /W 无效");

            string stream = GetDecompressedStream(obj);
            byte[] data = Latin1.GetBytes(stream);

            int start = 0, count = 0;
            if (obj.Dict.TryGetValue("/Index", out iv) && iv.Kind == "array" && iv.Array.Count >= 2)
            {
                start = (int)iv.Array[0];
                count = (int)iv.Array[1];
            }
            else
            {
                PdfVal sv;
                if (!obj.Dict.TryGetValue("/Size", out sv) || sv.Kind != "num")
                    throw new InvalidDataException("xref stream 缺少 /Size");
                start = 0;
                count = (int)sv.Number;
            }

            for (int i = 0; i < count; i++)
            {
                int p = i * rowLen;
                if (p + rowLen > data.Length) break;
                int type = (int)ReadIntBE(data, p, w1);
                long f2 = ReadIntBE(data, p + w1, w2);
                int f3 = (int)ReadIntBE(data, p + w1 + w2, w3);
                sec.Entries[start + i] = new XrefEntry(type, f2, f3);
            }
            return sec;
        }

        private static XrefSection ParseXrefTable(string pdf, int pos)
        {
            XrefSection sec = new XrefSection();
            Lexer lx = new Lexer(pdf, pos);
            while (lx.I < pdf.Length)
            {
                lx.SkipWs();
                if (lx.I + 7 <= pdf.Length && pdf.Substring(lx.I, 7) == "trailer")
                {
                    lx.I += 7;
                    lx.SkipWs();
                    if (lx.I >= pdf.Length || pdf[lx.I] != '<') throw new InvalidDataException("trailer 字典缺失");
                    int st = lx.I;
                    PdfVal dummy = lx.ReadValue(); // 读取整个字典
                    sec.TrailerDict = pdf.Substring(st, lx.I - st);
                    break;
                }
                long first = lx.ReadInt();
                long cnt = lx.ReadInt();
                if (cnt <= 0) break;
                for (int i = 0; i < cnt; i++)
                {
                    lx.SkipWs();
                    long off = lx.ReadInt();
                    long gen = lx.ReadInt();
                    lx.SkipWs();
                    if (lx.I >= pdf.Length) break;
                    char flag = pdf[lx.I++];
                    sec.Entries[(int)(first + i)] = new XrefEntry(flag == 'n' ? 1 : 0, off, (int)gen);
                }
            }
            if (string.IsNullOrEmpty(sec.TrailerDict))
                throw new InvalidDataException("xref 表缺少 trailer");
            return sec;
        }

        private static long ReadIntBE(byte[] data, int pos, int width)
        {
            long v = 0;
            for (int i = 0; i < width; i++) v = (v << 8) | data[pos + i];
            return v;
        }

        private static string GetDecompressedStream(PdfObj obj)
        {
            if (!obj.HasStream) return string.Empty;
            string raw = obj.Body.Substring(obj.StreamStart, obj.StreamEnd - obj.StreamStart);
            PdfVal f;
            bool flate = obj.Dict.TryGetValue("/Filter", out f) &&
                         (f.Kind == "name" && (f.Name == "/FlateDecode" || f.Name == "/Fl"));
            if (!flate) return raw;

            byte[] bytes = Latin1.GetBytes(raw);
            int off = 0;
            if (bytes.Length > 2 && (bytes[0] & 0x0f) == 8 && ((bytes[0] << 8 | bytes[1]) % 31) == 0)
                off = 2;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes, off, bytes.Length - off))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                    return Latin1.GetString(outMs.ToArray());
                }
            }
            catch
            {
                // 解压失败不能继续重建：对象流里的页面对象会丢失，宁可放弃整个文件
                throw new InvalidDataException("PDF 流解压失败，跳过以避免损坏文件");
            }
        }

        private static void UnpackObjectStreams(string pdf, Dictionary<int, XrefEntry> entries,
            Dictionary<int, PdfObj> objects, HashSet<int> excluded)
        {
            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, XrefEntry> kv in entries)
            {
                if (kv.Value.Type != 2) continue;
                int container = (int)kv.Value.F2;
                if (!groups.ContainsKey(container)) groups[container] = new List<int>();
                groups[container].Add(kv.Key);
            }

            foreach (KeyValuePair<int, List<int>> g in groups)
            {
                XrefEntry ce;
                if (!entries.TryGetValue(g.Key, out ce) || ce.Type != 1) continue;
                PdfObj container = ReadObject(pdf, (int)ce.F2);
                if (container == null || !container.HasStream) continue;

                PdfVal nv, fv;
                if (!container.Dict.TryGetValue("/N", out nv) || nv.Kind != "num" ||
                    !container.Dict.TryGetValue("/First", out fv) || fv.Kind != "num") continue;

                int n = (int)nv.Number;
                int first = (int)fv.Number;
                string stream = GetDecompressedStream(container);
                if (stream.Length <= first) continue;

                string header = stream.Substring(0, first);
                Lexer lx = new Lexer(header, 0);
                List<int> nums = new List<int>();
                List<int> offs = new List<int>();
                for (int i = 0; i < n * 2; i++)
                {
                    long v = lx.ReadInt();
                    if (i % 2 == 0) nums.Add((int)v); else offs.Add((int)v);
                }

                for (int i = 0; i < nums.Count; i++)
                {
                    int st = first + offs[i];
                    if (st < 0 || st >= stream.Length) continue;
                    int en = (i + 1 < offs.Count) ? first + offs[i + 1] : stream.Length;
                    if (en <= st || en > stream.Length) en = stream.Length;
                    string content = stream.Substring(st, en - st).Trim();
                    if (content.Length == 0) continue;
                    PdfObj child = new PdfObj();
                    child.Num = nums[i];
                    child.Gen = 0;
                    child.Body = nums[i] + " 0 obj\n" + content + "\nendobj\n";
                    ParseDictRange(child);
                    objects[nums[i]] = child;
                }
                excluded.Add(g.Key); // 容器对象不再写入
            }
        }

        private static PdfObj ReadObject(string pdf, int offset)
        {
            if (offset < 0 || offset >= pdf.Length) return null;
            Lexer lx = new Lexer(pdf, offset);
            lx.SkipWs();
            int num = (int)lx.ReadInt();
            int gen = (int)lx.ReadInt();
            lx.SkipWs();
            if (lx.I + 3 > pdf.Length || pdf.Substring(lx.I, 3) != "obj") return null;
            lx.I += 3;
            int contentStart = lx.I;
            lx.SkipWs();

            PdfObj obj = new PdfObj { Num = num, Gen = gen };
            int dictStartAbs = -1, dictEndAbs = -1;
            if (lx.I + 1 < pdf.Length && pdf[lx.I] == '<' && pdf[lx.I + 1] == '<')
            {
                dictStartAbs = lx.I;
                obj.Dict = ParseTopDict(pdf, ref lx, out dictEndAbs);
            }

            int probe = lx.I;
            lx.SkipWs();
            if (lx.I + 6 <= pdf.Length && pdf.Substring(lx.I, 6) == "stream" &&
                (lx.I + 6 == pdf.Length || Lexer.IsWs(pdf[lx.I + 6])))
            {
                obj.HasStream = true;
                int p = lx.I + 6;
                if (p < pdf.Length && pdf[p] == '\r') { p++; if (p < pdf.Length && pdf[p] == '\n') p++; }
                else if (p < pdf.Length && pdf[p] == '\n') p++;
                PdfVal lv;
                int streamEndAbs;
                if (obj.Dict.TryGetValue("/Length", out lv) && lv.Kind == "num" && lv.Number >= 0 &&
                    p + lv.Number <= pdf.Length)
                {
                    streamEndAbs = p + (int)lv.Number;
                }
                else
                {
                    int e = pdf.IndexOf("endstream", p, StringComparison.Ordinal);
                    if (e < 0) throw new InvalidDataException("找不到 endstream");
                    streamEndAbs = e;
                }
                int eo = pdf.IndexOf("endobj", streamEndAbs, StringComparison.Ordinal);
                if (eo < 0) throw new InvalidDataException("找不到 endobj");
                obj.Body = pdf.Substring(offset, eo + 6 - offset);
                obj.StreamStart = p - offset;
                obj.StreamEnd = streamEndAbs - offset;
            }
            else
            {
                lx.I = probe;
                int eo = pdf.IndexOf("endobj", contentStart, StringComparison.Ordinal);
                if (eo < 0) throw new InvalidDataException("找不到 endobj");
                obj.Body = pdf.Substring(offset, eo + 6 - offset);
            }
            // 字典偏移由绝对位置换算为相对 Body 的位置
            if (dictStartAbs >= 0)
            {
                obj.DictStart = dictStartAbs - offset;
                obj.DictEnd = dictEndAbs - offset;
            }
            return obj;
        }

        private static void ParseDictRange(PdfObj obj)
        {
            if (obj.DictStart < 0) return;
            Lexer lx = new Lexer(obj.Body, obj.DictStart);
            obj.Dict = ParseTopDict(obj.Body, ref lx, out obj.DictEnd);
        }

        private static Dictionary<string, PdfVal> ParseTopDict(string s, ref Lexer lx, out int dictEnd)
        {
            Dictionary<string, PdfVal> d = new Dictionary<string, PdfVal>();
            dictEnd = -1;
            lx.I += 2; // skip <<
            int depth = 1;
            while (lx.I < s.Length && depth > 0)
            {
                lx.SkipWs();
                if (lx.I >= s.Length) break;
                if (s[lx.I] == '/' && depth == 1)
                {
                    string name = lx.ReadName();
                    lx.SkipWs();
                    int vs = lx.I;
                    PdfVal v = lx.ReadValue();
                    v.Start = vs;
                    v.End = lx.I;
                    d[name] = v;
                    continue;
                }
                if (lx.I + 1 < s.Length && s[lx.I] == '<' && s[lx.I + 1] == '<') { depth++; lx.I += 2; continue; }
                if (lx.I + 1 < s.Length && s[lx.I] == '>' && s[lx.I + 1] == '>')
                {
                    depth--;
                    lx.I += 2;
                    if (depth == 0) { dictEnd = lx.I; break; }
                    continue;
                }
                if (s[lx.I] == '(') { lx.ReadLiteralString(); continue; }
                lx.I++;
            }
            return d;
        }

        private static string Rebuild(string original, XrefSection root, Dictionary<int, PdfObj> objects,
            int infoNum, string newId)
        {
            int headerEnd = original.IndexOf('\n');
            string header = headerEnd > 0 && headerEnd < 32
                ? original.Substring(0, headerEnd + 1)
                : "%PDF-1.7\n";

            StringBuilder sb = new StringBuilder();
            sb.Append(header);
            sb.Append("%\xE2\xE3\xCF\xD3\n");
            sb.AppendLine();

            Dictionary<int, long> offsets = new Dictionary<int, long>();
            List<int> nums = new List<int>(objects.Keys);
            nums.Sort();
            long running = Latin1.GetByteCount(sb.ToString());
            foreach (int num in nums)
            {
                offsets[num] = running;
                PdfObj obj = objects[num];
                sb.Append(obj.Body);
                if (!obj.Body.EndsWith("\n", StringComparison.Ordinal)) sb.Append('\n');
                running += Latin1.GetByteCount(obj.Body) + (obj.Body.EndsWith("\n", StringComparison.Ordinal) ? 0 : 1);
            }

            long xrefOffset = running;
            int maxNum = 0;
            foreach (int n in nums) if (n > maxNum) maxNum = n;

            sb.Append("xref\n0 ").Append(maxNum + 1).Append('\n');
            sb.Append("0000000000 65535 f \n");
            for (int i = 1; i <= maxNum; i++)
            {
                long off;
                if (offsets.TryGetValue(i, out off))
                    sb.Append(off.ToString("D10")).Append(" 00000 n \n");
                else
                    sb.Append("0000000000 65535 f \n");
            }

            // trailer：移除 /Prev /XRefStm，替换 /Size /Root /Info /ID，保留其他键
            string td = root.TrailerDict;
            td = Regex.Replace(td, @"/Prev\s+\d+\s*", string.Empty, RegexOptions.IgnoreCase);
            td = Regex.Replace(td, @"/XRefStm\s+\d+\s*", string.Empty, RegexOptions.IgnoreCase);
            td = Regex.Replace(td, @"/Size\s+\d+\s*", string.Empty, RegexOptions.IgnoreCase);
            td = Regex.Replace(td, @"/Root\s+\d+\s+\d+\s+R\s*", string.Empty, RegexOptions.IgnoreCase);
            td = Regex.Replace(td, @"/Info\s+\d+\s+\d+\s+R\s*", string.Empty, RegexOptions.IgnoreCase);
            td = Regex.Replace(td, @"/ID\s*\[[^\]]*\]\s*", string.Empty, RegexOptions.IgnoreCase);

            int rootNum = 1;
            Match rm = Regex.Match(root.TrailerDict, @"/Root\s+(\d+)\s+(\d+)\s+R", RegexOptions.IgnoreCase);
            if (rm.Success) rootNum = int.Parse(rm.Groups[1].Value);
            if (!objects.ContainsKey(rootNum) && nums.Count > 0) rootNum = nums[0];
            int infoRef = infoNum >= 0 && objects.ContainsKey(infoNum) ? infoNum : 0;

            string infoPart = infoRef > 0 ? " /Info " + infoRef + " 0 R" : string.Empty;
            string prefix = "<< /Size " + (maxNum + 1) + " /Root " + rootNum + " 0 R" + infoPart +
                            " /ID [" + newId + "] ";
            if (td.StartsWith("<<", StringComparison.Ordinal) && td.EndsWith(">>", StringComparison.Ordinal))
                td = prefix + td.Substring(2, td.Length - 4).Trim() + " >>";
            else
                td = prefix + ">>";

            sb.Append("trailer\n").Append(td).Append('\n');
            sb.Append("startxref\n").Append(xrefOffset).Append('\n');
            sb.Append("%%EOF\n");
            return sb.ToString();
        }

        private static bool ValidatePdf(byte[] data)
        {
            try
            {
                string s = Latin1.GetString(data);
                if (!s.StartsWith("%PDF-", StringComparison.Ordinal)) return false;
                int p = s.LastIndexOf("startxref", StringComparison.Ordinal);
                if (p < 0) return false;
                Lexer lx = new Lexer(s, p + 9);
                long off = lx.ReadInt();
                if (off < 0 || off >= s.Length) return false;
                XrefSection sec = ParseXrefAt(s, (int)off);
                return sec.Entries.Count > 0 && !string.IsNullOrEmpty(sec.TrailerDict);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
