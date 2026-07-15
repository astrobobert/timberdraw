using System;
using System.Runtime.InteropServices;

namespace TimberDraw
{
    // The app-wide browse-for-folder: the MODERN shell picker (IFileDialog with FOS_PICKFOLDERS),
    // so folder browsing looks like the file dialogs everywhere else in the app. WinForms'
    // FolderBrowserDialog on .NET Framework is still the legacy tree dialog -- visibly different
    // (Robert's call, batch-3 #1) -- so it survives only as the fallback when the COM dialog
    // cannot be created.
    internal static class FolderPicker
    {
        // Show the picker seeded at `initial` (when it exists). Returns the chosen folder, or
        // null on cancel.
        public static string Pick(string title, string initial)
        {
            try { return PickModern(title, initial); }
            catch (System.Exception ex)
            {
                Diag.Warn("FolderPicker", "modern picker unavailable, using the legacy dialog: " + ex.Message);
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = title,
                    SelectedPath = initial ?? "",
                    ShowNewFolderButton = true
                })
                {
                    return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
                }
            }
        }

        private static string PickModern(string title, string initial)
        {
            var dlg = (IFileDialog)new FileOpenDialogRcw();
            dlg.GetOptions(out uint opts);
            dlg.SetOptions(opts | FosPickFolders | FosForceFileSystem);
            dlg.SetTitle(title);
            if (!string.IsNullOrEmpty(initial) && System.IO.Directory.Exists(initial))
            {
                Guid iid = typeof(IShellItem).GUID;
                IShellItem seed = SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid);
                dlg.SetFolder(seed);
            }
            if (dlg.Show(IntPtr.Zero) != 0) return null;   // nonzero HRESULT = canceled
            dlg.GetResult(out IShellItem picked);
            picked.GetDisplayName(SigdnFileSysPath, out IntPtr p);
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }

        private const uint FosPickFolders = 0x20;
        private const uint FosForceFileSystem = 0x40;
        private const uint SigdnFileSysPath = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern IShellItem SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid);

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        // Only Show/SetOptions/GetOptions/SetFolder/SetTitle/GetResult are called, but a COM
        // interface declaration must list the WHOLE vtable in order -- do not reorder or trim.
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr hwndOwner);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
