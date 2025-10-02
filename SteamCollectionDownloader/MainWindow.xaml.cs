using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamDownloader
{
    public partial class MainWindow : Window
    {
        private readonly string LogFile = $"steam_download_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        private readonly HashSet<string> successful = new HashSet<string>();
        private readonly HashSet<string> failed = new HashSet<string>();
        private bool isSteamCmdInitialized = false;
        private string steamCmdPath = string.Empty;
        private int totalItems = 0;
        private int completedItems = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            OpenFolderButton.IsEnabled = false;
            UpdateStatus("Initializing...");
            steamCmdPath = await FindCMDexeAsync();

            if (string.IsNullOrWhiteSpace(steamCmdPath) || !File.Exists(steamCmdPath))
            {
                Log("steamcmd.exe not found or could not be initialized.", ConsoleColor.Red);
                UpdateStatus("Error: steamcmd.exe not found");
                StartButton.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            if (!isSteamCmdInitialized)
            {
                await InitializeSteamCmdAsync(steamCmdPath);
            }

            string collectionUrl = CollectionUrlTextBox.Text;

            if (string.IsNullOrWhiteSpace(collectionUrl))
            {
                Log("No URL provided.", ConsoleColor.Yellow);
                UpdateStatus("Error: No URL provided");
                StartButton.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            List<string> ids;
            try
            {
                Log("Fetching IDs from collection...", ConsoleColor.Cyan);
                UpdateStatus("Fetching collection IDs...");
                ids = await Scraper.ExtractWorkshopIDs(collectionUrl);
            }
            catch (Exception ex)
            {
                Log($"Failed to fetch or parse collection: {ex.Message}", ConsoleColor.Red);
                UpdateStatus("Error: Failed to fetch IDs");
                StartButton.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            if (ids.Count == 0)
            {
                Log("No workshop items found in collection.", ConsoleColor.Yellow);
                UpdateStatus("Error: No items found in collection");
                StartButton.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            totalItems = ids.Count;
            completedItems = 0;
            DownloadProgressBar.Maximum = 100;
            DownloadProgressBar.Value = 0;
            Log($"Total items to download: {totalItems}", ConsoleColor.Cyan);

            foreach (var id in ids)
            {
                UpdateStatus($"Downloading item {id} ({completedItems + 1}/{totalItems})...");
                await DownloadItem(id, steamCmdPath, successful, failed);
                completedItems++;
                double progress = (completedItems * 100.0) / totalItems;
                DownloadProgressBar.Value = progress;
            }

            PrintResults();
            UpdateStatus("Download completed");
            StartButton.IsEnabled = true;
            OpenFolderButton.IsEnabled = true;
        }

        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string workshopFolder = Path.Combine(Path.GetDirectoryName(steamCmdPath), "steamapps", "workshop", "content");
                if (Directory.Exists(workshopFolder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = workshopFolder,
                        UseShellExecute = true
                    });
                    Log("Workshop content folder opened.", ConsoleColor.Green);
                    UpdateStatus("Folder opened");
                }
                else
                {
                    Log("Workshop content folder not found.", ConsoleColor.Yellow);
                    UpdateStatus("Error: Folder not found");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to open folder: {ex.Message}", ConsoleColor.Red);
                UpdateStatus("Error: Failed to open folder");
            }
        }

        private async Task<string?> FindCMDexeAsync()
        {
            UpdateStatus("Searching for steamcmd.exe...");
            string steamCmdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Steamcmd", "steamcmd.exe");
            if (File.Exists(steamCmdPath))
            {
                Log("Found steamcmd.exe in Steamcmd folder.", ConsoleColor.Green);
                UpdateStatus("steamcmd.exe found");
                return steamCmdPath;
            }

            string projectSteamCmdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Steamcmd", "steamcmd.exe");
            if (File.Exists(projectSteamCmdPath))
            {
                string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Steamcmd");
                Directory.CreateDirectory(targetDir);
                File.Copy(projectSteamCmdPath, steamCmdPath, true);
                Log("Copied steamcmd.exe to output directory.", ConsoleColor.Green);
                UpdateStatus("steamcmd.exe copied");
                return steamCmdPath;
            }

            Log("steamcmd.exe not found, downloading...", ConsoleColor.Yellow);
            UpdateStatus("Downloading steamcmd.exe...");
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
                response.EnsureSuccessStatusCode();

                string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Steamcmd", "steamcmd.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
                await File.WriteAllBytesAsync(zipPath, await response.Content.ReadAsByteArrayAsync());

                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, Path.GetDirectoryName(zipPath));
                File.Delete(zipPath);

                if (File.Exists(steamCmdPath))
                {
                    Log("steamcmd.exe downloaded successfully.", ConsoleColor.Green);
                    UpdateStatus("steamcmd.exe downloaded");
                    return steamCmdPath;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to download steamcmd.exe: {ex.Message}", ConsoleColor.Red);
                UpdateStatus("Error: Failed to download steamcmd.exe");
            }

            string[] paths =
            {
                @"C:\steamcmd\steamcmd.exe",
                @"C:\Program Files (x86)\SteamCMD\steamcmd.exe",
                @"C:\Program Files\SteamCMD\steamcmd.exe",
                @"C:\Games\steamcmd.exe",
                @"D:\steamcmd\steamcmd.exe"
            };

            return paths.FirstOrDefault(File.Exists)
                   ?? Environment.GetEnvironmentVariable("PATH")?
                       .Split(Path.PathSeparator)
                       .Select(dir => Path.Combine(dir, "steamcmd.exe"))
                       .FirstOrDefault(File.Exists);
        }

        private async Task InitializeSteamCmdAsync(string steamCmdPath)
        {
            Log("Initializing steamcmd.exe...", ConsoleColor.Cyan);
            UpdateStatus("Initializing steamcmd.exe...");

            var startInfo = new ProcessStartInfo
            {
                FileName = steamCmdPath,
                Arguments = "+quit",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Log("Failed to start steamcmd.exe for initialization.", ConsoleColor.Red);
                    UpdateStatus("Error: Failed to initialize steamcmd.exe");
                    return;
                }

                var outputTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        if (line.Contains("Steam>", StringComparison.OrdinalIgnoreCase))
                        {
                            Log("[steamcmd-init] SteamCMD initialized.", ConsoleColor.Gray);
                        }
                    }
                });

                var errorTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        Log($"[steamcmd-init] Error: {line}", ConsoleColor.Red);
                    }
                });

                await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask);

                if (process.ExitCode == 0)
                {
                    Log("steamcmd.exe initialized successfully.", ConsoleColor.Green);
                    UpdateStatus("steamcmd.exe initialized");
                    isSteamCmdInitialized = true;
                }
                else
                {
                    Log("steamcmd.exe initialization failed.", ConsoleColor.Red);
                    UpdateStatus("Error: steamcmd.exe initialization failed");
                }
            }
            catch (Exception ex)
            {
                Log($"Initialization error: {ex.Message}", ConsoleColor.Red);
                UpdateStatus("Error: Initialization failed");
            }
        }

        private async Task DownloadItem(string id, string steamCmdPath, HashSet<string> successful, HashSet<string> failed)
        {
            Log($"Starting download of item {id}", ConsoleColor.Cyan);

            string arguments = $"+login anonymous +workshop_download_item {id} +quit";

            var startInfo = new ProcessStartInfo
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
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Log($"Failed to start steamcmd for item {id}.", ConsoleColor.Red);
                    UpdateStatus($"Error: Failed to download item {id}");
                    failed.Add(id);
                    return;
                }

                bool itemError = false;

                var outputTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        // Filter and log only relevant messages
                        if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"Error downloading item {id}: {line}", ConsoleColor.Red);
                            itemError = true;
                        }
                        else if (line.Contains("progress:", StringComparison.OrdinalIgnoreCase) || line.Contains("%"))
                        {
                            var match = Regex.Match(line, @"progress: (\d+)%");
                            if (match.Success)
                            {
                                double itemProgress = double.Parse(match.Groups[1].Value);
                                // Calculate total progress: completed items + current item progress
                                double totalProgress = ((completedItems + (itemProgress / 100.0)) * 100.0) / totalItems;
                                Dispatcher.Invoke(() => DownloadProgressBar.Value = totalProgress);
                                Log($"Progress for item {id}: {itemProgress}%", ConsoleColor.Cyan);
                            }
                        }
                    }
                });

                var errorTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        Log($"Error downloading item {id}: {line}", ConsoleColor.Red);
                        itemError = true;
                    }
                });

                await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask);

                if (!itemError && process.ExitCode == 0)
                {
                    Log($"Success: Item {id} downloaded.", ConsoleColor.Green);
                    successful.Add(id);
                }
                else
                {
                    Log($"Error: Item {id} failed to download.", ConsoleColor.Red);
                    UpdateStatus($"Error: Item {id} failed");
                    failed.Add(id);
                }
            }
            catch (Exception ex)
            {
                Log($"Error downloading item {id}: {ex.Message}", ConsoleColor.Red);
                UpdateStatus($"Error: Item {id} failed");
                failed.Add(id);
            }
        }

        private void PrintResults()
        {
            Log("\n===== COLLECTION DOWNLOAD COMPLETED =====", ConsoleColor.White);
            UpdateStatus("Download completed");

            Log("Total items downloaded:", ConsoleColor.Green);
            foreach (string id in successful) Log(id, ConsoleColor.Green);
            Log($"Total: {successful.Count}", ConsoleColor.Green);

            Log("\nFailed items:", ConsoleColor.Red);
            foreach (string id in failed) Log(id, ConsoleColor.Red);
            Log($"Total: {failed.Count}", ConsoleColor.Red);
        }

        private void Log(string message, ConsoleColor color)
        {
            string timeStamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Dispatcher.Invoke(() =>
            {
                var item = new ListBoxItem
                {
                    Content = timeStamped,
                    Foreground = color switch
                    {
                        ConsoleColor.Red => Brushes.Red,
                        ConsoleColor.Green => Brushes.GreenYellow,
                        ConsoleColor.Yellow => Brushes.Yellow,
                        ConsoleColor.Cyan => Brushes.Cyan,
                        ConsoleColor.White => Brushes.White,
                        _ => Brushes.Gray
                    }
                };
                LogListBox.Items.Add(item);
                LogListBox.ScrollIntoView(item);
            });
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message;
            });
        }
    }
}