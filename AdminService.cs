using System.Runtime.InteropServices;

namespace Nat64Switch;

internal static class AdminService
{
    public static bool IsAdministrator => IsUserAnAdmin();

    public static bool RelaunchElevated(IEnumerable<string> arguments)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        var argumentLine = string.Join(" ", arguments
            .Where(item => !string.Equals(item, "--pause", StringComparison.OrdinalIgnoreCase))
            .Append("--pause")
            .Select(QuoteArgument));

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = argumentLine,
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"'))
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsUserAnAdmin();
}
