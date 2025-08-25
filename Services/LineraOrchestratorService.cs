// LineraOrchestratorService.cs
using System.Text.Json;
using System.Text.RegularExpressions;
using LineraOrchestrator.Models;

namespace LineraOrchestrator.Services
{
    public class LineraOrchestratorService
    {
        private readonly LineraCliRunner _cli;
        private readonly LineraConfig _config;
        private const string PublisherChainId = "aee928d4bf3880353b4a3cd9b6f88e6cc6e5ed050860abae439e7782e9b2dfe8";
        public LineraOrchestratorService(LineraCliRunner cli, LineraConfig config)
        {
            _cli = cli;
            _config = config;
        }

        // Khởi động Linera Net trong nền và trích xuất các biến môi trường
        public async Task<LineraConfig> StartLineraNetAsync()
        {
            try
            {
                StopLineraNode();
                await Task.Delay(1000);
                //1) Chạy lệnh "linera net up" trong nền và lấy các biến môi trường
                var message = await _cli.StartLineraNetInBackgroundAsync();

                //2) Trích xuất các biến môi trường từ log file
                var wallet = ExtractEnvironmentVariable("/tmp/linera_output.log", "LINERA_WALLET");
                var storage = ExtractEnvironmentVariable("/tmp/linera_output.log", "LINERA_STORAGE");

                // Set vào cấu hình LineraConfig
                _config.LineraWallet = wallet;
                _config.LineraStorage = storage;

                // Log thông tin sau khi set biến môi trường
                Console.WriteLine($"LINERA_WALLET: {_config.LineraWallet}");
                Console.WriteLine($"LINERA_STORAGE: {_config.LineraStorage}");

                // **Kiểm tra và đảm bảo các phương thức publish được gọi**
                if (string.IsNullOrEmpty(wallet) || string.IsNullOrEmpty(storage))
                {
                    throw new InvalidOperationException("Environment variables not set.");
                }
                await Task.Delay(5000);
                // **Export biến môi trường cho linera**
                Environment.SetEnvironmentVariable("LINERA_WALLET", _config.LineraWallet);
                Environment.SetEnvironmentVariable("LINERA_STORAGE", _config.LineraStorage);
                await Task.Delay(2000); // Chờ 2 giây để mạng Linera ổn định
                var leaderboardAppId = await PublishAndCreateLeaderboardAppAsync();
                Console.WriteLine($"Leaderboard App ID: {leaderboardAppId}");
                await Task.Delay(1000);
                var moduleXfighter = await PublishXfighterModuleAsync();
                Console.WriteLine($"Module XFighter: {moduleXfighter}");
                await Task.Delay(1000);              
                var xfighterAppID = await PublishAndCreateXfighterAppAsync(leaderboardAppId);
                Console.WriteLine($"XFighter App ID: {xfighterAppID}");

                return _config;                 // Trả về đối tượng LineraConfig đã được cập nhật
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error: {ex.Message}");
            }
        }

        // Dừng tiến trình Linera Node nếu nó đang chạy.
        public void StopLineraNode()
        {
            if (_config.LineraNetPid.HasValue)
            {
                try
                {
                    _cli.StopProcess(_config.LineraNetPid.Value);
                    _config.LineraNetPid = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to stop Linera process: {ex.Message}");
                }
            }
        }
        //B2: Phương thức trích xuất biến môi trường từ log file -
        private static string? ExtractEnvironmentVariable(string logFilePath, string variableName)
        {
            if (!File.Exists(logFilePath)) return null;
            var lines = File.ReadAllLines(logFilePath);
            foreach (var line in lines)
            {
                if (line.Contains(variableName))
                {
                    var match = Regex.Match(line, @$"export {variableName}=""([^""]+)""");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            return null; // trả về null nếu không tìm thấy
        }
        // Phương thức sử dụng publish để tạo XFighter Module 
        public async Task<string> PublishXfighterModuleAsync()
        {
            var contractPath = Path.Combine(_config.XFighterPath, "xfighter_contract.wasm");
            var servicePath = Path.Combine(_config.XFighterPath, "xfighter_service.wasm");

            var arguments = new[]
            {
                    "publish-module",
                    contractPath,
                    servicePath,
                    PublisherChainId
            };

            string result = await _cli.RunAndCaptureOutputAsync(arguments);
            Console.WriteLine("Raw CLI Output (Xfighter Publish Module):");
            Console.WriteLine(result);

            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("No output returned when publishing Xfighter module.");

            var moduleId = result.Trim(); // Fallback → parse raw string
            Console.WriteLine($"Successfully published Xfighter module with ID: {moduleId}");
            return moduleId;
        }


        // Phương thức sử dụng publish-and-create để tạo Leaderboard APPID (chuẩn JSON)
        public async Task<string> PublishAndCreateLeaderboardAppAsync()
        {
            var contractPath = Path.Combine(_config.LeaderboardPath, "leaderboard_contract.wasm");
            var servicePath = Path.Combine(_config.LeaderboardPath, "leaderboard_service.wasm");

            var result = await _cli.RunAndCaptureOutputAsync(
                "publish-and-create",
                contractPath,
                servicePath,
                "--json-argument",
                "null"
            );

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new Exception("Failed to publish and create leaderboard app: no output returned.");
            }

            return result.Trim(); // Trả về App ID dạng hex string
        }

        // Tạo XFighter AppID
        public async Task<string> PublishAndCreateXfighterAppAsync(string leaderboardAppId)
        {
            var contractPath = Path.Combine(_config.XFighterPath, "xfighter_contract.wasm");
            var servicePath = Path.Combine(_config.XFighterPath, "xfighter_service.wasm");

            var arguments = new[]
            {
                "publish-and-create",
                contractPath,
                servicePath,
                "--json-argument", $"{{\"leaderboard\":\"{leaderboardAppId}\"}}",
                "--json-parameters", $"{{\"publisher\":\"{PublisherChainId}\"}}"
            };

            string result = await _cli.RunAndCaptureOutputAsync(arguments);
            Console.WriteLine("Raw CLI Output (Xfighter Publish&Create):");
            Console.WriteLine(result);

            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("No output returned when creating Xfighter app.");

            var xfighterAppId = result.Trim(); // Fallback → parse raw string
            Console.WriteLine($"Successfully created Xfighter app with ID: {xfighterAppId}");
            return xfighterAppId;
        }

    }
}