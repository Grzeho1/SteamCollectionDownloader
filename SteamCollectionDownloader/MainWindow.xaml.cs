using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
            LoadConfig();
        }

        private Dictionary<string, string> ReadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to read config: {ex.Message}", ConsoleColor.Yellow);
            }
            return new Dictionary<string, string>();
        }

        private void WriteConfig(Dictionary<string, string> config)
        {
            try
            {
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config));
            }
            catch (Exception ex)
            {
                Log($"Failed to write config: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        private void LoadConfig()
        {
            var config = ReadConfig();
            if (config.TryGetValue("LastUrl", out var url))
            {
                CollectionUrlTextBox.Text = url;
            }
            if (config.TryGetValue("LastUsername", out var user) && !string.IsNullOrWhiteSpace(user))
            {
                UsernameTextBox.Text = user;
            }
            if (config.TryGetValue("EncryptedPassword", out var enc) && !string.IsNullOrWhiteSpace(enc))
            {
                try
                {
                    PasswordBox.Password = CredentialStore.Decrypt(enc);
                    RememberPasswordCheckBox.IsChecked = true;
                }
                catch (Exception ex)
                {
                    Log($"Failed to decrypt saved password: {ex.Message}", ConsoleColor.Yellow);
                }
            }
        }

        private void SaveConfig(string url, string? username, string? encryptedPassword)
        {
            var config = ReadConfig();
            config["LastUrl"] = url ?? string.Empty;
            if (username != null)
            {
                config["LastUsername"] = username;
            }
            else
            {
                config.Remove("LastUsername");
            }
            if (encryptedPassword != null)
            {
                config["EncryptedPassword"] = encryptedPassword;
            }
            else
            {
                config.Remove("EncryptedPassword");
            }
            WriteConfig(config);
        }

        private void LoginMode_Changed(object sender, RoutedEventArgs e)
        {
            if (CredentialsPanel == null) return;
            CredentialsPanel.Visibility = (AccountRadio.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
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

            if (string.IsNullOrWhiteSpace(collectionUrl))
            {
                Log("No URL provided.", ConsoleColor.Yellow);
                UpdateStatus("Error: No URL provided");
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = true;
                return;
            }

            bool useAccount = AccountRadio.IsChecked == true;
            string loginUser = "anonymous";
            string? usernameToSave = null;
            string? encryptedPasswordToSave = null;

            if (useAccount)
            {
                string username = UsernameTextBox.Text.Trim();
                string password = PasswordBox.Password;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    Log("Username and password required for account login.", ConsoleColor.Yellow);
                    UpdateStatus("Error: Missing credentials");
                    StartButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                    OpenFolderButton.IsEnabled = true;
                    return;
                }

                Log($"Authenticating as '{username}'...", ConsoleColor.Cyan);
                UpdateStatus("Authenticating...");

                var authResult = await SteamAuth.AuthenticateAsync(
                    steamCmdPath,
                    username,
                    password,
                    PromptForGuardCodeAsync,
                    line => Log($"[auth] {line}", ConsoleColor.Gray),
                    cancellationTokenSource.Token);

                if (!authResult.Success)
                {
                    Log($"Authentication failed: {authResult.ErrorMessage}", ConsoleColor.Red);
                    UpdateStatus("Authentication failed");
                    StartButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                    OpenFolderButton.IsEnabled = true;
                    return;
                }

                Log("Authentication successful.", ConsoleColor.Green);
                loginUser = username;
                usernameToSave = username;
                if (RememberPasswordCheckBox.IsChecked == true)
                {
                    try
                    {
                        encryptedPasswordToSave = CredentialStore.Encrypt(password);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to encrypt password: {ex.Message}", ConsoleColor.Yellow);
                    }
                }
            }

            SaveConfig(collectionUrl, usernameToSave, encryptedPasswordToSave);

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

            var toDownload = new List<(string gameId, string workshopId, string itemName)>();
            foreach (var item in ids)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested) break;

                string itemPath = Path.Combine(Path.GetDirectoryName(steamCmdPath), "steamapps", "workshop", "content", item.gameId, item.workshopId);
                if (Directory.Exists(itemPath) && Directory.GetFiles(itemPath, "*", SearchOption.AllDirectories).Any())
                {
                    Log($"Item {item.workshopId} '{item.itemName}' already exists.", ConsoleColor.Green);
                    successful.Add(item.workshopId);
                    completedItems++;
                    DownloadProgressBar.Value = (completedItems * 100.0) / totalItems;
                }
                else
                {
                    toDownload.Add(item);
                }
            }

            try
            {
                if (toDownload.Count > 0 && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Log($"Batching {toDownload.Count} items into one steamcmd run.", ConsoleColor.Cyan);
                    Log("This may take a while depending on collection size - download runs in the background, you can keep using your PC.", ConsoleColor.Yellow);
                    UpdateStatus($"Downloading {toDownload.Count} items in background - this may take a while...");
                    await DownloadAll(toDownload, steamCmdPath, loginUser, successful, failed, cancellationTokenSource.Token);
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

        private async Task DownloadAll(List<(string gameId, string workshopId, string itemName)> items, string steamCmdPath, string loginUser, HashSet<string> successful, HashSet<string> failed, CancellationToken cancellationToken)
        {
            if (items.Count == 0) return;

            var nameMap = items.ToDictionary(i => i.workshopId, i => (i.gameId, i.itemName));

            var sb = new StringBuilder();
            sb.Append($"+login {loginUser}");
            foreach (var (gameId, workshopId, _) in items)
            {
                sb.Append($" +workshop_download_item {gameId} {workshopId}");
            }
            sb.Append(" +quit");

            var startInfo = new ProcessStartInfo
            {
                FileName = steamCmdPath,
                Arguments = sb.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log("Failed to start steamcmd.", ConsoleColor.Red);
                foreach (var item in items) failed.Add(item.workshopId);
                return;
            }

            using var killOnCancel = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            });

            void ParseLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                var startMatch = Regex.Match(line, @"Downloading item (\d+)");
                if (startMatch.Success)
                {
                    string id = startMatch.Groups[1].Value;
                    string name = nameMap.TryGetValue(id, out var info) ? info.itemName : id;
                    UpdateStatus($"Downloading '{name}' ({completedItems + 1}/{totalItems})");
                    Log($"Downloading {id} '{name}'", ConsoleColor.Cyan);
                    return;
                }

                var successMatch = Regex.Match(line, @"Success\..*?item (\d+)");
                if (successMatch.Success)
                {
                    string id = successMatch.Groups[1].Value;
                    string name = nameMap.TryGetValue(id, out var info) ? info.itemName : id;
                    if (!successful.Contains(id))
                    {
                        successful.Add(id);
                        Interlocked.Increment(ref completedItems);
                        Dispatcher.Invoke(() => DownloadProgressBar.Value = (completedItems * 100.0) / totalItems);
                    }
                    Log($"Success: {id} '{name}'", ConsoleColor.Green);
                    return;
                }

                var failMatch = Regex.Match(line, @"ERROR.*?[Dd]ownload(?:ing)? [Ii]tem (\d+).*?\((.+?)\)");
                if (failMatch.Success)
                {
                    string id = failMatch.Groups[1].Value;
                    string reason = failMatch.Groups[2].Value;
                    string name = nameMap.TryGetValue(id, out var info) ? info.itemName : id;
                    if (!failed.Contains(id))
                    {
                        failed.Add(id);
                        Interlocked.Increment(ref completedItems);
                        Dispatcher.Invoke(() => DownloadProgressBar.Value = (completedItems * 100.0) / totalItems);
                    }
                    Log($"Failed: {id} '{name}' ({reason})", ConsoleColor.Red);
                    if (nameMap.TryGetValue(id, out var fi)) DeleteFailed(fi.gameId, id);
                    return;
                }
            }

            var outputTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    ParseLine(line);
                }
            });

            var errorTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) != null)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"stderr: {line}", ConsoleColor.Red);
                    }
                }
            });

            try
            {
                await process.WaitForExitAsync();
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (OperationCanceledException)
            {
                Log("Download cancelled.", ConsoleColor.Yellow);
            }

            foreach (var item in items)
            {
                if (successful.Contains(item.workshopId) || failed.Contains(item.workshopId)) continue;

                string itemPath = Path.Combine(Path.GetDirectoryName(steamCmdPath) ?? string.Empty, "steamapps", "workshop", "content", item.gameId, item.workshopId);
                if (Directory.Exists(itemPath) && Directory.GetFiles(itemPath, "*", SearchOption.AllDirectories).Any())
                {
                    successful.Add(item.workshopId);
                    Log($"Success (verified on disk): {item.workshopId} '{item.itemName}'", ConsoleColor.Green);
                }
                else
                {
                    failed.Add(item.workshopId);
                    Log($"Failed (no output, no files): {item.workshopId} '{item.itemName}'", ConsoleColor.Red);
                }
                Interlocked.Increment(ref completedItems);
                Dispatcher.Invoke(() => DownloadProgressBar.Value = (completedItems * 100.0) / totalItems);
            }
        }


        private void DeleteFailed(string gameId, string itemId)
        {

            try
            {
                string steamRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Steamcmd"));
                string itemPath = Path.Combine(steamRoot, "steamapps", "workshop", "content", gameId, itemId);

                if (Directory.Exists(itemPath))
                {
                    Log($"Deleting folder for failed item {itemId}...", ConsoleColor.Yellow);
                    Directory.Delete(itemPath, true);
                    Log($"Deleted folder for failed item {itemId}.", ConsoleColor.Yellow);
                }
                else
                {
                    Log($"No folder found for failed item {itemId}.", ConsoleColor.Gray);
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to delete folder for {itemId}: {ex.Message}", ConsoleColor.Red);
            }

        }



 
        private Task<string?> PromptForGuardCodeAsync()
        {
            var tcs = new TaskCompletionSource<string?>();
            Dispatcher.Invoke(() =>
            {
                UpdateStatus("Waiting for Steam Guard code...");
                var dialog = new SteamGuardDialog { Owner = this };
                bool? result = dialog.ShowDialog();
                tcs.SetResult(result == true ? dialog.Code : null);
            });
            return tcs.Task;
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