//LineraOrchestratorService.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using LineraOrchestrator.Services;
using LineraOrchestrator.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace LineraOrchestrator.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LineraController : ControllerBase
    {
        private readonly LineraOrchestratorService _svc;

        // Khởi tạo Controller với service Linera
        public LineraController(LineraOrchestratorService svc)
        {
            _svc = svc;
        }

        // API để khởi động Linera Node
        [HttpPost("start-linera-node")]
        public async Task<IActionResult> StartLineraNet()
        {
            try
            {
                var config = await _svc.StartLineraNetAsync();

                return Ok(new
                {
                    success = true,
                    message = "Linera Node đã thành công khởi động và các biến môi trường đã được trích xuất.",
                    linera_wallet = config.LineraWallet,
                    linera_storage = config.LineraStorage,
                    linera_keystore = config.LineraKeystore,
                     // thêm các ID hữu ích cho client
                    xfighter_module_id = config.XFighterModuleId,
                    xfighter_app_id = config.XFighterAppId,
                    leaderboard_app_id = config.LeaderboardAppId,
                    isReady = config.IsReady

                });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== New endpoints moduleId and leaderboardAppId =====================
        /// <summary>
        /// Single endpoint that opens a new match chain and then creates an xfighter app on it.
        /// Body must include.
        /// </summary>
        [HttpPost("open-and-create")]
        public async Task<IActionResult> OpenAndCreate([FromBody] OpenAndCreateRequest req)
        {
            if (req == null) return BadRequest(new { success = false, error = "invalid_request", message = "Request body required." });

            if (string.IsNullOrWhiteSpace(req.ModuleId))
                return BadRequest(new { success = false, error = "missing_moduleId", message = "moduleId is required in the request body." });

            if (string.IsNullOrWhiteSpace(req.LeaderboardAppId))
                return BadRequest(new { success = false, error = "missing_leaderboardAppId", message = "leaderboardAppId is required in the request body." });

            try
            {
                var (chainId, appId) = await _svc.OpenAndCreateWithServiceControlAsync(
                    req.ModuleId,
                    req.LeaderboardAppId,
                    req.MatchId, // NEW: truyền matchId vào, truyền thẳng vào chainID Update 7 sep 25
                    req.MaxRetries ?? 5,
                    req.RetryDelayMs ?? 2000);

                // matchId = chainId theo rule mới
                var matchId = chainId;
                return Ok(new { success = true, chainId, appId, matchId = req.MatchId ?? chainId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    details = ex.InnerException?.Message
                });
            }
        }
        //Open a new match chain only (for debugging).
        [HttpPost("open-match-chain")]
        public async Task<IActionResult> OpenMatchChain()
        {
            try
            {
                var chainId = await _svc.OpenMatchChainAsync();
                return Ok(new { success = true, newChainId = chainId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        /// <summary>
        /// Create XFighter app on an existing chain (resolve chainId if partial supplied).
        /// Body must include moduleId, chainId (or partial) and leaderboardAppId.
        /// </summary>
        [HttpPost("create-xfighter-app")] //(for debugging)
        public async Task<IActionResult> CreateXfighterApp([FromBody] CreateXfighterRequest req)
        {
            if (req == null) return BadRequest(new { success = false, error = "invalid_request", message = "Request body required." });

            if (string.IsNullOrWhiteSpace(req.ModuleId))
                return BadRequest(new { success = false, error = "missing_moduleId", message = "moduleId is required in the request body." });

            if (string.IsNullOrWhiteSpace(req.ChainId))
                return BadRequest(new { success = false, error = "missing_chainId", message = "chainId is required in the request body." });

            if (string.IsNullOrWhiteSpace(req.LeaderboardAppId))
                return BadRequest(new { success = false, error = "missing_leaderboardAppId", message = "leaderboardAppId is required in the request body." });

            try
            {
                int maxRetries = req.MaxRetries ?? 3;
                int retryDelayMs = req.RetryDelayMs ?? 2000;

                var appId = await _svc.CreateXfighterAppOnChainAsync(req.ModuleId, req.ChainId, req.LeaderboardAppId, maxRetries, retryDelayMs);
                return Ok(new { success = true, appId, chainId = req.ChainId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, details = ex.InnerException?.Message });
            }
        }
        //Automatic Linera Services
        [HttpPost("start-linera-service")]
        public async Task<IActionResult> StartLineraService([FromQuery] int port = 8080)
        {
            try
            {
                var pid = await _svc.StartLineraServiceAsync(port);
                return Ok(new { success = true, pid });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("stop-linera-service")]
        public async Task<IActionResult> StopLineraService()
        {
            try
            {
                await _svc.StopLineraServiceAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        //submit-match-result & get-leaderboard-data" endpoint
        [HttpPost("submit-match-result")]
        public async Task<IActionResult> SubmitMatchResult([FromBody] JsonElement payload)
        {
            try
            {
                if (payload.ValueKind != JsonValueKind.Object)
                    return BadRequest(new { success = false, message = "Empty or invalid body" });

                // helpers
                string GetFirstString(JsonElement obj, params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        if (obj.ValueKind != JsonValueKind.Object) continue;
                        if (obj.TryGetProperty(k, out var p) && p.ValueKind != JsonValueKind.Null)
                        {
                            try { var s = p.GetString(); if (!string.IsNullOrWhiteSpace(s)) return s; } catch { }
                        }
                    }
                    return string.Empty;
                }

                int GetFirstInt(JsonElement obj, params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        if (obj.ValueKind != JsonValueKind.Object) continue;
                        if (obj.TryGetProperty(k, out var p) && p.ValueKind != JsonValueKind.Null)
                        {
                            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var iv)) return iv;
                            if (p.ValueKind == JsonValueKind.String)
                            {
                                var s = p.GetString();
                                if (int.TryParse(s, out var iv2)) return iv2;
                                if (long.TryParse(s, out var lv)) return Convert.ToInt32(lv);
                            }
                        }
                    }
                    return 0;
                }

                // read top-level chain/app ids (case-insensitive attempts)
                var chainId = GetFirstString(payload, "chainId", "ChainId");
                var appId = GetFirstString(payload, "appId", "AppId");

                // Try nested matchResult first
                MatchResult? match = null;
                if (payload.TryGetProperty("matchResult", out var mrElem) && mrElem.ValueKind == JsonValueKind.Object)
                {
                    var matchId = GetFirstString(mrElem, "MatchId", "matchId");
                    if (string.IsNullOrWhiteSpace(matchId)) matchId = Guid.NewGuid().ToString();

                    match = new MatchResult
                    {
                        MatchId = matchId,
                        Player1Username = GetFirstString(mrElem, "Player1Username", "player1Username", "player", "Player"),
                        Player2Username = GetFirstString(mrElem, "Player2Username", "player2Username"),
                        Player1Score = GetFirstInt(mrElem, "Player1Score", "player1Score", "score"),
                        Player2Score = GetFirstInt(mrElem, "Player2Score", "player2Score"),
                        WinnerUsername = GetFirstString(mrElem, "WinnerUsername", "winnerUsername"),
                        LoserUsername = GetFirstString(mrElem, "LoserUsername", "loserUsername"),
                        DurationSeconds = GetFirstInt(mrElem, "DurationSeconds", "durationSeconds"),
                        Timestamp = GetFirstInt(mrElem, "Timestamp", "timestamp"),
                        MapName = GetFirstString(mrElem, "MapName", "mapName"),
                        MatchType = GetFirstString(mrElem, "MatchType", "matchType")
                    };

                    if (match.Timestamp == 0) match.Timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                else
                {
                    // fallback flattened legacy shape
                    var player = GetFirstString(payload, "player", "Player");
                    var score = GetFirstInt(payload, "score", "Score");
                    var matchId = GetFirstString(payload, "matchId", "MatchId");
                    if (string.IsNullOrWhiteSpace(matchId)) matchId = Guid.NewGuid().ToString();

                    match = new MatchResult
                    {
                        MatchId = matchId,
                        Player1Username = player,
                        Player2Username = string.Empty,
                        Player1Score = score,
                        Player2Score = 0,
                        WinnerUsername = player,
                        LoserUsername = string.Empty,
                        DurationSeconds = 0,
                        Timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        MapName = string.Empty,
                        MatchType = string.Empty
                    };
                }

                var json = await _svc.SubmitMatchResultAsync(chainId, appId, match);

                // parse to JsonDocument for consistent response shape
                var parsed = JsonDocument.Parse(json);
                return Ok(new { success = true, body = parsed });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, details = ex.InnerException?.Message });
            }
        }

        [HttpPost("get-leaderboard-data")]
        public async Task<IActionResult> GetLeaderboardData([FromBody] LeaderboardRequest req)
        {
            try
            {
                var json = await _svc.GetLeaderboardDataAsync(req.ChainId, req.AppId);
                return Ok(new { success = true, body = JsonDocument.Parse(json) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        // ===================== DEBUG: Match Mapping =====================
        [HttpGet("match-mapping/{matchId}")]
        public IActionResult GetMatchMapping(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return BadRequest(new { success = false, error = "matchId is required" });

            var mapping = _svc.GetMappingForMatch(matchId);
            if (mapping == null)
                return NotFound(new { success = false, message = $"No mapping found for matchId={matchId}" });

            return Ok(new
            {
                success = true,
                matchId,
                chainId = mapping.ChainId,
                appId = mapping.AppId
            });
        }

        [HttpGet("match-mapping/all")]
        public IActionResult GetAllMatchMappings()
        {
            var allMappings = _svc.GetAllMappings();
            return Ok(new
            {
                success = true,
                count = allMappings.Count,
                mappings = allMappings.Select(kv => new
                {
                    matchId = kv.Key,
                    chainId = kv.Value.ChainId,
                    appId = kv.Value.AppId
                }).ToList()
            });
        }
        // ===================== DEBUG: Match Mapping =====================
        // ===================== Config Status Linera Node & Service on Unity =====================

        [HttpGet("linera-service-status")]
        [HttpPost("linera-service-status")]
        public IActionResult GetLineraServiceStatus()
        {
            try
            {
                var running = _svc.IsServiceRunning();   // check service có chạy không
                var pid = _svc.GetServicePid();          // lấy PID (null nếu chưa có)

                Console.WriteLine($"[Linera] Status check via API => Running={running}, PID={pid}");

                return Ok(new
                {
                    success = true,
                    isRunning = running,
                    pid
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Linera] Error in service-status endpoint: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("linera-config")]
        public IActionResult GetLineraConfig()
        {
            try
            {
                var cfg = _svc.GetCurrentConfig();
                return Ok(new
                {
                    success = true,
                    linera_wallet = cfg.LineraWallet,
                    linera_storage = cfg.LineraStorage,
                    linera_keystore = cfg.LineraKeystore,
                    xfighter_module_id = cfg.XFighterModuleId,
                    leaderboard_app_id = cfg.LeaderboardAppId,
                    xfighter_app_id = cfg.XFighterAppId,
                    isReady = cfg.IsReady
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
