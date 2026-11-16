using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GlassDock.Windows.Desktop;

internal sealed class WindowsChildProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;

    internal WindowsChildProcessJob()
    {
        handle = CreateJobObjectW(0, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    internal Process Start(string executable, string arguments)
    {
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>(), Flags = 1 };
        if (!CreateProcessW(executable, new StringBuilder($"\"{executable}\" {arguments}"), 0, 0,
            false, 0x08000004, 0, Path.GetDirectoryName(executable), ref startup, out var created))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Process? process = null;
        try
        {
            if (!AssignProcessToJobObject(handle, created.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            process = Process.GetProcessById((int)created.ProcessId);
            _ = process.SafeHandle;
            if (ResumeThread(created.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return process;
        }
        catch
        {
            TerminateProcess(created.Process, 1);
            process?.Dispose();
            throw;
        }
        finally
        {
            CloseHandle(created.Thread);
            CloseHandle(created.Process);
        }
    }

    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize;
        public nint ReservedBytes, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment,
        string? directory, ref StartupInfo startup, out ProcessInformation created);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
