using System.Runtime.InteropServices;
using K400Fn.Hidpp;

namespace K400Fn;

internal static class Program
{
    private const string Version = "1.1.0";

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static int Main(string[] args)
    {
        bool quiet = args.Any(arg => arg is "-q" or "--quiet");
        if (quiet)
            HideOwnConsole();

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

                case "-q":
                case "--quiet":
                    quiet = true;
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

        TextWriter output = quiet ? TextWriter.Null : Console.Out;
        TextWriter error = quiet ? TextWriter.Null : Console.Error;

        int exitCode;
        try
        {
            exitCode = FnSwitcher.Run(
                status ? null : target,
                toggle,
                verbose,
                output,
                error);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            if (verbose)
                error.WriteLine(ex);
            exitCode = 1;
        }

        if (pause && !quiet && !Console.IsInputRedirected)
        {
            output.WriteLine();
            output.Write("Press any key to exit...");
            Console.ReadKey(intercept: true);
        }

        return exitCode;
    }

    /// <summary>
    /// Hides the console only when this process owns it (for example when the
    /// scheduled task starts it). A shared terminal is left alone.
    /// </summary>
    private static void HideOwnConsole()
    {
        uint[] processes = new uint[2];
        uint count = GetConsoleProcessList(processes, (uint)processes.Length);
        if (count > 1)
            return;

        IntPtr window = GetConsoleWindow();
        if (window != IntPtr.Zero)
            ShowWindow(window, 0); // SW_HIDE
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
              -q, --quiet     No output (and hide the console if owned)
                  --pause     Wait for a key press before exiting
              -h, --help      Show this help
                  --version   Show the version

            Examples:
              K400Fn                 Use standard F1-F12 keys
              K400Fn media           Use media keys by default
              K400Fn status          Show what the keyboard is doing now
              K400Fn toggle -v        Flip the mode and show discovery details
              K400Fn fkeys --quiet    Silent, for Task Scheduler

            The setting is stored by the keyboard. Some K400 Plus firmware
            resets it after a long idle period; run the tool again if that
            happens.
            """);
    }
}
