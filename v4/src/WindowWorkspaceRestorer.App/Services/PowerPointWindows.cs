using System.Runtime.InteropServices;
using System.Text;
using WindowWorkspaceRestorer.Interop;

namespace WindowWorkspaceRestorer.Services;

internal static class PowerPointWindows
{
    internal static object? TryGetDocumentWindow(nint handle)
    {
        object? result = null;
        NativeMethods.EnumChildWindows(handle, (child, _) =>
        {
            var name = new StringBuilder(128);
            NativeMethods.GetClassName(child, name, name.Capacity);
            // Modern PowerPoint uses mdiClass; older releases use paneClassDC.
            if (name.ToString() is not ("paneClassDC" or "mdiClass")) return true;
            var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
            var hr = NativeMethods.AccessibleObjectFromWindow(child, 0xFFFFFFF0, ref dispatch, out var value);
            if (hr == 0 && value is not null)
            {
                result = value;
                return false;
            }
            Release(value);
            return true;
        }, 0);
        return result;
    }

    internal static object? TryGetApplication()
    {
        var active = NativeMethods.TryGetActiveComObject("PowerPoint.Application");
        if (active is not null) return active;
        foreach (var snapshot in new WindowScanner().Scan().Where(window =>
                     string.Equals(window.ProcessName, "POWERPNT.EXE", StringComparison.OrdinalIgnoreCase)))
        {
            var window = TryGetDocumentWindow(snapshot.Handle);
            try
            {
                if (window is not null) return ((dynamic)window).Application;
            }
            catch (COMException) { }
            finally { Release(window); }
        }
        return null;
    }

    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
