using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ServerManager.App;

/// <summary>Each managed server has its own hidden console, so Ctrl+C cannot reach another server or terminal.</summary>
internal static class ServerProcess
{
    internal static (Process Process, StreamReader Output, StreamReader Error) Start(string executable, params string[] arguments)
    {
        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        if (!CreatePipe(out var outputRead, out var outputWrite, ref security, 0)) throw new Win32Exception();
        using (outputWrite)
        {
            if (!CreatePipe(out var errorRead, out var errorWrite, ref security, 0)) { outputRead.Dispose(); throw new Win32Exception(); }
            using (errorWrite)
            using (var input = CreateFile("NUL", 0x80000000, 3, ref security, 3, 0, 0))
            {
                Process? process = null;
                StreamReader? stdout = null, stderr = null;
                var info = new ProcessInformation();
                try
                {
                    if (input.IsInvalid || !SetHandleInformation(outputRead, 1, 0) || !SetHandleInformation(errorRead, 1, 0)) throw new Win32Exception();
                    var startup = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x101, ShowWindow = 0,
                        Input = input.DangerousGetHandle(), Output = outputWrite.DangerousGetHandle(), Error = errorWrite.DangerousGetHandle()
                    };
                    var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
                    // CREATE_NEW_CONSOLE + CREATE_SUSPENDED. Obtain the managed handle before a fast exit can race us.
                    if (!CreateProcess(executable, command, 0, 0, true, 0x14, 0, Path.GetDirectoryName(executable), ref startup, out info)) throw new Win32Exception();
                    process = Process.GetProcessById(info.ProcessId);
                    _ = process.Handle;
                    stdout = new StreamReader(new FileStream(outputRead, FileAccess.Read), Encoding.UTF8, true);
                    stderr = new StreamReader(new FileStream(errorRead, FileAccess.Read), Encoding.UTF8, true);
                    if (ResumeThread(info.Thread) == uint.MaxValue) throw new Win32Exception();
                    return (process, stdout, stderr);
                }
                catch
                {
                    if (info.Process != 0) _ = TerminateProcess(info.Process, 1);
                    stdout?.Dispose(); stderr?.Dispose(); outputRead.Dispose(); errorRead.Dispose(); process?.Dispose(); throw;
                }
                finally { if (info.Thread != 0) CloseHandle(info.Thread); if (info.Process != 0) CloseHandle(info.Process); }
            }
        }
    }

    private static string Quote(string value)
    {
        var text = new StringBuilder("\""); var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    internal static async Task<bool> RequestStop(Process process)
    {
        if (process.HasExited) return true;
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "AssettoServerManager.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--signal-stop"); start.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("Cannot start shutdown helper.");
        try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)); }
        catch (TimeoutException) { if (!helper.HasExited) helper.Kill(); return false; }
        return helper.ExitCode == 0;
    }

    // Runs in a separate short-lived GUI process. Never attach the manager itself to a server console.
    internal static int Signal(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out var pid) || !long.TryParse(args[2], out var started)) return 2;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != started
                || !string.Equals(Path.GetFileName(process.MainModule?.FileName), "AssettoServer.exe", StringComparison.OrdinalIgnoreCase)) return 3;
            if (!AttachConsole((uint)pid)) return 4;
            try
            {
                var ids = new uint[32];
                var count = GetConsoleProcessList(ids, (uint)ids.Length);
                if (count == 0 || count > ids.Length || ids.Take((int)count).Any(id => id != pid && id != Environment.ProcessId)) return 5;
                if (!SetConsoleCtrlHandler(0, true) || !GenerateConsoleCtrlEvent(0, 0)) return 6;
                Thread.Sleep(150);
                return 0;
            }
            finally { FreeConsole(); }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return 7; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public nint Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title; public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedSize; public nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(SafeFileHandle handle, int mask, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, int share, ref SecurityAttributes security, int creation, int flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfo startup, out ProcessInformation info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleCtrlHandler(nint handler, bool add);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList([Out] uint[] ids, uint count);
}
