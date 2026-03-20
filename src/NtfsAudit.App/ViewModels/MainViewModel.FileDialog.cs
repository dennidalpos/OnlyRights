/*
 * OnlyRights
 * Copyright (c) 2026 Danny Perondi
 * All rights reserved.
 *
 * Proprietary and confidential.
 * Viewing is permitted only for reference, evaluation, or internal review.
 * Unauthorized copying, modification, distribution, sublicensing,
 * commercial use, or reuse of this file is prohibited without prior
 * written permission from Danny Perondi.
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using System.Windows.Threading;
using Win32 = Microsoft.Win32;
using Newtonsoft.Json;
using WpfMessageBox = System.Windows.MessageBox;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private bool TryPickFolder(out string selectedPath)
        {
            selectedPath = null;
            try
            {
                var dialog = (IFileDialog)new FileOpenDialog();
                dialog.SetTitle("Seleziona cartella");
                dialog.GetOptions(out var options);
                options |= (uint)(FileDialogOptions.PickFolders
                    | FileDialogOptions.ForceFileSystem
                    | FileDialogOptions.PathMustExist
                    | FileDialogOptions.NoChangeDirectory);
                dialog.SetOptions(options);
                AddNetworkPlaces(dialog);

                if (!string.IsNullOrWhiteSpace(RootPath))
                {
                    if (SHCreateItemFromParsingName(RootPath, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
                    {
                        dialog.SetFolder(folder);
                    }
                }

                var result = dialog.Show(IntPtr.Zero);
                if (result == HResultCanceled)
                {
                    return false;
                }
                if (result != 0)
                {
                    return false;
                }
                dialog.GetResult(out var item);
                item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var pszString);
                selectedPath = Marshal.PtrToStringUni(pszString);
                Marshal.FreeCoTaskMem(pszString);
                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            catch
            {
                return false;
            }
        }

        private void AddNetworkPlaces(IFileDialog dialog)
        {
            return;
        }

        private bool TryAddPlace(IFileDialog dialog, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var place) == 0)
            {
                dialog.AddPlace(place, (int)FileDialogAddPlace.Bottom);
                return true;
            }

            return false;
        }

        private const int HResultCanceled = unchecked((int)0x800704C7);

        [Flags]
        private enum FileDialogOptions : uint
        {
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            NoChangeDirectory = 0x00000008,
            PathMustExist = 0x00000800
        }

        private enum FileDialogAddPlace
        {
            Top = 0,
            Bottom = 1
        }

        private enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
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

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);
    }
}
