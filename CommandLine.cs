namespace Nat64Switch;

internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine(string command, string[] args)
    {
        Command = command;
        ParseArgs(args);
    }

    public string Command { get; }

    public bool HasFlag(string name) => _flags.Contains(name);

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLine("menu", Array.Empty<string>());
        }

        if (args[0].StartsWith("-", StringComparison.Ordinal))
        {
            var command = args[0] switch
            {
                "-h" or "--help" => "help",
                "-v" or "--version" => "version",
                _ => throw new ArgumentException($"未知参数：{args[0]}")
            };
            return new CommandLine(command, args);
        }

        return new CommandLine(args[0], args);
    }

    private void ParseArgs(string[] args)
    {
        for (var index = 1; index < args.Length; index++)
        {
            var token = args[index];
            if (token is "-h" or "--help")
            {
                _flags.Add("help");
                continue;
            }

            if (token is "-v" or "--version")
            {
                _flags.Add("version");
                continue;
            }

            if (token is "--json")
            {
                _flags.Add("json");
                continue;
            }

            if (token is "--dry-run")
            {
                _flags.Add("dry-run");
                continue;
            }

            if (token is "--dhcp")
            {
                _flags.Add("dhcp");
                continue;
            }

            if (token is "--pause")
            {
                continue;
            }

            var canonical = token switch
            {
                "-a" => "--adapter",
                "-s" => "--server",
                "-d" => "--domain",
                "--timeout" => "--timeout",
                "--adapter" => "--adapter",
                "--server" => "--server",
                "--domain" => "--domain",
                _ => null
            };

            if (canonical is null)
            {
                throw new ArgumentException($"未知参数：{token}");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"参数 {token} 缺少值。");
            }

            _options[canonical] = args[++index];
        }
    }
}
