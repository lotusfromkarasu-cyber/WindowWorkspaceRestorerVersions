using System.Runtime.InteropServices;

namespace WindowWorkspaceRestorer.Services;

internal static class ComHelpers
{
    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch (InvalidComObjectException)
            {
                // The server may already have released the proxy.
            }
        }
    }

    internal static void TrySetAutomationSecurity(dynamic application, out object? previous)
    {
        previous = null;
        try
        {
            previous = application.AutomationSecurity;
            // Office MsoAutomationSecurity.msoAutomationSecurityByUI.
            application.AutomationSecurity = 2;
        }
        catch
        {
            // Older or incompatible Office automation servers may not expose it.
        }
    }

    internal static void TryRestoreAutomationSecurity(dynamic application, object? previous)
    {
        if (previous is null)
        {
            return;
        }

        try
        {
            application.AutomationSecurity = previous;
        }
        catch
        {
            // Do not mask the original open result.
        }
    }
}
