using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PatchGUI.Core
{
    internal static class FileAccessProbe
    {
        public static bool CanOpenForPatch(string fullPath, out int win32Error)
        {
            win32Error = 0;

            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            try
            {
                if (!File.Exists(fullPath))
                    return false;

                var attrs = File.GetAttributes(fullPath);
                if ((attrs & FileAttributes.Directory) != 0)
                    return false;
            }
            catch
            {
                return false;
            }

            const uint GenericRead = 0x80000000;
            const uint GenericWrite = 0x40000000;
            const uint Delete = 0x00010000;
            const uint OpenExisting = 3;
            const uint FileAttributeNormal = 0x00000080;

            using SafeFileHandle handle = CreateFileW(
                fullPath,
                GenericRead | GenericWrite | Delete,
                0, // require exclusive access; fail early if any handle exists
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            if (!handle.IsInvalid)
                return true;

            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        public static string DescribeWin32Error(int win32Error)
        {
            if (win32Error == 0)
                return "OK";

            try
            {
                return new Win32Exception(win32Error).Message;
            }
            catch
            {
                return $"Win32Error={win32Error}";
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }
}
