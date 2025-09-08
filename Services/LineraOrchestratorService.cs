// LineraOrchestratorService.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LineraOrchestrator.Models;
using System.Linq;

namespace LineraOrchestrator.Services
{
    public class LineraOrchestratorService
    {
        private readonly LineraCliRunner _cli;
        private readonly LineraConfig _config;
        private const string PublisherChainId = "aee928d4bf3880353b4a3cd9b6f88e6cc6e5ed050860abae439e7782e9b2dfe8";
        private readonly string _deploymentIdsPath;//kiểm tra những lần deploy để debug thời gian đầu
        public LineraOrchestratorService(LineraCliRunner cli, LineraConfig config)
        {
            _cli = cli;
            _config = config;
            // deployment file in current working dir (project root). Change if you prefer different path.
            _deploymentIdsPath = Path.Combine(Directory.GetCurrentDirectory(), "deployment_ids.json");

            LoadMatchMapping(); // Tải dữ liệu khi khởi động
        }
        /// <summary>
        /// Useful for controller to return LINERA_WALLET/LINERA_STORAGE quickly trả các biến môi trường về config
        /// </summary>
        public LineraConfig GetCurrentConfig()
        {
            return _config;
        }
        #region Linera service lifecycle & helpers
        // Khởi động Linera Net trong nền và trích xuất các biến môi trường
        public async Task<LineraConfig> StartLineraNetAsync()
        {
            try
            {
                Console.WriteLine($" Clean Linera Node + Service...!"); //Dọn dẹp các tiến trình Linera cũ
                await StopLineraServiceAsync();
                StopLineraNode();           
                
                //1) Chạy lệnh "linera net up" trong nền và lấy các biến môi trường
                var message = await _cli.StartLineraNetInBackgroundAsync();
                Console.WriteLine($"Linera Node Running");

                //2) Trích xuất các biến môi trường từ log file
                var wallet = ExtractEnvironmentVariable("/tmp/linera_output.log", "LINERA_WALLET");
                var storage = ExtractEnvironmentVariable("/tmp/linera_output.log", "LINERA_STORAGE");

                // **Kiểm tra và đảm bảo các phương thức publish được gọi**
                if (string.IsNullOrEmpty(wallet) || string.IsNullOrEmpty(storage))
                {
                    throw new InvalidOperationException("Environment variables not set.");
                }

                // Set vào cấu hình LineraConfig
                _config.LineraWallet = wallet;
                _config.LineraStorage = storage;
                // Log thông tin sau khi set biến môi trường
                Console.WriteLine($"Successfully export Environment variables");

                Console.WriteLine($"LINERA_WALLET: {_config.LineraWallet}");
                Console.WriteLine($"LINERA_STORAGE: {_config.LineraStorage}");
            
                // **Export biến môi trường cho linera**
                Environment.SetEnvironmentVariable("LINERA_WALLET", _config.LineraWallet);
                Environment.SetEnvironmentVariable("LINERA_STORAGE", _config.LineraStorage);
                await Task.Delay(1000); // Chờ 2 giây để mạng Linera ổn định

                var moduleXfighter = await PublishXfighterModuleAsync();
                await Task.Delay(200);
                Console.WriteLine($"Module XFighter : \n{moduleXfighter}");

                var leaderboardAppId = await PublishAndCreateLeaderboardAppAsync();
                await Task.Delay(200);
                Console.WriteLine($"Leaderboard App ID :\n{leaderboardAppId}");
                               
                var xfighterAppID = await PublishAndCreateXfighterAppAsync(leaderboardAppId);
                await Task.Delay(200);
                Console.WriteLine($"XFighter App ID : \n{xfighterAppID}");
                
                // Set vào cấu hình LineraConfig
                _config.LeaderboardAppId = leaderboardAppId;
                _config.XFighterModuleId = moduleXfighter;      
                _config.XFighterAppId = xfighterAppID;

                // Sau khi tạo leaderboard, module, app xong
                await StartLineraServiceAsync();
                Console.WriteLine("Linera service started after node initialization.");
                Console.WriteLine("Linera Ready For MatchMaking System!");
                return _config; // Trả về đối tượng LineraConfig đã được cập nhật
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
        // Trích xuất biến môi trường từ log file 
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
            Console.WriteLine("Raw CLI Output (Xfighter Publish Module):" + result);

            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("No output returned when publishing Xfighter module.");

            var moduleId = result.Trim(); // Fallback → parse raw string
            // An extra step to save into LineraConfig to check check for potential errors
            _config.XFighterModuleId = moduleId;
            AppendDeploymentIdToFile("XFighterModuleId", moduleId); // Persist to file for recovery

            Console.WriteLine($"Successfully published Xfighter module with ID: {moduleId}");
            return moduleId;
        }
        // Phương thức sử dụng publish-and-create để tạo Leaderboard APPID (raw output fallback)
        public async Task<string> PublishAndCreateLeaderboardAppAsync()
        {
            var contractPath = Path.Combine(_config.LeaderboardPath, "leaderboard_contract.wasm");
            var servicePath = Path.Combine(_config.LeaderboardPath, "leaderboard_service.wasm");

            var result = await _cli.RunAndCaptureOutputAsync(
                "publish-and-create",
                contractPath,
                servicePath,
                PublisherChainId,
                "--json-argument",
                "null"
            );

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new Exception("Failed to publish and create leaderboard app: no output returned.");
            }
            var leaderboardAppId = result.Trim();

            // An extra step to save into LineraConfig to check check for potential errors 
            _config.LeaderboardAppId = leaderboardAppId;
            AppendDeploymentIdToFile("LeaderboardAppId", leaderboardAppId);

            return leaderboardAppId; // Trả về App ID dạng hex string
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
            
            _config.XFighterAppId = xfighterAppId;// An extra step to save into LineraConfig to check check for potential errors
            AppendDeploymentIdToFile("XFighterAppId", xfighterAppId);

            Console.WriteLine($"Successfully created Xfighter app with ID: {xfighterAppId}");
            return xfighterAppId;
        }
        // Saving here to debug previous potential errors \\wsl.localhost\Ubuntu\home\roycrypto\LineraOrchestrator
        private void AppendDeploymentIdToFile(string key, string id)
        {
            try
            {
                var line = $"{DateTime.UtcNow:O} {key}: {id}";
                // Append text atomically with File.AppendAllLines (writes a single line)
                File.AppendAllLines(_deploymentIdsPath, new[] { line });
            }
            catch (Exception ex)
            {
                // Không throw để không làm gián đoạn flow; chỉ log để debug.
                Console.WriteLine($"Warning: failed to append deployment id to file: {ex.Message}");
            }
        }
        
        // Tới đây là xong bước khởi tạo flow start net → publish leaderboard → publish module → create app
        // sang bước tiếp theo open-match-chain và create-xfighter-app sau matchmaking
        // ==================== New: Open-match-chain + ResolveChainId + CreateXfighterAppOnChain ====================
        // helper: find first 64-hex token anywhere in text
        private static string? FirstHex64(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, @"\b([0-9a-f]{64})\b", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }
        // helper: parse last non-empty line (node style)
        private static string ParseLastLineAsId(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout)) return string.Empty;
            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(l => l.Trim())
                              .Where(l => !string.IsNullOrEmpty(l))
                              .ToArray();
            return lines.Length == 0 ? string.Empty : lines.Last();
        }

        // Parse wallet chains:linera wallet show, fallback to regex extraction
        private async Task<List<string>> ParseWalletChainsAsync()
        {
            var list = new List<string>();
            string text;
            try
            {
                text = await _cli.RunAndCaptureOutputAsync("wallet", "show");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: 'linera wallet show' failed: {ex.Message}");
                return list;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                var matches = Regex.Matches(text, @"\b([0-9a-f]{64})\b", RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                    list.Add(m.Groups[1].Value.ToLowerInvariant());
            }

            return list;
        }
        // ResolveChainIdAsync: exact chain id, fallback to raw if hex64
        public async Task<string> ResolveChainIdAsync(string? requestedChain = null)
        {
            if (!string.IsNullOrWhiteSpace(requestedChain) &&
                Regex.IsMatch(requestedChain, @"^[0-9a-f]{64}$", RegexOptions.IgnoreCase))
            {
                return requestedChain.ToLowerInvariant();
            }

            var chains = await ParseWalletChainsAsync();
            if (chains.Count == 0)
                throw new InvalidOperationException("No chain IDs found in wallet.");

            return chains.First();
        }
        // OpenMatchChainAsync: run `linera open-chain` and parse new chain ID
        public async Task<string> OpenMatchChainAsync()
        {
            Console.WriteLine($"Calling opening xfighter match chainID onchain...");
            await Task.Delay(500);
            var output = await _cli.RunAndCaptureOutputAsync("open-chain");
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("open-chain returned no output.");

            var chainId = FirstHex64(output);
            if (!string.IsNullOrEmpty(chainId)) return chainId;

            var last = ParseLastLineAsId(output);
            if (!string.IsNullOrEmpty(last) && Regex.IsMatch(last, @"^[0-9a-f]{64}$", RegexOptions.IgnoreCase))
                return last;

            throw new InvalidOperationException($"Failed to parse chain id from open-chain output. Raw: {output}");
        }

        // CreateXfighterAppOnChainAsync: create application from module on chainCandidate (resolve + retry)
      public async Task<string> CreateXfighterAppOnChainAsync(string moduleId, string chainCandidate, string leaderboardAppId, int maxRetries = 5, int retryDelayMs = 2000)
        {
            if (string.IsNullOrWhiteSpace(moduleId)) throw new ArgumentException("moduleId required.");
            if (string.IsNullOrWhiteSpace(chainCandidate)) throw new ArgumentException("chainCandidate required.");

            var chainId = await ResolveChainIdAsync(chainCandidate);
            var jsonArg = JsonSerializer.Serialize(new { leaderboard = leaderboardAppId });

            Exception? lastEx = null;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Console.WriteLine($"Calling creating xfighter match appID onchain...");
                    await Task.Delay(500);
                    Console.WriteLine($"Attempt {i + 1}/{maxRetries} to create app from module {moduleId} " +
                        $"on chain {chainId}...");
                    var result = await _cli.RunAndCaptureOutputAsync("create-application", 
                        moduleId, 
                        chainId, 
                        "--json-argument", 
                        jsonArg);

                    if (string.IsNullOrWhiteSpace(result))
                        throw new InvalidOperationException("create-application returned empty output.");

                    var newAppId = ParseLastLineAsId(result);
                    if (string.IsNullOrWhiteSpace(newAppId))
                        throw new InvalidOperationException($"create-application returned output but could not parse App ID. Raw: {result}");

                    Console.WriteLine($"Created new application on chain {chainId} with ID: {newAppId}");
                    return newAppId;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Console.WriteLine($"Attempt {i + 1} failed: {ex.Message}");
                    if (i < maxRetries - 1) await Task.Delay(retryDelayMs);
                }
            }
            throw new InvalidOperationException($"Failed to create xfighter app after {maxRetries} attempts. Last error: {lastEx?.Message}", lastEx);
        }
        // ==================== End new methods ====================
        // Tới đây là xong bước khởi tạo flow start net → publish leaderboard → publish module → create app

        // ==================== Start Linera Service Automatic ====================
        // Dừng tiến trình Linera Service nếu nó đang chạy.
        // Wrapper để gọi LineraCliRunner.StartLineraServiceInBackgroundAsync
        public async Task<int> StartLineraServiceAsync(int port = 8080)
        {
            Console.WriteLine($"Starting Linera service. " +
                $"LINERA_WALLET={_config.LineraWallet}, " +
                $"LINERA_STORAGE={_config.LineraStorage}");

            if (string.IsNullOrEmpty(_config.LineraWallet) || string.IsNullOrEmpty(_config.LineraStorage))
                throw new InvalidOperationException("LINERA_WALLET or LINERA_STORAGE not set. Call StartLineraNetAsync() first.");

            if (_config.LineraServicePid.HasValue)
            {
                try
                {
                    Process.GetProcessById(_config.LineraServicePid.Value);
                    Console.WriteLine($"Linera service already running (PID {_config.LineraServicePid}).");
                    return _config.LineraServicePid.Value;
                }
                catch (ArgumentException)
                {
                    _config.LineraServicePid = null; // process không tồn tại
                }
            }

            int pid = await _cli.StartLineraServiceInBackgroundAsync(port);
            _config.LineraServicePid = pid;
            Console.WriteLine($"Started Linera service (PID {pid}). Logs: /tmp/linera_service.log");
            return pid;
        }

        // Async stop wrapper (uses LineraCliRunner.StopProcess)
        public async Task StopLineraServiceAsync(int waitMs = 500)
        {
            if (!_config.LineraServicePid.HasValue)
            {
                Console.WriteLine("Linera service not running (no PID).");
                return;
            }

            var pid = _config.LineraServicePid.Value;
            try
            {
                Console.WriteLine($"Stopping Linera service (PID {pid}) with graceful kill...");
                _cli.StopProcess(pid); // your StopProcess in LineraCliRunner          
                await Task.Delay(waitMs); // Chờ một chút để tiến trình tự dừng
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: StopLineraService failed: {ex.Message}");
            }

            // Wait a bit to let RocksDB unlock
            await Task.Delay(waitMs);
            _config.LineraServicePid = null;
            Console.WriteLine("Linera service stopped.");
        }
        //==================== [NEW] Helper: check if Linera Service is running by PID ====================
      
        private bool IsLineraServiceRunning
        {
            get
            {
                if (!_config.LineraServicePid.HasValue) return false;
                try
                {
                    var process = Process.GetProcessById(_config.LineraServicePid.Value);
                    return !process.HasExited;
                }
                catch
                {
                    return false; // process không tồn tại
                }
            }
        }

        // Helper to call to Unity and Get Linera Service Status Text
        /// <summary>
        /// Kiểm tra xem Linera service đang chạy hay không.
        /// Trả về true nếu LineraServicePid có giá trị và tiến trình vẫn tồn tại.
        /// Nếu tiến trình không tồn tại nữa, sẽ clear LineraServicePid và trả về false.
        /// </summary>
        public int? GetServicePid() => _config.LineraServicePid;

        public bool IsServiceRunning()
        {
            if (!_config.LineraServicePid.HasValue)
            {
                Console.WriteLine("[Linera] Service status check: no PID stored.");
                return false;
            }

            try
            {
                var pid = _config.LineraServicePid.Value;
                var proc = Process.GetProcessById(pid);
                Console.WriteLine($"[Linera] Service running with PID={pid}, StartTime={proc.StartTime}");
                return true;
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"[Linera] No process found for PID={_config.LineraServicePid}. Clearing PID.");
                _config.LineraServicePid = null;
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Linera] Error checking service: {ex.Message}");
                return false;
            }
        }
        #endregion Linera service lifecycle & helpers

        #region Linera open chain, submit match data, get leaderboard 
        // [NEW] Unified open+create with service stop/start
        // Open-and-create: chỉ một process chạy tại một thời điểm
        //Step 1: If multiple requests arrive, Multiple [QUEUE] open-and-create requests queued,
        //- but only 1 lock acquired runs first, the following ones will wait. When the first one releases,
        //- the lock is released and then the next lock acquired.
        //Step 2: Add log as request ID (eg: Guid.NewGuid().ToString("N").Substring(0,6)) to easily track each request
        //Step 3: [NEWPATCH] OpenAndCreateWithServiceControlAsync – thêm lưu mapping bằng matchId
        // Put Linera service in Queue update 3 Sep 25
        private readonly SemaphoreSlim _openAndCreateLock = new SemaphoreSlim(1, 1);
        public async Task<(string chainId, string appId)> OpenAndCreateWithServiceControlAsync(
            string moduleId, string leaderboardAppId, string? matchId = null,
            int maxRetries = 5, int retryDelayMs = 2000)
        {
            var requestId = Guid.NewGuid().ToString("N").Substring(0, 6);
            Console.WriteLine($"[QUEUE:{requestId}] Request queued at {DateTime.UtcNow:O}, CurrentCount={_openAndCreateLock.CurrentCount}");

            bool lockAcquired = await _openAndCreateLock.WaitAsync(1000);
            if (!lockAcquired)
            {
                throw new InvalidOperationException("Failed to acquire lock for creating chain and app.");
            }
            Console.WriteLine($"[QUEUE:{requestId}] Lock acquired at {DateTime.UtcNow:O}");

            try
            {
                // Bước 1: Dừng dịch vụ hiện tại và chờ nó dừng hẳn
                if (IsLineraServiceRunning)
                {
                    Console.WriteLine($"[QUEUE:{requestId}] Stopping Linera service before open-and-create...");
                    await StopLineraServiceAsync();
                    await Task.Delay(1000);
                }

                var chainId = await OpenMatchChainAsync();
                var appId = await CreateXfighterAppOnChainAsync(moduleId, chainId, leaderboardAppId, maxRetries, retryDelayMs);

                // [NEW] Lưu ánh xạ matchId → (chainId, appId) update 7 Sep 25
                // 1) nếu thiếu thì set, nhưng ta CHỐT rule: matchId == chainId luôn
                if (!string.IsNullOrWhiteSpace(matchId))
                    matchId = chainId;

                // 2) dùng local non-null để index dictionary (hết CS8604)
                var key = matchId ?? chainId;

                // 3) lưu mapping dưới key = chainId (đồng nhất với rule)
                key = chainId;
                _matchMap[key] = new MatchMapping { ChainId = chainId, AppId = appId };
                SaveMatchMapping();
                Console.WriteLine($"[MAP] Added mapping: matchId={matchId} → chain={chainId}, app={appId}");

                Console.WriteLine($"[QUEUE:{requestId}] Restarting Linera service after open-and-create...");
                await StartLineraServiceAsync();
                await Task.Delay(500);

                return (chainId, appId);
            }
            finally
            {
                Console.WriteLine($"[QUEUE:{requestId}] Lock released at {DateTime.UtcNow:O}");
                _openAndCreateLock.Release();
            }
        }

        // [MODIFIED] EnsureServiceRunningAsync – fixed Task return & wait
        // Đảm bảo service bật khi cần
        private async Task EnsureServiceRunningAsync()
        {
            if (!IsLineraServiceRunning)
            {
                Console.WriteLine("[INFO] Linera service not running. Starting...");
                await StartLineraServiceAsync();
                await Task.Delay(1000);
            }
        }

        // Run operation (SubmitMatchResultAsync, GetLeaderboardDataAsync)
        private async Task<T> RunWithLineraServiceAsync<T>(Func<Task<T>> operation)
        {
            await EnsureServiceRunningAsync();
            return await operation();
        }
        // ==================== End Linera Service Automatic ====================

        // ==================== Start SubmitMatchResultAsync, GetLeaderboardDataAsync ====================
        // Step1: Submit match result via GraphQL mutation recordScore
        // Step2:Gửi mutation recordScore with helper postgraphQL
        // Step3:[ NEW PATCH] SubmitMatchResultAsync – lookup mapping nếu thiếu chainId/appId
        // Step4: Enforce single-result per matchId rước khi gửi mutation, kiểm tra trạng thái mapping:
        // nếu _matchMap[matchId].status == "submitted" => trả lỗi 400 (“match already submitted”).
        // Step5:lock(_matchMap) để đảm bảo check-and-set atomic trên dictionary đơn giản, hiệu quả cho quy mô nhỏ ***
        // Trạng thái chu trình: "created" → "submitting" → "submitted"/"failed".
        // SubmittedAt lưu ISO UTC (DateTime.UtcNow.ToString("o")) dễ đọc và parse
        public async Task<string> SubmitMatchResultAsync(string? chainId, string? appId, MatchResult matchResult, int timeoutMs = 10000)
        {
            
            if (matchResult == null)
            {
                throw new ArgumentNullException(nameof(matchResult));
            }

            // START NEW step3 Nếu thiếu chainId hoặc appId, tra từ matchId
            // 1)Resolve chain/app from mapping if missing
            // 2)Atomic check-and-set status
            // 3)after post GraphQL, update Leaderboard -> cập nhật mapping sang submitted / failed
            if (string.IsNullOrWhiteSpace(chainId) || string.IsNullOrWhiteSpace(appId))
            {
                if (!string.IsNullOrWhiteSpace(matchResult.MatchId) &&
                    _matchMap.TryGetValue(matchResult.MatchId, out var pair))
                {
                    chainId = pair.ChainId;
                    appId = pair.AppId;
                    Console.WriteLine($"[MAP] Found mapping for match {matchResult.MatchId}: chain={chainId}, app={appId}");
                }
                else
                {
                    throw new InvalidOperationException
                        ($"Cannot find chain/app mapping for matchId: {matchResult.MatchId}");
                }
            }

            // Ensure we have resolved values now
            if (string.IsNullOrWhiteSpace(chainId) || string.IsNullOrWhiteSpace(appId))
                throw new InvalidOperationException
                    ("chainId and appId are required (either provided or via mapping).");

            // Prevent duplicate submission: atomic check & set using lock on _matchMap
            var matchKey = matchResult.MatchId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(matchKey))
            {
                lock (_matchMap)
                {
                    if (_matchMap.TryGetValue(matchKey, out var existing) && string.Equals(existing.Status, "submitted", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Match {matchKey} has already been submitted (status=submitted).");
                    }

                    // Mark as 'submitting' to prevent concurrent submissions racing
                    if (!_matchMap.ContainsKey(matchKey))
                    {
                        _matchMap[matchKey] = new MatchMapping
                        {
                            ChainId = chainId,
                            AppId = appId,
                            Status = "submitting"
                        };
                    }
                    else
                    {
                        _matchMap[matchKey].Status = "submitting";
                    }
                    SaveMatchMapping();
                }
            }
            // END NEW step3 Nếu thiếu chainId hoặc appId, tra từ matchId
            // MAPPING: MatchID → ChainID → AppID 
            // Execute the GraphQL mutation under service-run wrapper (ensures Linera service is up)
            return await RunWithLineraServiceAsync(async () =>
            {

                var url = $"http://localhost:8080/chains/{chainId}/applications/{appId}";
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

                var graphql = @"
                    mutation recordScore($matchResult: MatchResultInput!) {
                        recordScore(matchResult: $matchResult)
                    }
                ";

                var payload = new
                {
                    query = graphql,
                    variables = new { matchResult }
                };

                //CamelCase để property names match với GraphQL (matchId, player, score)
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                // Log payload để debug
                Console.WriteLine("[DEBUG] GraphQL payload: " + JsonSerializer.Serialize(payload, options));

                var content = new StringContent(JsonSerializer.Serialize(payload, options), Encoding.UTF8, "application/json");
                // ***mở rộng MatchMapping để lưu trạng thái status/ submittedAt / submittedOpId***
                HttpResponseMessage resp;
                string text;
                try
                {
                    resp = await client.PostAsync(url, content);
                    text = await resp.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    // mark mapping as failed if relevant
                    if (!string.IsNullOrWhiteSpace(matchKey))
                    {
                        lock (_matchMap)
                        {
                            if (_matchMap.TryGetValue(matchKey, out var m2))
                            {
                                m2.Status = "failed";
                                m2.SubmittedAt = DateTime.UtcNow.ToString("o");
                                SaveMatchMapping();
                            }
                        }
                    }
                    throw new InvalidOperationException($"HTTP request to linera service failed: {ex.Message}", ex);
                }

                if (!resp.IsSuccessStatusCode)
                {
                    // mark mapping failed
                    if (!string.IsNullOrWhiteSpace(matchKey))
                    {
                        lock (_matchMap)
                        {
                            if (_matchMap.TryGetValue(matchKey, out var m2))
                            {
                                m2.Status = "failed";
                                m2.SubmittedAt = DateTime.UtcNow.ToString("o");
                                SaveMatchMapping();
                            }
                        }
                    }
                    throw new InvalidOperationException($"HTTP {resp.StatusCode} from linera service: {text}");
                }
                // ***NEW MAPPING  need state so orchestrator knows match has been submitted
                // (prevent duplicate) and save opId for debugging/tracing ***

                // ---- parse op hex from response ----
                string? opHex = null;
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var dataEl))
                    {
                        // data might be string hex or an object
                        if (dataEl.ValueKind == JsonValueKind.String)
                        {
                            opHex = dataEl.GetString();
                        }
                        else if (dataEl.ValueKind == JsonValueKind.Object)
                        {
                            // try recordScore property inside data
                            if (dataEl.TryGetProperty("recordScore", out var rs) && rs.ValueKind == JsonValueKind.String)
                                opHex = rs.GetString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: failed to parse op hex from response: {ex.Message}");
                }

                // normalize op id to 64 hex
                var opId = string.Empty;
                if (!string.IsNullOrEmpty(opHex))
                {
                    var found = FirstHex64(opHex);
                    opId = found ?? opHex;
                }
               // --- NEW: Persist mapping BEFORE doing leaderboard mutations ---
                var persistOk = true;
                if (!string.IsNullOrWhiteSpace(matchKey))
                {
                    lock (_matchMap)
                    {
                        if (_matchMap.TryGetValue(matchKey, out var m))
                        {
                            m.Status = "submitted";
                            m.SubmittedOpId = string.IsNullOrWhiteSpace(opId) ? null : opId;
                            m.SubmittedAt = DateTime.UtcNow.ToString("o");
                        }
                        else
                        {
                            _matchMap[matchKey] = new MatchMapping
                            {
                                ChainId = chainId,
                                AppId = appId,
                                Status = "submitted",
                                SubmittedOpId = string.IsNullOrWhiteSpace(opId) ? null : opId,
                                SubmittedAt = DateTime.UtcNow.ToString("o")
                            };
                        }
                        // Save and check success
                        persistOk = SaveMatchMapping();
                    }
                }

                // If we failed to persist mapping, mark as failed and do not post leaderboard
                if (!persistOk)
                {
                    lock (_matchMap)
                    {
                        if (!string.IsNullOrWhiteSpace(matchKey) && _matchMap.TryGetValue(matchKey, out var m2))
                        {
                            m2.Status = "failed";
                            m2.SubmittedAt = DateTime.UtcNow.ToString("o");
                            // best-effort persist
                            SaveMatchMapping();
                        }
                    }
                    throw new InvalidOperationException("Failed to persist match mapping; aborting leaderboard update to avoid duplicates.");
                }

                // ---- Call leaderboard mutation(s) on PublisherChainId if configured ----
                if (!string.IsNullOrEmpty(_config.LeaderboardAppId))
                {
                    try
                    {
                        var winner = !string.IsNullOrWhiteSpace(matchResult.WinnerUsername) ? matchResult.WinnerUsername :
                                     !string.IsNullOrWhiteSpace(matchResult.Player1Username) ? matchResult.Player1Username : string.Empty;
                        var loser = !string.IsNullOrWhiteSpace(matchResult.LoserUsername) ? matchResult.LoserUsername :
                                    !string.IsNullOrWhiteSpace(matchResult.Player2Username) ? matchResult.Player2Username : string.Empty;

                        var tasks = new List<Task<string>>();

                        if (!string.IsNullOrEmpty(winner))
                        {
                            var lbWinnerQuery =
                             $"mutation {{ recordScore(matchId: \"{EscapeGraphqlString(matchResult.MatchId ?? string.Empty)}\", " +
                             $"userId: \"{EscapeGraphqlString(winner ?? string.Empty)}\", isWinner: true) }}";
                            Console.WriteLine("[DEBUG] Posting leaderboard mutation (winner): " + lbWinnerQuery);
                            tasks.Add(PostRawGraphQLAsync(PublisherChainId, _config.LeaderboardAppId, lbWinnerQuery));
                        }

                        if (!string.IsNullOrEmpty(loser))
                        {
                            var lbLoserQuery =
                             $"mutation {{ recordScore(matchId: \"{EscapeGraphqlString(matchResult.MatchId ?? string.Empty)}\", " +
                             $"userId: \"{EscapeGraphqlString(loser ?? string.Empty)}\", isWinner: false) }}";
                            Console.WriteLine("[DEBUG] Posting leaderboard mutation (loser): " + lbLoserQuery);
                            tasks.Add(PostRawGraphQLAsync(PublisherChainId, _config.LeaderboardAppId, lbLoserQuery));
                        }

                        if (tasks.Count > 0)
                        {
                            string[] results = await Task.WhenAll(tasks);
                            for (int i = 0; i < results.Length; i++)
                                Console.WriteLine($"[DEBUG] Leaderboard mutation response #{i + 1}: {results[i]}");

                            Console.WriteLine("[DEBUG] Leaderboard mutations completed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: leaderboard update failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[DEBUG] Skipping leaderboard update because LeaderboardAppId is empty.");
                }
                // return raw service response (text)
                return text;
           });
        }
        
        // Submit match -> RecordMatch -> Mutation Leaderboard -> Getleaderboard
        // Small helper to post a raw GraphQL query (no variables) to a chain/app
        private static async Task<string> PostRawGraphQLAsync(string chainId, string appId, string graphqlQuery, int timeoutMs = 8000)
        {
            var url = $"http://localhost:8080/chains/{chainId}/applications/{appId}";
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            var payload = new { query = graphqlQuery, variables = new { } };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(url, content);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {resp.StatusCode} from linera service: {text}");
            return text;
        }
        // GraphQL string escape helper Utility helpers
        private static string EscapeGraphqlString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        // Get leaderboard via GraphQL query (use publisher chain + leaderboard app)
        public async Task<string> GetLeaderboardDataAsync(string chainId, string appId, int timeoutMs = 8000)
        {
            return await RunWithLineraServiceAsync(async () =>
            {
                var url = $"http://localhost:8080/chains/{chainId}/applications/{appId}";
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

                     var graphql = @"
                        query {
                            leaderboard {
                                userId
                                score
                                totalMatches
                                totalWins
                                totalLosses
                            }
                        }
                    ";

                var payload = new { query = graphql, variables = new { } };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync(url, content);
                var text = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {resp.StatusCode} from linera service: {text}");

                return text;
            });
        }
        // ==================== END SubmitMatchResultAsync, GetLeaderboardDataAsync ====================
        #endregion Linera open chain, submit match data, get leaderboard 

        #region MatchMapping persistence
        
        // ==================== START ánh xạ MAPPING: MatchID → ChainID → AppID ====================
        // Logic Mapping in-memory + persist trong Orchestrator Vì chainID/appID không phải cố định – mỗi trận là một cặp mới.
        // Khi Unity nhấn "Submid Record Match" (button)
        // Nếu Unity gửi đầy đủ chainId và appId, orchestrator dùng luôn(không cần lookup).
        // Nếu Unity chỉ gửi matchId, orchestrator tra từ _matchMap để tìm ra chainId/appId.
        // Nếu không lưu ánh xạ này, khi submit sẽ không biết nên gửi vào chain nào → kết quả có thể bị gửi sai nơi.

        private static readonly Dictionary<string, MatchMapping> _matchMap = new();
        private static readonly string _matchMappingFile =
            Environment.GetEnvironmentVariable("MATCH_MAPPING_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "linera_orchestrator", "match_mapping.json");

        private bool SaveMatchMapping()
        {
            try
            {
                lock (_matchMap)
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    };

                    // ensure directory exists
                    var dir = Path.GetDirectoryName(_matchMappingFile);
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var json = JsonSerializer.Serialize(_matchMap, options);
                    var tmp = _matchMappingFile + ".tmp";

                    File.WriteAllText(tmp, json);
                    // atomic replace
                    if (File.Exists(_matchMappingFile)) File.Delete(_matchMappingFile);
                    File.Move(tmp, _matchMappingFile);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to save match mapping: {ex.Message}");
                return false;
            }
        }
        private static void LoadMatchMapping()
        {
            lock (_matchMap)
            {
                try
                {
                    if (File.Exists(_matchMappingFile))
                    {
                        // Kiểm tra xem file có rỗng không
                        var fileInfo = new FileInfo(_matchMappingFile);
                        if (fileInfo.Length == 0)
                        {
                            Console.WriteLine("[WARN] Match mapping file is empty. Initializing an empty map.");
                            _matchMap.Clear();
                            return;
                        }

                        var json = File.ReadAllText(_matchMappingFile);
                        var data = JsonSerializer.Deserialize<Dictionary<string, MatchMapping>>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (data != null)
                        {
                            _matchMap.Clear();
                            foreach (var kv in data) _matchMap[kv.Key] = kv.Value;
                            Console.WriteLine($"[INFO] Successfully loaded {_matchMap.Count} match mappings.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[INFO] Match mapping file not found. Initializing a new empty map.");
                        _matchMap.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to load match mapping: {ex.Message}");
                    // Trong trường hợp xảy ra lỗi khi đọc/phân tích, ta vẫn khởi tạo một dictionary rỗng
                    // để chương trình có thể tiếp tục hoạt động.
                    _matchMap.Clear();
                }
            }
        }
        public MatchMapping? GetMappingForMatch(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId)) return null;
            return _matchMap.TryGetValue(matchId, out var mapping) ? mapping : null;
        }

        public Dictionary<string, MatchMapping> GetAllMappings()
        {
            // return a copy to avoid external modification
            lock (_matchMap)
            {
                return new Dictionary<string, MatchMapping>(_matchMap);
            }
        }
        // ==================== END ánh xạ MAPPING: MatchID → ChainID → AppID ====================
        #endregion MatchMapping persistence
    }
}