using K400Fn.Hidpp;

namespace K400Fn;

internal static class Program
{
    private const string Version = "1.0.0";

    private static int Main(string[] args)
    {
        FnMode? target = FnMode.FunctionKeys;
        bool toggle = false;
        bool status = false;
        bool verbose = false;
        bool pause = false;

        foreach (string raw in args)
        {
            string arg = raw.Trim();
            switch (arg.ToLowerInvariant())
            {
                case "fkeys":
                case "fn":
                case "function":
                case "functionkeys":
                    target = FnMode.FunctionKeys;
                    status = false;
                    toggle = false;
                    break;

                case "media":
                case "special":
                case "mediakeys":
                    target = FnMode.MediaKeys;
                    status = false;
                    toggle = false;
                    break;

                case "toggle":
                case "switch":
                    toggle = true;
                    target = null;
                    status = false;
                    break;

                case "status":
                case "show":
                    status = true;
                    toggle = false;
                    target = null;
                    break;

                case "-v":
                case "--verbose":
                    verbose = true;
                    break;

                case "--pause":
                    pause = true;
                    break;

                case "-h":
                case "--help":
                case "/?":
                case "help":
                    PrintHelp();
                    return 0;

                case "--version":
                case "-V":
                    Console.WriteLine($"K400Fn {Version}");
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown option: {raw}");
                    Console.Error.WriteLine();
                    PrintHelp();
                    return 2;
            }
        }

        // No arguments means "the thing I almost always want".
        if (args.Length == 0)
            target = FnMode.FunctionKeys;

        int exitCode;
        try
        {
            exitCode = FnSwitcher.Run(
                status ? null : target,
                toggle,
                verbose,
                Console.Out,
                Console.Error);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (verbose)
                Console.Error.WriteLine(ex);
            exitCode = 1;
        }

        if (pause && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("Press any key to exit...");
            Console.ReadKey(intercept: true);
        }

        return exitCode;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            K400Fn - switch the F-key row on a Logitech K400 Plus (and similar
            keyboards) without installing Logi Options+.

            Usage:
              K400Fn [command] [options]

            Commands:
              fkeys      Make F1-F12 the primary keys (default when run with no args)
              media      Make the media/special keys primary
              toggle     Flip between the two modes
              status     Show the current mode

            Options:
              -v, --verbose   Show HID discovery details
                  --pause     Wait for a key press before exiting
              -h, --help      Show this help
                  --version   Show the version

            Examples:
              K400Fn                 Use standard F1-F12 keys
              K400Fn media           Use media keys by default
              K400Fn status          Show what the keyboard is doing now
              K400Fn toggle -v        Flip the mode and show discovery details

            The setting is stored in the keyboard, so it survives reboots and
            does not need a background program.
            """);
    }
}
