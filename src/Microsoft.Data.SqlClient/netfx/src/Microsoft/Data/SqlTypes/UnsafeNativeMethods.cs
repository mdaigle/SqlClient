// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Common;

namespace Microsoft.Data.SqlTypes
{
    [SuppressUnmanagedCodeSecurity]
    internal static class UnsafeNativeMethods
    {
        #region PInvoke methods

        [DllImport("NtDll.dll", CharSet = CharSet.Unicode)]
        [ResourceExposure(ResourceScope.Machine)]
        internal static extern UInt32 NtCreateFile
            (
                out Microsoft.Win32.SafeHandles.SafeFileHandle fileHandle,
                Int32 desiredAccess,
                ref OBJECT_ATTRIBUTES objectAttributes,
                out IO_STATUS_BLOCK ioStatusBlock,
                ref Int64 allocationSize,
                UInt32 fileAttributes,
                System.IO.FileShare shareAccess,
                UInt32 createDisposition,
                UInt32 createOptions,
                SafeHandle eaBuffer,
                UInt32 eaLength
            );

        [DllImport("Kernel32.dll", SetLastError = true)]
        [ResourceExposure(ResourceScope.None)]
        internal static extern FileType GetFileType
            (
                Microsoft.Win32.SafeHandles.SafeFileHandle hFile
            );

        // RTM versions of Win7 and Windows Server 2008 R2
        private static readonly Version ThreadErrorModeMinOsVersion = new Version(6, 1, 7600);

        // do not use this method directly, use SetErrorModeWrapper instead
        [DllImport("Kernel32.dll", ExactSpelling = true)]
        [ResourceExposure(ResourceScope.Process)]
        private static extern uint SetErrorMode(uint mode);

        // do not use this method directly, use SetErrorModeWrapper instead
        // this API exists since Windows 7 / Windows Server 2008 R2
        [DllImport("Kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [ResourceExposure(ResourceScope.None)]
        [SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage")]
        private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);

        /// <summary>
        /// this method uses thread-safe version of SetErrorMode on Windows 7/Windows Server 2008 R2 operating systems.
        /// </summary>
        [ResourceExposure(ResourceScope.Process)] // None on Windows7 / Windows Server 2008 R2 or later
        [ResourceConsumption(ResourceScope.Process)]
        internal static void SetErrorModeWrapper(uint mode, out uint oldMode)
        {
            if (Environment.OSVersion.Version >= ThreadErrorModeMinOsVersion)
            {
                // safe to use new API
                if (!SetThreadErrorMode(mode, out oldMode))
                {
                    throw new System.ComponentModel.Win32Exception();
                }
            }
            else
            {
                // cannot use the new SetThreadErrorMode API on current OS, fallback to the old one
                oldMode = SetErrorMode(mode);
            }
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [ResourceExposure(ResourceScope.Machine)]
        internal static extern bool DeviceIoControl
            (
                Microsoft.Win32.SafeHandles.SafeFileHandle fileHandle,
                uint ioControlCode,
                IntPtr inBuffer,
                uint cbInBuffer,
                IntPtr outBuffer,
                uint cbOutBuffer,
                out uint cbBytesReturned,
                IntPtr overlapped
            );

        [DllImport("NtDll.dll")]
        [ResourceExposure(ResourceScope.None)]
        internal static extern UInt32 RtlNtStatusToDosError
            (
                UInt32 status
            );

        #region definitions from devioctl.h

        internal const ushort FILE_DEVICE_FILE_SYSTEM = 0x0009;

        internal enum Method
        {
            METHOD_BUFFERED,
            METHOD_IN_DIRECT,
            METHOD_OUT_DIRECT,
            METHOD_NEITHER
        };

        internal enum Access
        {
            FILE_ANY_ACCESS,
            FILE_READ_ACCESS,
            FILE_WRITE_ACCESS
        }

        internal static uint CTL_CODE
            (
                ushort deviceType,
                ushort function,
                byte method,
                byte access
            )
        {
            if (function > 4095)
                throw ADP.ArgumentOutOfRange("function");

            return (uint)((deviceType << 16) | (access << 14) | (function << 2) | method);
        }

        #endregion

        #endregion

        #region Error codes

        internal const int ERROR_INVALID_HANDLE = 6;
        internal const int ERROR_MR_MID_NOT_FOUND = 317;

        internal const uint STATUS_INVALID_PARAMETER = 0xc000000d;
        internal const uint STATUS_SHARING_VIOLATION = 0xc0000043;
        internal const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xc0000034;

        #endregion

        internal const uint SEM_FAILCRITICALERRORS = 0x0001;

        internal enum FileType : uint
        {
            Unknown = 0x0000,   // FILE_TYPE_UNKNOWN
            Disk = 0x0001,   // FILE_TYPE_DISK
            Char = 0x0002,   // FILE_TYPE_CHAR
            Pipe = 0x0003,   // FILE_TYPE_PIPE
            Remote = 0x8000    // FILE_TYPE_REMOTE
        }

        #region definitions from wdm.h

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct OBJECT_ATTRIBUTES
        {
            internal int length;
            internal IntPtr rootDirectory;
            internal SafeHandle objectName;
            internal int attributes;
            internal IntPtr securityDescriptor;
            internal SafeHandle securityQualityOfService;
        }

        [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct UNICODE_STRING
        {
            internal UInt16 length;
            internal UInt16 maximumLength;
            internal string buffer;
        }

        // VSTFDevDiv # 547461 [Backport SqlFileStream fix on Win7 to QFE branch]
        // Win7 enforces correct values for the _SECURITY_QUALITY_OF_SERVICE.qos member.
        // taken from _SECURITY_IMPERSONATION_LEVEL enum definition in winnt.h
        internal enum SecurityImpersonationLevel
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct SECURITY_QUALITY_OF_SERVICE
        {
            internal UInt32 length;
            [MarshalAs(UnmanagedType.I4)]
            internal int impersonationLevel;
            internal byte contextDynamicTrackingMode;
            internal byte effectiveOnly;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct IO_STATUS_BLOCK
        {
            internal UInt32 status;
            internal IntPtr information;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct FILE_FULL_EA_INFORMATION
        {
            internal UInt32 nextEntryOffset;
            internal Byte flags;
            internal Byte EaNameLength;
            internal UInt16 EaValueLength;
            internal Byte EaName;
        }

        [Flags]
        internal enum CreateOption : uint
        {
            FILE_WRITE_THROUGH = 0x00000002,
            FILE_SEQUENTIAL_ONLY = 0x00000004,
            FILE_NO_INTERMEDIATE_BUFFERING = 0x00000008,
            FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020,
            FILE_RANDOM_ACCESS = 0x00000800
        }

        internal enum CreationDisposition : uint
        {
            FILE_SUPERSEDE = 0,
            FILE_OPEN = 1,
            FILE_CREATE = 2,
            FILE_OPEN_IF = 3,
            FILE_OVERWRITE = 4,
            FILE_OVERWRITE_IF = 5
        }

        #endregion

        #region definitions from winnt.h

        internal const int FILE_READ_DATA = 0x0001;
        internal const int FILE_WRITE_DATA = 0x0002;
        internal const int FILE_READ_ATTRIBUTES = 0x0080;
        internal const int SYNCHRONIZE = 0x00100000;

        #endregion

        #region definitions from ntdef.h

        [Flags]
        internal enum Attributes : uint
        {
            Inherit = 0x00000002,
            Permanent = 0x00000010,
            Exclusive = 0x00000020,
            CaseInsensitive = 0x00000040,
            OpenIf = 0x00000080,
            OpenLink = 0x00000100,
            KernelHandle = 0x00000200,
            ForceAccessCheck = 0x00000400,
            ValidAttributes = 0x000007F2
        }

        #endregion

    }
}
