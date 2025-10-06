using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamDownloader
{
    public partial class MainWindow : Window
    {
        private readonly string LogFile = $"download_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        private readonly string ConfigFile = "config.json";
        private readonly HashSet<string> successful = new HashSet<string>();
        private readonly HashSet<string> failed = new HashSet<string>();
        private string steamCmdPath = string.Empty;
        private int totalItems = 0;
        private int completedItems = 0;
        private CancellationTokenSource cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            
        }

        private void LoadLastUrl()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (config != null && config.TryGetValue("LastUrl", out var url))
                    {
                        CollectionUrlTextBox.Text = url;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to load last URL: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        private void SaveLastUrl(string url)
        {
            try
            {
                var config = new Dictionary<string, string> { { "LastUrl", url } };
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config));
            }
            catch (Exception ex)
            {
                Log($"Failed to save last URL: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            OpenFolderButton.IsEnabled = false;
            cancellationTokenSource = new CancellationTokenSource();
            UpdateStatus("Initializing...");
            steamCmdPath = await FindCMDexeAsync();
            failed.Clear();

            if (string.IsNullOrWhiteSpace(steamCmdPath) || !File.Exists(steamCmdPath))
            {
                Log("steamcmd.exe not found.", ConsoleColor.Red);
                UpdateStatus("Error: steamcmd.exe not found");
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            string collectionUrl = CollectionUrlTextBox.Text;
            SaveLastUrl(collectionUrl);

            if (string.IsNullOrWhiteSpace(collectionUrl))
            {
                Log("No URL provided.", ConsoleColor.Yellow);
                UpdateStatus("Error: No URL provided");
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            List<(string gameId, string workshopId, string itemName)> ids;
            try
            {
                Log("Fetching IDs from collection...", ConsoleColor.Cyan);
                UpdateStatus("Fetching IDs...");
                ids = await Scraper.ExtractWorkshopIDs(collectionUrl);
            }
            catch (Exception ex)
            {
                Log($"Failed to fetch collection: {ex.Message}", ConsoleColor.Red);
                UpdateStatus("Error: Failed to fetch IDs");
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            if (ids.Count == 0)
            {
                Log("No items found in collection.", ConsoleColor.Yellow);
                UpdateStatus("Error: No items found");
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            totalItems = ids.Count;
            completedItems = 0;
            DownloadProgressBar.Maximum = 100;
            DownloadProgressBar.Value = 0;
            Log($"Total items to process: {totalItems}", ConsoleColor.Cyan);

            try
            {
                foreach (var (gameId, workshopId, itemName) in ids)
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        Log("Download cancelled by user.", ConsoleColor.Yellow);
                        UpdateStatus("Download cancelled");
                        break;
                    }

                    string itemPath = Path.Combine(Path.GetDirectoryName(steamCmdPath), "steamapps", "workshop", "content", gameId, workshopId);
                    if (Directory.Exists(itemPath) && Directory.GetFiles(itemPath, "*", SearchOption.AllDirectories).Any())
                    {
                        Log($"Item {workshopId} '{itemName}' already exists for AppID {gameId}.", ConsoleColor.Green);
                        successful.Add(workshopId);
                        completedItems++;
                        double itemProgress = (completedItems * 100.0) / totalItems;
                        DownloadProgressBar.Value = itemProgress;
                        UpdateStatus($"Item {workshopId} skipped ({completedItems}/{totalItems})");
                        continue;
                    }

                    UpdateStatus($"Downloading item {workshopId} '{itemName}' for AppID {gameId} ({completedItems + 1}/{totalItems})...");
                    await DownloadItem(gameId, workshopId, itemName, steamCmdPath, successful, failed, cancellationTokenSource.Token);
                    completedItems++;
                    double totalProgress = (completedItems * 100.0) / totalItems;
                    DownloadProgressBar.Value = totalProgress;
                }
            }
            catch (OperationCanceledException)
            {
                Log("Download cancelled.", ConsoleColor.Yellow);
                UpdateStatus("Download cancelled");
            }

            Log("\n===== DOWNLOAD COMPLETED =====", ConsoleColor.White);
            UpdateStatus("Download completed");
            Log($"Downloaded or skipped: {successful.Count}", ConsoleColor.Green);
            Log($"Failed: {failed.Count}", ConsoleColor.Red);
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            OpenFolderButton.IsEnabled = true;
            cancellationTokenSource.Dispose();
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            CancelButton.IsEnabled = false;
            UpdateStatus("Cancelling...");
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
                    Log("Folder opened.", ConsoleColor.Green);
                    UpdateStatus("Folder opened");
                }
                else
                {
                    Log("Folder not found.", ConsoleColor.Yellow);
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
                Log("Found steamcmd.exe.", ConsoleColor.Green);
                UpdateStatus("steamcmd.exe found");
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
                    Log("steamcmd.exe downloaded.", ConsoleColor.Green);
                    UpdateStatus("steamcmd.exe downloaded");
                    return steamCmdPath;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to download steamcmd.exe: {ex.Message}", ConsoleColor.Red);
                UpdateStatus("Error: Failed to download steamcmd.exe");
            }

            return null;
        }

        private async Task DownloadItem(string gameId, string workshopId, string itemName, string steamCmdPath, HashSet<string> successful, HashSet<string> failed, CancellationToken cancellationToken)
        {
            Log($"Starting download of item {workshopId} '{itemName}' for AppID {gameId}", ConsoleColor.Cyan);

            string arguments = $"+login anonymous +workshop_download_item {gameId} {workshopId} +quit";
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
                    Log($"Failed to start steamcmd for item {workshopId}.", ConsoleColor.Red);
                    UpdateStatus($"Error: Failed to download item {workshopId}");
                    failed.Add(workshopId);
                    return;
                }

                bool itemError = false;
                var outputTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"Error downloading item {workshopId}: {line}", ConsoleColor.Red);
                            itemError = true;
                        }
                        else if (line.Contains("progress:", StringComparison.OrdinalIgnoreCase) || line.Contains("%"))
                        {
                            var match = Regex.Match(line, @"progress: (\d+)%");
                            if (match.Success)
                            {
                                double itemProgress = double.Parse(match.Groups[1].Value);
                                double totalProgress = ((completedItems + (itemProgress / 100.0)) * 100.0) / totalItems;
                                Dispatcher.Invoke(() => DownloadProgressBar.Value = totalProgress);
                                Log($"Progress: {itemProgress}%", ConsoleColor.Cyan);
                            }
                        }
                    }
                }, cancellationToken);

                var errorTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Log($"Error downloading item {workshopId}: {line}", ConsoleColor.Red);
                        itemError = true;
                    }
                }, cancellationToken);

                await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask);

                if (!itemError && process.ExitCode == 0)
                {
                    Log($"Success: Item {workshopId} '{itemName}' downloaded.", ConsoleColor.Green);
                    successful.Add(workshopId);
                }
                else
                {
                    Log($"Error: Item {workshopId} failed to download.", ConsoleColor.Red);
                    UpdateStatus($"Error: Item {workshopId} failed");
                    failed.Add(workshopId);
                }
            }
            catch (OperationCanceledException)
            {
                Log($"Download of item {workshopId} cancelled.", ConsoleColor.Yellow);
                UpdateStatus($"Download cancelled");
                failed.Add(workshopId);
            }
            catch (Exception ex)
            {
                Log($"Error downloading item {workshopId}: {ex.Message}", ConsoleColor.Red);
                UpdateStatus($"Error: Item {workshopId} failed");
                failed.Add(workshopId);
            }
        }


        private void DeleteFailed(string gameId, string itemId)
        {

            try
            {
                string itemPath = Path.Combine(Path.GetDirectoryName(steamCmdPath), "steamapps", "workshop", "content", gameId, itemId);
                if (Directory.Exists(itemPath))
                {
                    Directory.Delete(itemPath, true);
                    Log($"Deleted incomplete item {itemId} for AppID {gameId}.", ConsoleColor.Yellow);
                }
                else
                {
                    Log($"No folder found for failed item {itemId}.", ConsoleColor.Gray);
                }
            } catch(Exception ex)
            {
                Log($"Failed to delete folder for item {itemId}.:{ex.Message}", ConsoleColor.Red);
            }

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
                        ConsoleColor.White => Brushes.WhiteSmoke,
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