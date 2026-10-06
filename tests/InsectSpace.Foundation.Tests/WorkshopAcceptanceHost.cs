using System.Collections.Concurrent;
using System.Diagnostics;
using InsectSpace.GuWorkshop;
using InsectSpace.Server.Economy;
using InsectSpace.Server.GuPaths;
using InsectSpace.Server.GuWorkshop;

// Explicit test executable mode; these controls are not compiled into the game server.
internal static class WorkshopAcceptanceHost
{
    private sealed class Clock : IWorkshopClock { public long Seconds { get; set; } = 100; }
    private sealed class Random : IWorkshopRandom
    {
        public int Value, Calls;
        public int Next(int maximum) { Calls++; return Value % maximum; }
    }

    public static void Run(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out int port) || port < 1 || port > 65535 ||
            !int.TryParse(args[2], out int battlePort) || battlePort < 1 || battlePort > 65535)
            throw new ArgumentException("Expected fixture directory, TCP port and KCP port.");
        string directory = Path.GetFullPath(args[0]);
        if (File.ReadAllText(Path.Combine(directory, "TEST_ONLY.txt")).Trim() != "WORKSHOP_TEST_FIXTURE")
            throw new InvalidDataException("Explicit test fixture marker required.");
        var catalog = new WorkshopCatalog(GuPathService.LoadLocalCatalog(), name => File.ReadAllBytes(Path.Combine(directory, name + ".bytes")));
        var clock = new Clock(); var random = new Random();
        using var host = new LocalEconomyHost(port, battlePort, workshopMode: true, workshopClock: clock, workshopRandom: random, workshopCatalog: catalog);
        var console = new LocalEconomyConsole(host);
        var commands = new ConcurrentQueue<string>();
        _ = Task.Run(() => { string line; while ((line = Console.ReadLine()) != null) commands.Enqueue(line); commands.Enqueue("quit"); });
        Console.WriteLine("FIXTURE_READY TEST ONLY " + catalog.Fingerprint);
        var timer = Stopwatch.StartNew(); double last = 0;
        bool running = true;
        while (running)
        {
            double now = timer.Elapsed.TotalSeconds; host.Tick((float)(now - last)); last = now;
            while (commands.TryDequeue(out string command))
            {
                string result;
                if (command.StartsWith("roll:", StringComparison.Ordinal))
                {
                    int value = int.Parse(command[5..]);
                    if (value < 0 || value >= 10000) throw new ArgumentOutOfRangeException(nameof(value));
                    random.Value = value; result = "roll=" + value;
                }
                else if (command.StartsWith("time:", StringComparison.Ordinal))
                {
                    long value = long.Parse(command[5..]);
                    if (value < clock.Seconds) throw new ArgumentOutOfRangeException(nameof(value));
                    clock.Seconds = value; result = "time=" + value;
                }
                else if (command == "state")
                {
                    var state = host.Workshop.Snapshot(host.Principal, host.Cultivation.Snapshot(host.Principal));
                    result = $"time={clock.Seconds} roll={random.Value} calls={random.Calls} inventory={state.Inventory.Count} wallet={state.Wallet.YuanShi}";
                }
                else if (command == "quit") { running = false; result = "stopped"; }
                else if (command == "cultivation-xp" || command == "cultivation-proof") result = console.Execute(command);
                else throw new ArgumentException("Unknown test command.");
                Console.WriteLine("FIXTURE_ACK " + command + " " + result);
            }
            Thread.Sleep(5);
        }
    }
}
