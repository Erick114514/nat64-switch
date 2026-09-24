using System.Text.Json;

namespace Nat64Switch;

internal static class StateStore
{
    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NAT64Switch",
        "state.json");

    public static void Save(string adapterName, DnsState state)
    {
        var all = ReadAll();
        if (all.ContainsKey(adapterName))
        {
            return;
        }

        all[adapterName] = new BackupState(
            state.Servers.ToArray(),
            state.Dhcp,
            DateTimeOffset.UtcNow);
        WriteAll(all);
    }

    public static BackupState? Take(string adapterName)
    {
        var all = ReadAll();
        if (!all.Remove(adapterName, out var state))
        {
            return null;
        }

        WriteAll(all);
        return state;
    }

    private static Dictionary<string, BackupState> ReadAll()
    {
        if (!File.Exists(StateFile))
        {
            return new Dictionary<string, BackupState>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(StateFile);
            return JsonSerializer.Deserialize(
                json,
                AppJsonContext.Default.DictionaryStringBackupState)
                ?? new Dictionary<string, BackupState>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, BackupState>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteAll(Dictionary<string, BackupState> state)
    {
        var directory = Path.GetDirectoryName(StateFile)
            ?? throw new InvalidOperationException("无法确定状态目录。");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            StateFile,
            JsonSerializer.Serialize(
                state,
                AppJsonContext.Default.DictionaryStringBackupState));
    }
}

internal sealed record BackupState(
    string[] Servers,
    string Dhcp,
    DateTimeOffset SavedAtUtc);
