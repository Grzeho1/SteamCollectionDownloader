using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamDownloader
{
    public record AuthResult(bool Success, string ErrorMessage);

    public static class SteamAuth
    {
        private enum AttemptOutcome
        {
            Success,
            NeedsGuardCode,
            InvalidPassword,
            InvalidGuardCode,
            RateLimited,
            OtherFailure
        }

        private record AttemptResult(AttemptOutcome Outcome, string Message);

        public static async Task<AuthResult> AuthenticateAsync(
            string steamCmdPath,
            string username,
            string password,
            Func<Task<string?>> guardCodeProvider,
            Action<string> logCallback,
            CancellationToken cancellationToken)
        {
            var first = await TryLoginAsync(steamCmdPath, username, password, null, logCallback, cancellationToken).ConfigureAwait(false);

            if (first.Outcome == AttemptOutcome.Success)
            {
                return new AuthResult(true, string.Empty);
            }

            if (first.Outcome != AttemptOutcome.NeedsGuardCode)
            {
                return new AuthResult(false, first.Message);
            }

            logCallback("Steam Guard code required.");
            string? code = await guardCodeProvider().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(code))
            {
                return new AuthResult(false, "Steam Guard code not provided");
            }

            var second = await TryLoginAsync(steamCmdPath, username, password, code, logCallback, cancellationToken).ConfigureAwait(false);

            if (second.Outcome == AttemptOutcome.Success)
            {
                return new AuthResult(true, string.Empty);
            }
            return new AuthResult(false, second.Message);
        }

        private static async Task<AttemptResult> TryLoginAsync(
            string steamCmdPath,
            string username,
            string password,
            string? guardCode,
            Action<string> logCallback,
            CancellationToken cancellationToken)
        {
            string arguments = guardCode != null
                ? $"+login \"{username}\" \"{password}\" \"{guardCode}\" +quit"
                : $"+login \"{username}\" \"{password}\" +quit";

            var startInfo = new ProcessStartInfo
            {
                FileName = steamCmdPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(steamCmdPath) ?? string.Empty
            };

            string steamCmdDir = Path.GetDirectoryName(steamCmdPath) ?? string.Empty;
            string consoleLogPath = Path.Combine(steamCmdDir, "logs", "console_log.txt");
            long consoleLogStartPosition = 0;
            try
            {
                if (File.Exists(consoleLogPath))
                {
                    consoleLogStartPosition = new FileInfo(consoleLogPath).Length;
                }
            }
            catch { }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new AttemptResult(AttemptOutcome.OtherFailure, "Failed to start steamcmd");
            }

            try { process.StandardInput.Close(); } catch { }

            bool loginOk = false;
            bool guardHinted = false;
            AttemptOutcome? failureOutcome = null;
            string? failureMessage = null;
            DateTime lastActivity = DateTime.UtcNow;

            void Inspect(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lastActivity = DateTime.UtcNow;
                logCallback(line.Trim());

                string upper = line.ToUpperInvariant();

                if (upper.Contains("LOGGING IN USER") && upper.Contains("OK"))
                {
                    loginOk = true;
                    return;
                }
                if (upper.Contains("WAITING FOR USER INFO") || upper.Contains("WAITING FOR LICENSE INFO"))
                {
                    loginOk = true;
                    return;
                }

                bool isGuardSuccessLine = upper.Contains("STEAM GUARD CODE PROVIDED");
                if (isGuardSuccessLine)
                {
                    // code was accepted by steamcmd — do NOT treat as a hint
                }
                else if (upper.Contains("THIS COMPUTER HAS NOT BEEN AUTHENTICATED")
                    || upper.Contains("CHECK YOUR EMAIL")
                    || upper.Contains("STEAM GUARD CODE:")
                    || upper.Contains("STEAM GUARD CODE REQUIRED")
                    || upper.Contains("TWO-FACTOR AUTHENTICATION")
                    || upper.Contains("MOBILE AUTHENTICATOR"))
                {
                    guardHinted = true;
                }

                if (failureOutcome != null) return;

                if (upper.Contains("NEED TWO FACTOR") || upper.Contains("ACCOUNTLOGONDENIEDNEEDTWOFACTOR")
                    || upper.Contains("ACCOUNT LOGON DENIED")
                    || upper.Contains("ACCOUNTLOGONDENIED")
                    || guardHinted)
                {
                    failureOutcome = AttemptOutcome.NeedsGuardCode;
                    failureMessage = "Steam Guard code required";
                }
                else if (upper.Contains("TWO-FACTOR CODE MISMATCH") || upper.Contains("TWOFACTORCODEMISMATCH")
                         || upper.Contains("STEAM GUARD CODE MISMATCH"))
                {
                    failureOutcome = AttemptOutcome.InvalidGuardCode;
                    failureMessage = "Steam Guard code is incorrect";
                }
                else if (upper.Contains("INVALID PASSWORD") || upper.Contains("INVALIDPASSWORD"))
                {
                    failureOutcome = AttemptOutcome.InvalidPassword;
                    failureMessage = "Invalid password";
                }
                else if (upper.Contains("RATELIMITEXCEEDED") || upper.Contains("RATE LIMIT")
                         || upper.Contains("ACCOUNTLOGINDENIEDTHROTTLE"))
                {
                    failureOutcome = AttemptOutcome.RateLimited;
                    failureMessage = "Steam rate limit exceeded, try again later";
                }
                else if (upper.Contains("FAILED"))
                {
                    failureOutcome = AttemptOutcome.OtherFailure;
                    failureMessage = line.Trim();
                }
            }

            var stdoutTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    Inspect(line);
                }
            }, cancellationToken);

            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    Inspect(line);
                }
            }, cancellationToken);

            var fileTailTask = Task.Run(async () =>
            {
                long position = consoleLogStartPosition;
                var lineBuffer = new StringBuilder();
                bool killedForGuard = false;

                while (!process.HasExited && !cancellationToken.IsCancellationRequested && !killedForGuard)
                {
                    try
                    {
                        if (File.Exists(consoleLogPath))
                        {
                            using var fs = new FileStream(consoleLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            if (fs.Length > position)
                            {
                                fs.Seek(position, SeekOrigin.Begin);
                                var buf = new byte[fs.Length - position];
                                int read = await fs.ReadAsync(buf, 0, buf.Length, cancellationToken).ConfigureAwait(false);
                                position += read;
                                string chunk = Encoding.UTF8.GetString(buf, 0, read);
                                foreach (char c in chunk)
                                {
                                    if (c == '\n' || c == '\r')
                                    {
                                        if (lineBuffer.Length > 0)
                                        {
                                            string fline = lineBuffer.ToString();
                                            lineBuffer.Clear();
                                            Inspect(fline);
                                            if (guardHinted && !process.HasExited)
                                            {
                                                try { process.Kill(); } catch { }
                                                killedForGuard = true;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lineBuffer.Append(c);
                                    }
                                }
                                if (lineBuffer.Length > 0)
                                {
                                    string partial = lineBuffer.ToString();
                                    string upperPartial = partial.ToUpperInvariant();
                                    bool partialSuccess = upperPartial.Contains("STEAM GUARD CODE PROVIDED");
                                    if (!partialSuccess && (upperPartial.Contains("STEAM GUARD CODE:")
                                        || upperPartial.Contains("THIS COMPUTER HAS NOT BEEN AUTHENTICATED")
                                        || upperPartial.Contains("CHECK YOUR EMAIL")))
                                    {
                                        guardHinted = true;
                                        Inspect(partial);
                                        lineBuffer.Clear();
                                        if (!process.HasExited)
                                        {
                                            try { process.Kill(); } catch { }
                                            killedForGuard = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken);

            var absoluteDeadline = DateTime.UtcNow.AddMinutes(8);
            var inactivityTimeout = TimeSpan.FromSeconds(60);
            bool timedOut = false;
            bool killedForGuardFromFile = false;

            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { }
                    return new AttemptResult(AttemptOutcome.OtherFailure, "Cancelled");
                }
                if (guardHinted && !process.HasExited)
                {
                    try { process.Kill(); } catch { }
                    killedForGuardFromFile = true;
                    break;
                }
                if (DateTime.UtcNow > absoluteDeadline)
                {
                    try { process.Kill(); } catch { }
                    timedOut = true;
                    break;
                }
                if (DateTime.UtcNow - lastActivity > inactivityTimeout)
                {
                    try { process.Kill(); } catch { }
                    timedOut = true;
                    break;
                }
                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(); } catch { }
                    return new AttemptResult(AttemptOutcome.OtherFailure, "Cancelled");
                }
            }

            if (timedOut)
            {
                if (guardHinted)
                {
                    return new AttemptResult(AttemptOutcome.NeedsGuardCode, "Steam Guard code required");
                }
                return new AttemptResult(AttemptOutcome.OtherFailure, "Authentication timed out (no output)");
            }
            if (killedForGuardFromFile)
            {
                return new AttemptResult(AttemptOutcome.NeedsGuardCode, "Steam Guard code required");
            }

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, fileTailTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

            if (loginOk && failureOutcome == null)
            {
                return new AttemptResult(AttemptOutcome.Success, string.Empty);
            }
            if (failureOutcome != null)
            {
                return new AttemptResult(failureOutcome.Value, failureMessage ?? "Login failed");
            }
            if (process.ExitCode == 0)
            {
                return new AttemptResult(AttemptOutcome.Success, string.Empty);
            }
            if (guardHinted)
            {
                return new AttemptResult(AttemptOutcome.NeedsGuardCode, "Steam Guard code required");
            }
            return new AttemptResult(AttemptOutcome.OtherFailure, $"steamcmd exited with code {process.ExitCode}");
        }
    }
}
