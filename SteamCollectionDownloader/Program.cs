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
        string steamCmdPath = "D:\\steamcmd\\steamcmd.exe";
        Console.WriteLine(steamCmdPath);

        Console.WriteLine("Insert collection url.");
        string collectionUrl = Console.ReadLine();

        List<string> list = await Scraper.ExtractWorkshopIDs(collectionUrl);

        Console.WriteLine("IDs:");
        List<string> commands = new List<string>();

        // všechny stahované položky
        foreach (string id in list)
        {
            Console.WriteLine("workshop_download_item" + " " + id);
            commands.Add($"+workshop_download_item {id}");
        }


        string allCommands = string.Join(" ", commands);

        ProcessStartInfo startInfo = new ProcessStartInfo()
        {
            FileName = steamCmdPath,
            Arguments = $"+login anonymous {allCommands} +quit",  // prihlaseni jako anonym

            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        using (Process process = Process.Start(startInfo))
        {
            if (process != null)
            {
                try
                {
                    string output = await process.StandardOutput.ReadLineAsync();
                    Console.WriteLine(output);
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill();

                    }
                }
            }
        }
    }
}
