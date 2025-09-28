using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamDownloader;

class Program
{
    private const int DefaultBatchSize = 2;
    private static readonly string LogFile = $"steam_download_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";

    static async Task Main(string[] args)
    {
        string? steamCmdPath = PromptForSteamCmdPath();
        if (steamCmdPath == null)
        {
            Log("steamcmd.exe was not found.", ConsoleColor.Red);
            return;
        }

        Console.WriteLine("Insert collection URL:");
        string? collectionUrl = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(collectionUrl))
        {
            Log("No URL provided. Exiting.", ConsoleColor.Yellow);
            return;
        }

        var ids = await Scraper.ExtractWorkshopIDs(collectionUrl);
        if (ids.Count == 0)
        {
            Log("No workshop IDs found.", ConsoleColor.Yellow);
            return;
        }

        var successful = new HashSet<string>();
        var failed = new HashSet<string>();

        var batches = ids
            .Select((id, index) => new { id, index })
            .GroupBy(x => x.index / DefaultBatchSize)
            .Select(group => group.Select(x => x.id).ToList())
            .ToList();

        foreach (var batch in batches)
        {
            await DownloadBatch(batch, steamCmdPath, successful, failed);
        }

        PrintResults(successful, failed);
    }

    private static string? PromptForSteamCmdPath()
    {
        string? steamCmdPath = FindCMDexe();
        if (steamCmdPath != null) return steamCmdPath;

        Log("Cannot find steamcmd.exe.", ConsoleColor.Yellow);
        Console.WriteLine("Please install steamcmd and place it in C:\\steamcmd\\ or D:\\steamcmd\\");
        Console.WriteLine("Or enter the full path manually:");

        string? userInput = Console.ReadLine();
        return !string.IsNullOrWhiteSpace(userInput) && File.Exists(userInput) ? userInput : null;
    }

    private static async Task DownloadBatch(List<string> batch, string steamCmdPath,
        HashSet<string> successful, HashSet<string> failed)
    {
        Log($"Starting download of batch ({batch.Count} items)", ConsoleColor.Cyan);

        string arguments = $"+login anonymous " +
                           string.Join(" ", batch.Select(id => $"+workshop_download_item {id}")) +
                           " +quit";

        var startInfo = new ProcessStartInfo
        {
            FileName = steamCmdPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log("Failed to start steamcmd process.", ConsoleColor.Red);
                failed.UnionWith(batch);
                return;
            }

            bool batchError = false;

            
            var outputTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    Log($"[steamcmd] {line}", ConsoleColor.Gray);
                    if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        batchError = true;
                }
            });

            
            var errorTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) != null)
                {
                    Log($"[steamcmd][ERR] {line}", ConsoleColor.Red);
                    batchError = true;
                }
            });

            await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask);

            if (!batchError && process.ExitCode == 0)
            {
                foreach (var id in batch)
                {
                    Log($"SUCCESS {id}", ConsoleColor.Green);
                    successful.Add(id);
                }
            }
            else
            {
                foreach (var id in batch)
                {
                    Log($"ERROR {id}", ConsoleColor.Red);
                    failed.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Exception for batch: {ex.Message}", ConsoleColor.Red);
            failed.UnionWith(batch);
        }
    }

    private static void PrintResults(HashSet<string> successful, HashSet<string> failed)
    {
        Log("\n===== COMPLETE =====", ConsoleColor.White);

        Log("Downloaded:", ConsoleColor.Green);
        foreach (string id in successful) Log(id, ConsoleColor.Green);
        Log($"Total: {successful.Count}", ConsoleColor.Green);

        Log("\nWith error:", ConsoleColor.Red);
        foreach (string id in failed) Log(id, ConsoleColor.Red);
        Log($"Total: {failed.Count}", ConsoleColor.Red);

      //  Log($"\nLog file saved to: {Path.GetFullPath(LogFile)}", ConsoleColor.Cyan);
    }

    private static string? FindCMDexe()
    {
        string[] paths =
        {
            @"C:\steamcmd\steamcmd.exe",
            @"C:\Program Files (x86)\SteamCMD\steamcmd.exe",
            @"C:\Program Files\SteamCMD\steamcmd.exe",
            @"C:\Games\steamcmd.exe",
            @"D:\steamcmd\steamcmd.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd.exe")
        };

        return paths.FirstOrDefault(File.Exists)
               ?? Environment.GetEnvironmentVariable("PATH")?
                   .Split(Path.PathSeparator)
                   .Select(dir => Path.Combine(dir, "steamcmd.exe"))
                   .FirstOrDefault(File.Exists);
    }

    private static void Log(string message, ConsoleColor color = ConsoleColor.Gray)
    {
        string timeStamped = $"[{DateTime.Now:HH:mm:ss}] {message}";

        // Výpis do konzole
        Console.ForegroundColor = color;
        Console.WriteLine(timeStamped);
        Console.ResetColor();

        // Zápis do log souboru
        //File.AppendAllText(LogFile, timeStamped + Environment.NewLine);
    }
}
