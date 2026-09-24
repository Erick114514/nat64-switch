using System.Text;

namespace Nat64Switch;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var isInteractive = args.Length == 0 ||
            string.Equals(args[0], "menu", StringComparison.OrdinalIgnoreCase);
        var shouldPause = isInteractive ||
            args.Any(arg => string.Equals(arg, "--pause", StringComparison.OrdinalIgnoreCase));

        try
        {
            return App.Run(args);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("已取消。");
            return 130;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"错误：{ex.Message}");
            return 1;
        }
        finally
        {
            if (shouldPause)
            {
                Console.WriteLine();
                Console.Write("按 Enter 退出...");
                Console.ReadLine();
            }
        }
    }
}
