using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SteamDownloader;


class Program
{


    static async Task Main(string[] args)
    {
        // string steamCmdPath = "D:\\steamcmd\\steamcmd.exe";

        string? steamCmdPath = FindCMDexe();
        if (steamCmdPath == null)
        {
            Console.WriteLine("Cannot find steamcmd.exe. Please install steamcmd and place it in C:\\steamcmd\\ or D:\\steamcmd\\");
            Console.WriteLine("Or write here the full path to steamcmd.exe manually, something like C:\\Games\\steamcmd.exe  ");

            string? userInput = Console.ReadLine();

            if (!string.IsNullOrWhiteSpace(userInput) && File.Exists(userInput))
            {
                steamCmdPath = userInput;  
            }
            else
            {
                Console.WriteLine("steamcmd.exe was not found.");
                return; 
            }
        }

        Console.WriteLine("Insert collection url.");
        string collectionUrl = Console.ReadLine();

        List<string> list = await Scraper.ExtractWorkshopIDs(collectionUrl);

        Console.WriteLine("IDs:");
        List<string> commands = new List<string>();

        // všechny stahované položky
        foreach (string id in list)
        {
            Console.WriteLine("workshop_download_item" + " " + id);
        }
        // velikost batch.. ošetření chyby, kdy bylo v kolekci moc záznamů a cmd spadlo na chybu
        int batchSize = 10; 
        var batches = list
            .Select((id, index) => new { id, index })
            .GroupBy(x => x.index / batchSize)
            .Select(group => group.Select(x => x.id).ToList())
            .ToList();

        // Logování výsledků
        List<string> successfulDown = new List<string>();
        List<string> failedDown = new List<string>();

        // Zpracování každé dávky zvlášt
        foreach (var batch in batches)
        {
            Console.WriteLine($"Processing batch: {string.Join(", ", batch)}");
            string arguments = $"+login anonymous " +
                               string.Join(" ", batch.Select(id => $"+workshop_download_item {id}")) +
                               " +quit";

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = steamCmdPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string output;
                        bool batchSuccess = true;

                        while ((output = await process.StandardOutput.ReadLineAsync()) != null)
                        {
                            Console.WriteLine(output);

                            // Pokud EROR - zapiš
                            if (output.Contains("ERROR"))
                            {
                                string failedId = batch.FirstOrDefault(id => output.Contains(id));
                                if (failedId != null)
                                {
                                    failedDown.Add(failedId);
                                }

                                batchSuccess = false;
                            }
                        }

                        process.WaitForExit();

                        // pokud succes 
                        if (batchSuccess && process.ExitCode == 0)
                        {
                            successfulDown.AddRange(batch);
                        }
                        else
                        {
                            failedDown.AddRange(batch.Except(successfulDown));
                        }
                    }
                    else
                    {
                        Console.WriteLine("Failed to start batch.");
                        failedDown.AddRange(batch);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error for batch: {ex.Message}");
                failedDown.AddRange(batch);
            }
        }

        // Zobrazení výsledků
        Console.WriteLine("\nComplete.");
        Console.WriteLine("Downloaded:");
        foreach (string id in successfulDown)
        {
            
            Console.WriteLine(id);
        }

        Console.WriteLine($"\nTotal: {successfulDown.Count}");

        Console.WriteLine("\nWith error:");
        foreach (string id in failedDown)
        {
            Console.WriteLine(id);
        }
    }

    static string FindCMDexe()
    {
        
        string[] paths = new[]
        {
         @"C:\steamcmd\steamcmd.exe",
        @"C:\Program Files (x86)\SteamCMD\steamcmd.exe",
        @"C:\Program Files\SteamCMD\steamcmd.exe",
        @"C:\Games\x\steamcmd.exe",
        @"D:\steamcmd\steamcmd.exe",

        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd.exe") };

        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir, "steamcmd.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
        }

        return null; 

    }

}
