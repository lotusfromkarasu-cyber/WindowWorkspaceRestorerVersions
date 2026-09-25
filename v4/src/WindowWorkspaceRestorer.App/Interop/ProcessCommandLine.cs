using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowWorkspaceRestorer.Interop;

internal static class ProcessCommandLine
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const int MaxCommandLineChars = 32767;

    public static string? TryGet(uint processId)
    {
        var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (process == nint.Zero)
        {
            return null;
        }

        try
        {
            var headerSize = nint.Size == 8 ? 16 : 8;
            var bufferSize = headerSize + (MaxCommandLineChars * 2) + 2;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                var status = NativeMethods.NtQueryInformationProcess(
                    process,
                    ProcessCommandLineInformation,
                    buffer,
                    (uint)bufferSize,
                    out _);
                if (status != 0)
                {
                    return null;
                }

                var length = (int)Marshal.ReadInt16(buffer);
                if (length <= 0 || length > MaxCommandLineChars * 2)
                {
                    return null;
                }

                // ProcessCommandLineInformation returns a UNICODE_STRING header
                // followed immediately by the command-line text in the same output
                // buffer, so the text starts right after the header and needs no
                // cross-process memory read.
                var textPointer = buffer + headerSize;
                return Marshal.PtrToStringUni(textPointer, length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    public static IReadOnlyList<string> ParseArguments(string commandLine)
    {
        var argv = NativeMethods.CommandLineToArgvW(commandLine, out var argumentCount);
        if (argv == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var arguments = new List<string>(argumentCount);
            for (var i = 0; i < argumentCount; i++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argv, i * nint.Size);
                arguments.Add(Marshal.PtrToStringUni(argumentPointer) ?? string.Empty);
            }

            return arguments;
        }
        finally
        {
            var heap = NativeMethods.GetProcessHeap();
            if (heap != nint.Zero)
            {
                NativeMethods.HeapFree(heap, 0, argv);
            }
        }
    }
}