using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AttriDel
{
    /// <summary>
    /// 老格式 OLE 复合文档清理器（doc / xls / ppt）。
    /// 元数据存放在 SummaryInformation / DocSummaryInformation / UserDefinedProperties
    /// 属性集中，通过系统 COM 接口 IPropertySetStorage.Delete 删除整个属性集，
    /// 再由 OleScrubber 把释放出的空闲扇区填零，正文流字节完全不动。
    /// </summary>
    internal static class OleCleaner
    {
        private static readonly Guid FMTID_SummaryInformation =
            new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
        private static readonly Guid FMTID_DocSummaryInformation =
            new Guid("D5CDD502-2E9C-101B-9397-08002B2CF9AE");
        private static readonly Guid FMTID_UserDefinedProperties =
            new Guid("D5CDD505-2E9C-101B-9397-08002B2CF9AE");

        private const uint STGM_READWRITE = 0x00000002;
        private const uint STGM_SHARE_EXCLUSIVE = 0x00000010;

        [ComImport, Guid("0000013A-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertySetStorage
        {
            void Create(ref Guid rfmtid, ref Guid piid, uint grfFlags, uint grfMode,
                        out IPropertyStorage ppprstg);
            void Open(ref Guid rfmtid, uint grfMode, out IPropertyStorage ppprstg);
            void Delete(ref Guid rfmtid);
            void Enum(out IntPtr pprgEnum);
            void CreateEx(ref Guid rfmtid, ref Guid piid, uint grfFlags, uint grfMode,
                          IntPtr pPropId, out IPropertyStorage ppprstg);
        }

        [ComImport, Guid("0000000B-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IStorage
        {
            void CreateStream(string pwcsName, uint grfMode, uint r1, uint r2, out IntPtr ppstm);
            void OpenStream(string pwcsName, IntPtr r1, uint grfMode, uint r2, out IntPtr ppstm);
            void CreateStorage(string pwcsName, uint grfMode, uint r1, uint r2, out IntPtr ppstg);
            void OpenStorage(string pwcsName, IntPtr pstgPriority, uint grfMode, IntPtr snb, uint r, out IntPtr ppstg);
            void CopyTo(uint ciidExclude, IntPtr rgiidExclude, IntPtr snbExclude, IStorage pstgDest);
            void MoveElementTo(string pwcsName, IntPtr pstgDest, string pwcsNewName, uint grfFlags);
            void Commit(uint grfCommitFlags);
            void Revert();
            void EnumElements(uint r1, IntPtr r2, uint r3, out IntPtr ppenum);
            void DestroyElement(string pwcsName);
            void RenameElement(string pwcsOld, string pwcsNew);
            void SetElementTimes(string pwcsName, IntPtr pct, IntPtr pat, IntPtr pmt);
            void SetClass(ref Guid clsid);
            void SetStateBits(uint grfStateBits, uint grfMask);
            void Stat(IntPtr pstatstg, uint grfStatFlag);
        }

        [ComImport, Guid("00000138-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStorage
        {
            void ReadMultiple(uint cpspec, IntPtr rgpspec, IntPtr rgpropvar);
            void WriteMultiple(uint cpspec, IntPtr rgpspec, IntPtr rgpropvar, uint pidNameFirst);
            void DeleteMultiple(uint cpspec, IntPtr rgpspec);
            void ReadPropertyNames(uint cpropid, IntPtr rgpropid, IntPtr rglpwstrName);
            void WritePropertyNames(uint cpropid, IntPtr rgpropid, IntPtr rglpwstrName);
            void DeletePropertyNames(uint cpropid, IntPtr rgpropid);
            void Commit(uint grfCommitFlags);
            void Revert();
            void Enum(out IntPtr ppenum);
            void SetTimes(IntPtr pftc, IntPtr pftm, IntPtr pfta);
            void SetClass(ref Guid clsid);
            void Stat(IntPtr pstatpsstg);
        }

        private const uint STGFMT_ANY = 4;

        [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int StgOpenStorageEx(
            [MarshalAs(UnmanagedType.LPWStr)] string pwcsName,
            uint grfMode,
            uint stgfmt,
            uint grfAttrs,
            IntPtr pStgOptions,
            IntPtr pSecurityDescriptor,
            ref Guid riid,
            out IntPtr ppObjectOpen);

        public static CleanResult Clean(string path, bool createBackup)
        {
            string tmp = path + ".attributedel.tmp";
            List<string> cleared = new List<string>();

            try
            {
                // 1. 独占读取检测占用
                byte[] raw;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    raw = new byte[fs.Length];
                    fs.Read(raw, 0, raw.Length);
                }

                // 2. 加密 / 签名检测（OLE 目录项名为 UTF-16）
                if (ContainsUtf16(raw, "EncryptedPackage") || ContainsUtf16(raw, "EncryptionInfo"))
                    return CleanResult.Skip(path, "文件已加密，跳过");
                if (ContainsUtf16(raw, "DigitalSignature"))
                    return CleanResult.Skip(path, "文档含数字签名，跳过");

                // 3. 复制到临时文件，在临时文件上操作，避免改坏原文件
                File.Copy(path, tmp, true);

                int n = DeleteAllPropertySets(tmp);
                if (n > 0) cleared.Add("属性集(" + n + "个)");

                // 4. 物理擦除空闲扇区，清除 OLE 删除后残留的敏感字节
                int scrubbed = OleScrubber.Scrub(tmp);
                if (scrubbed > 0) cleared.Add("物理擦除(" + scrubbed + "字节)");

                // 5. 校验：临时文件仍可被 OLE 打开
                if (!ValidateOle(tmp))
                    throw new InvalidDataException("清理后文件结构校验失败");

                // 5. 备份并替换
                if (createBackup)
                {
                    string bak = path + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Copy(path, bak);
                }
                File.Copy(tmp, path, true);
                TryDelete(tmp);

                return CleanResult.Ok(path, "OLE", cleared);
            }
            catch (COMException ex)
            {
                TryDelete(tmp);
                return CleanResult.Skip(path, "文件被占用、加密或无法打开：" + ex.Message);
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

        /// <summary>
        /// 删除 SummaryInformation / DocSummaryInformation / UserDefinedProperties
        /// 三个完整属性集。删除整个属性集会释放其扇区，供后续物理擦除清零。
        /// 注意：删掉属性集后 Office 仍能在打开时按需重建空属性集，不影响文档使用。
        /// </summary>
        private static int DeleteAllPropertySets(string path)
        {
            IntPtr pStgObj = IntPtr.Zero;
            IntPtr pPss = IntPtr.Zero;
            IStorage storage = null;
            IPropertySetStorage pss = null;
            int deleted = 0;

            Guid iidStg = typeof(IStorage).GUID;
            int hr = StgOpenStorageEx(path, STGM_READWRITE | STGM_SHARE_EXCLUSIVE,
                                      STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero,
                                      ref iidStg, out pStgObj);
            if (hr != 0 || pStgObj == IntPtr.Zero)
                throw new COMException("无法打开 OLE 复合文档", hr);

            try
            {
                storage = (IStorage)Marshal.GetTypedObjectForIUnknown(pStgObj, typeof(IStorage));

                Guid iidPss = typeof(IPropertySetStorage).GUID;
                int hrQ = Marshal.QueryInterface(pStgObj, ref iidPss, out pPss);
                if (hrQ != 0 || pPss == IntPtr.Zero)
                    throw new COMException("无法获取 IPropertySetStorage", hrQ);
                pss = (IPropertySetStorage)Marshal.GetTypedObjectForIUnknown(
                    pPss, typeof(IPropertySetStorage));

                Guid[] fmtids = { FMTID_SummaryInformation, FMTID_DocSummaryInformation,
                                  FMTID_UserDefinedProperties };
                foreach (Guid f in fmtids)
                {
                    Guid fmtid = f;
                    try
                    {
                        pss.Delete(ref fmtid);
                        deleted++;
                    }
                    catch (COMException)
                    {
                        // 属性集不存在，忽略
                    }
                }

                storage.Commit(0);
                return deleted;
            }
            finally
            {
                if (pss != null) Marshal.ReleaseComObject(pss);
                if (storage != null) Marshal.ReleaseComObject(storage);
                if (pPss != IntPtr.Zero) Marshal.Release(pPss);
                if (pStgObj != IntPtr.Zero) Marshal.Release(pStgObj);
            }
        }

        private static bool ValidateOle(string path)
        {
            IntPtr pObj = IntPtr.Zero;
            Guid iidPss = typeof(IPropertySetStorage).GUID;
            try
            {
                int hr = StgOpenStorageEx(path, STGM_READ | STGM_SHARE_EXCLUSIVE,
                                          STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero,
                                          ref iidPss, out pObj);
                return hr == 0 && pObj != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pObj != IntPtr.Zero) Marshal.Release(pObj);
            }
        }

        private const uint STGM_READ = 0x00000000;

        private static bool ContainsUtf16(byte[] data, string text)
        {
            byte[] pattern = Encoding.Unicode.GetBytes(text);
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        /// <summary>只读预览所需的 COM 结构</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct STATPROPSTG
        {
            public IntPtr lpwstrName;   // COM 返回 LPWSTR 指针，手动转换
            public uint propid;
            public ushort vt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPSPEC
        {
            public uint ulKind;     // PRSPEC_PROPID = 1
            private uint _padding;  // 64 位下联合体按 8 字节对齐
            public uint propid;
            private uint _padding2;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr ptr;      // VT_LPSTR / VT_LPWSTR
            [FieldOffset(8)] public int i4;
            [FieldOffset(8)] public double dbl;
            [FieldOffset(8)] public long filetime;
        }

        [ComImport, Guid("00000139-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSTATPROPSTG
        {
            void Next(uint celt, IntPtr rgelt, out uint pceltFetched);
            void Skip(uint celt);
            void Reset();
            void Clone(out IEnumSTATPROPSTG ppenum);
        }

        private const ushort VT_EMPTY = 0;
        private const ushort VT_I2 = 2;
        private const ushort VT_I4 = 3;
        private const ushort VT_R8 = 5;
        private const ushort VT_BOOL = 11;
        private const ushort VT_LPSTR = 30;
        private const ushort VT_LPWSTR = 31;
        private const ushort VT_FILETIME = 64;

        /// <summary>只读预览：枚举三个属性集的非空属性，不修改文件。</summary>
        public static InspectResult Inspect(string path)
        {
            List<PropertyItem> items = new List<PropertyItem>();
            IntPtr pStgObj = IntPtr.Zero;
            IStorage storage = null;
            try
            {
                byte[] raw;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    raw = new byte[fs.Length];
                    fs.Read(raw, 0, raw.Length);
                }
                if (ContainsUtf16(raw, "EncryptedPackage") || ContainsUtf16(raw, "EncryptionInfo"))
                    return InspectResult.Skip("文件已加密，无法读取属性");

                Guid iidStg = typeof(IStorage).GUID;
                int hr = StgOpenStorageEx(path, STGM_READ | STGM_SHARE_EXCLUSIVE,
                                          STGFMT_ANY, 0, IntPtr.Zero, IntPtr.Zero,
                                          ref iidStg, out pStgObj);
                if (hr != 0 || pStgObj == IntPtr.Zero)
                    return InspectResult.Skip("文件被占用或无法打开");
                storage = (IStorage)Marshal.GetTypedObjectForIUnknown(pStgObj, typeof(IStorage));

                Guid[] fmtids = { FMTID_SummaryInformation, FMTID_DocSummaryInformation,
                                  FMTID_UserDefinedProperties };
                string[] prefixes = { "", "d", "u" };
                for (int k = 0; k < fmtids.Length; k++)
                {
                    ReadPropertySet(pStgObj, fmtids[k], prefixes[k], items);
                }
                return InspectResult.Ok(items);
            }
            catch (COMException ex)
            {
                return InspectResult.Skip("文件被占用或无法打开：" + ex.Message);
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
            finally
            {
                if (storage != null) Marshal.ReleaseComObject(storage);
                if (pStgObj != IntPtr.Zero) Marshal.Release(pStgObj);
            }
        }

        private static void ReadPropertySet(IntPtr pStgObj, Guid fmtid, string prefix,
                                            List<PropertyItem> items)
        {
            IntPtr pPss = IntPtr.Zero;
            IPropertySetStorage pss = null;
            IntPtr pPropStg = IntPtr.Zero;
            IPropertyStorage propStg = null;
            IntPtr pEnum = IntPtr.Zero;
            IEnumSTATPROPSTG enumerator = null;
            try
            {
                Guid iidPss = typeof(IPropertySetStorage).GUID;
                int hrQ = Marshal.QueryInterface(pStgObj, ref iidPss, out pPss);
                if (hrQ != 0 || pPss == IntPtr.Zero) return;
                pss = (IPropertySetStorage)Marshal.GetTypedObjectForIUnknown(
                    pPss, typeof(IPropertySetStorage));

                Guid f = fmtid;
                try
                {
                    pss.Open(ref f, STGM_READ | STGM_SHARE_EXCLUSIVE, out propStg);
                }
                catch (COMException)
                {
                    return;   // 属性集不存在
                }
                if (propStg == null) return;

                propStg.Enum(out pEnum);
                if (pEnum == IntPtr.Zero) return;
                enumerator = (IEnumSTATPROPSTG)Marshal.GetTypedObjectForIUnknown(
                    pEnum, typeof(IEnumSTATPROPSTG));

                int statSize = Marshal.SizeOf(typeof(STATPROPSTG));
                IntPtr statPtr = Marshal.AllocHGlobal(statSize);
                try
                {
                    uint fetched;
                    while (true)
                    {
                        enumerator.Next(1, statPtr, out fetched);
                        if (fetched == 0) break;
                        STATPROPSTG stat = (STATPROPSTG)Marshal.PtrToStructure(statPtr, typeof(STATPROPSTG));
                        string value = ReadOneProperty(propStg, stat.propid, stat.vt);
                        if (value == null || value.Length == 0) continue;

                        // TotalTime（pid:10）在 OLE 里以 VT_FILETIME 存放，语义是 100ns 计数的编辑时长
                        if (stat.propid == 10 && !prefix.Equals("u", StringComparison.Ordinal) &&
                            stat.vt == VT_FILETIME)
                        {
                            long ticks;
                            if (long.TryParse(value, out ticks) && ticks > 0)
                                value = Math.Round(ticks / 600000000.0, 1) + " 分钟";
                            else
                                value = string.Empty;
                            if (value.Length == 0) continue;
                        }

                        string propName = stat.lpwstrName != IntPtr.Zero
                            ? Marshal.PtrToStringUni(stat.lpwstrName) : null;
                        string displayName;
                        if (propName != null && propName.Trim().Length > 0)
                        {
                            // 已知属性名统一中文显示，其余自定义属性保留原名
                            string mapped = FieldNames.Get(propName.Trim());
                            displayName = mapped.Equals(propName.Trim(), StringComparison.Ordinal)
                                ? propName.Trim() : mapped;
                        }
                        else
                            displayName = FieldNames.Get("pid:" + prefix + stat.propid);
                        items.Add(new PropertyItem(displayName, value));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(statPtr);
                }
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                if (propStg != null) Marshal.ReleaseComObject(propStg);
                if (pEnum != IntPtr.Zero) Marshal.Release(pEnum);
                if (pss != null) Marshal.ReleaseComObject(pss);
                if (pPss != IntPtr.Zero) Marshal.Release(pPss);
            }
        }

        private static string ReadOneProperty(IPropertyStorage propStg, uint propid, ushort declaredVt)
        {
            int psSize = Marshal.SizeOf(typeof(PROPSPEC));
            int pvSize = Marshal.SizeOf(typeof(PROPVARIANT));
            IntPtr psPtr = Marshal.AllocHGlobal(psSize);
            IntPtr pvPtr = Marshal.AllocHGlobal(pvSize);
            string result = string.Empty;
            try
            {
                PROPSPEC ps = new PROPSPEC { ulKind = 1, propid = propid };
                Marshal.StructureToPtr(ps, psPtr, false);
                for (int i = 0; i < pvSize; i++) Marshal.WriteByte(pvPtr, i, 0);

                propStg.ReadMultiple(1, psPtr, pvPtr);
                PROPVARIANT pv = (PROPVARIANT)Marshal.PtrToStructure(pvPtr, typeof(PROPVARIANT));

                switch (pv.vt)
                {
                    case VT_LPWSTR:
                        if (pv.ptr != IntPtr.Zero) result = Marshal.PtrToStringUni(pv.ptr) ?? string.Empty;
                        break;
                    case VT_LPSTR:
                        if (pv.ptr != IntPtr.Zero) result = Marshal.PtrToStringAnsi(pv.ptr) ?? string.Empty;
                        break;
                    case VT_FILETIME:
                        if (propid == 12 || propid == 13 || propid == 11)   // 创建/修改/打印时间
                        {
                            try { result = DateTime.FromFileTime(pv.filetime).ToString("yyyy-MM-dd HH:mm:ss"); }
                            catch { result = string.Empty; }
                        }
                        else
                        {
                            result = pv.filetime.ToString();   // 如 TotalTime：由调用方按数值解释
                        }
                        break;
                    case VT_I2:
                        result = ((short)(pv.i4 & 0xFFFF)).ToString();
                        break;
                    case VT_I4:
                        result = pv.i4.ToString();
                        break;
                    case VT_R8:
                        result = pv.dbl.ToString();
                        break;
                    case VT_BOOL:
                        result = ((short)(pv.i4 & 0xFFFF)) != 0 ? "是" : "否";
                        break;
                    default:
                        result = string.Empty;   // 二进制/未知类型不展示
                        break;
                }
                // 释放 BSTR / LPWSTR / LPSTR 等分配的内存
                if ((pv.vt == VT_LPWSTR || pv.vt == VT_LPSTR) && pv.ptr != IntPtr.Zero)
                {
                    try { Marshal.FreeCoTaskMem(pv.ptr); }
                    catch { }
                }
                return result.Trim();
            }
            catch (COMException)
            {
                return string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(psPtr);
                Marshal.FreeHGlobal(pvPtr);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
