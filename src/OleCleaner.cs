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

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
