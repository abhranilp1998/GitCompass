using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GitCompass.Terminal.Windows;

internal static class Native
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    internal static readonly IntPtr PseudoConsoleAttribute = new(0x00020016);

    [StructLayout(LayoutKind.Sequential)] internal struct Coord
    {
        public short X, Y;
        public Coord(short x, short y) { X = x; Y = y; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct JobBasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct JobExtendedLimitInformation
    {
        public JobBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, uint size);
    [DllImport("kernel32.dll")] internal static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole);
    [DllImport("kernel32.dll")] internal static extern int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);
    [DllImport("kernel32.dll")] internal static extern void ClosePseudoConsole(IntPtr pseudoConsole);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, UIntPtr size, IntPtr previousValue, IntPtr returnSize);
    [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref JobExtendedLimitInformation info, uint infoSize);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

    internal static void Check(bool success, string operation)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }
    internal static void CheckHResult(int result, string operation)
    {
        if (result < 0) throw new COMException(operation, result);
    }
}
