// LineraCliRunner.cs
using System.Diagnostics;
using System.Text;
using LineraOrchestrator.Models;

namespace LineraOrchestrator.Services
{
    public class LineraCliRunner
    {
        private readonly LineraConfig _config;

        public LineraCliRunner(LineraConfig config)
        {
            _config = config;
        }

        // vẫn dùng bash để start net up ở background (nohup), giữ nguyên phương thức này
        public int StartBackgroundProcess(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"nohup {_config.LineraCliPath} {args} > /tmp/linera_output.log 2>&1 & echo $!\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi };
            process.Start();

            string pidString = process.StandardOutput.ReadToEnd().Trim();
            if (int.TryParse(pidString, out int pid))
            {
                return pid;
            }
            throw new InvalidOperationException("Failed to get PID of background process.");
        }

        public void StopProcess(int pid)
        {
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (ArgumentException)
            {
                // process already stopped
            }
        }

        // ----- CHÍNH: chạy linera CLI mà KHÔNG qua shell, từng arg riêng -----
        // Sử dụng params string[] args: hãy gọi _cli.RunAndCaptureOutputAsync("publish-module", path1, path2, "--json-argument", json)
        // Thiết lập env cho tiến trình con trực tiếp: RunAndCaptureOutputAsync
        // Đảm bảo tiến trình linera spawn từ dotnet có đúng env,
        // vì export trong shell khác không tự lan sang tiến trình dotnet.
        public async Task<string> RunAndCaptureOutputAsync(params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.LineraCliPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var a in args)
                psi.ArgumentList.Add(a);

            if (!string.IsNullOrEmpty(_config.LineraWallet))
                psi.Environment["LINERA_WALLET"] = _config.LineraWallet;
            if (!string.IsNullOrEmpty(_config.LineraStorage))
                psi.Environment["LINERA_STORAGE"] = _config.LineraStorage;
            if (!string.IsNullOrEmpty(_config.LineraKeystore))
                psi.Environment["LINERA_KEYSTORE"] = _config.LineraKeystore;

            var process = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            var errSb = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errSb.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var err = errSb.ToString();
                var outp = sb.ToString();
                Console.WriteLine($"Process exit code {process.ExitCode}");
                Console.WriteLine("STDERR:");
                Console.WriteLine(err);
                Console.WriteLine("STDOUT:");
                Console.WriteLine(outp);
                throw new InvalidOperationException($"linera exited with code {process.ExitCode}: {err}");
            }

            return sb.ToString();
        }

        // Keep existing background-start helper which reads /tmp/linera_output.log
        public async Task<LineraConfig> StartLineraNetInBackgroundAsync()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"nohup {_config.LineraCliPath} net up > /tmp/linera_output.log 2>&1 & echo $!\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi };
            process.Start();

            string pidStr = process.StandardOutput.ReadToEnd().Trim();
            if (!int.TryParse(pidStr, out int pid))
                throw new InvalidOperationException("Không lấy được PID của linera net up");

            _config.LineraNetPid = pid;

            string? wallet = null;
            string? storage = null;
            int retries = 60; // 30s
            while ((wallet == null || storage == null) && retries-- > 0)
            {
                if (File.Exists("/tmp/linera_output.log"))
                {
                    var lines = File.ReadAllLines("/tmp/linera_output.log");
                    foreach (var line in lines)
                    {
                        if (wallet == null && line.Contains("export LINERA_WALLET"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(line, @"export LINERA_WALLET=""([^""]+)""");
                            if (match.Success) wallet = match.Groups[1].Value;
                        }
                        if (storage == null && line.Contains("export LINERA_STORAGE"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(line, @"export LINERA_STORAGE=""([^""]+)""");
                            if (match.Success) storage = match.Groups[1].Value;
                        }
                    }
                }
                await Task.Delay(500);
            }

            _config.LineraWallet = wallet;
            _config.LineraStorage = storage;
            _config.LineraKeystore = null;
            return _config;
        }
        // Automatic Orchestrator Linera Services

        public async Task<int> StartLineraServiceInBackgroundAsync(int port = 8080)
        {
            string logFile = "/tmp/linera_service.log";
            if (File.Exists(logFile)) File.Delete(logFile);

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"nohup {_config.LineraCliPath} service --port {port} > {logFile} 2>&1 & echo $!\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(_config.LineraWallet))
                psi.Environment["LINERA_WALLET"] = _config.LineraWallet;
            if (!string.IsNullOrEmpty(_config.LineraStorage))
                psi.Environment["LINERA_STORAGE"] = _config.LineraStorage;
            if (!string.IsNullOrEmpty(_config.LineraKeystore))
                psi.Environment["LINERA_KEYSTORE"] = _config.LineraKeystore;

            var process = new Process { StartInfo = psi };
            process.Start();

            string pidStr = await process.StandardOutput.ReadToEndAsync();
            if (!int.TryParse(pidStr?.Trim(), out int pid))
                throw new InvalidOperationException("Không lấy được PID của linera service khi start background.");

            _config.LineraServicePid = pid;

            Console.WriteLine($"[DEBUG] Linera service started in background with PID={pid}, log={logFile}");

            // Task nền theo dõi log để báo READY
            _ = Task.Run(async () =>
            {
                try
                {
                    using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    while (true)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null)
                        {
                            await Task.Delay(500);
                            continue;
                        }
                        if (line.Contains("GraphiQL IDE"))
                        {
                            Console.WriteLine($"[READY] Linera service is ready on port {port}");
                            break;
                        }
                        if (line.Contains("error") || line.Contains("panic"))
                        {
                            Console.WriteLine($"[ERROR] Linera service failed: {line}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Readiness check failed: {ex.Message}");
                }
            });

            return pid;
        }
    }
}
