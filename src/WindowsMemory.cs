using System;
using System.Runtime.InteropServices;

namespace StutterBuddies
{
    /// <summary>Reads native Windows process memory counters when Mono reports zeros.</summary>
    internal static class WindowsMemory
    {
        private const double BytesPerMiB = 1048576.0;

        // DWORD fields remain 32-bit; SIZE_T fields follow the process pointer size.
        // The native layout is 80 bytes on x64 and 44 bytes on x86.
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr process, ref ProcessMemoryCountersEx counters, uint size);

        /// <summary>
        /// Returns working set and private committed memory in MiB. These metrics
        /// overlap and must not be added. Failure returns false and zero outputs.
        /// </summary>
        public static bool TryRead(out double workingMiB, out double privateMiB)
        {
            workingMiB = 0;
            privateMiB = 0;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;

            try
            {
                uint size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCountersEx));
                var counters = new ProcessMemoryCountersEx { Size = size };
                // This is a pseudo handle owned by Windows; do not close it.
                if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, size)) return false;

                ulong workingBytes = counters.WorkingSetSize.ToUInt64();
                ulong privateBytes = counters.PrivateUsage.ToUInt64();
                if (workingBytes == 0 || privateBytes == 0) return false;

                workingMiB = workingBytes / BytesPerMiB;
                privateMiB = privateBytes / BytesPerMiB;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
            catch (PlatformNotSupportedException) { return false; }
        }
    }
}
