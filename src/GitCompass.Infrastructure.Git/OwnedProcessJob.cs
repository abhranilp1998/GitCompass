using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GitCompass.Infrastructure.Git;

/// <summary>Owns Git and its newly launched descendants. Closing the job cannot target unrelated processes.</summary>
internal sealed class OwnedProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    internal OwnedProcessJob()
    {
        handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the owned Git process job.");
        var limits = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>())) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "Cannot configure the owned Git process job."); }
    }
    internal void Assign(IntPtr processHandle)
    {
        if (!AssignProcessToJobObject(handle, processHandle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot establish ownership of Git's child processes.");
    }
    internal bool HasActiveProcesses
    {
        get { return QueryInformationJobObject(handle, 1, out BasicAccountingInformation accounting, (uint)Marshal.SizeOf<BasicAccountingInformation>(), IntPtr.Zero) && accounting.ActiveProcesses > 0; }
    }
    internal void Terminate() { if (!handle.IsClosed && !handle.IsInvalid) TerminateJobObject(handle, 1); }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation { public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount; public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation { public BasicLimitInformation BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit; public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicAccountingInformation { public long TotalUserTime; public long TotalKernelTime; public long ThisPeriodTotalUserTime; public long ThisPeriodTotalKernelTime; public uint TotalPageFaultCount; public uint TotalProcesses; public uint ActiveProcesses; public uint TotalTerminatedProcesses; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, out BasicAccountingInformation information, uint length, IntPtr returnLength);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
