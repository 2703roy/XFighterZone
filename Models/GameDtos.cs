//DTO = Data Transfer Object (đối tượng truyền dữ liệu)
//Cclass đơn giản chỉ chứa dữ liệu (properties), không có hoặc rất ít logic.
// Dùng để định nghĩa rõ ràng schema dữ liệu khi truyền qua lại giữa:
//Client ↔ API
//API ↔ Service
//Service ↔ GraphQL

using System.Text.Json.Serialization;

namespace LineraOrchestrator.Models
{
    //Cấu hình global JsonSerializerOptions (camelCase) và dùng lại options đó
    //khi serialize payload GraphQL. Ít phải annotate, tiện khi có nhiều DTO
    //[JsonPropertyName("moduleId")] -> ý nghĩa
    // ===================== Models for new endpoints =====================
    public class OpenAndCreateRequest
    {
        [JsonPropertyName("moduleId")]
        public string? ModuleId { get; set; }

        [JsonPropertyName("leaderboardAppId")]
        public string? LeaderboardAppId { get; set; }
        [JsonPropertyName("matchId")]
        public string? MatchId { get; set; } // NEW MAPPING

        [JsonPropertyName("maxRetries")]
        public int? MaxRetries { get; set; }

        [JsonPropertyName("retryDelayMs")]
        public int? RetryDelayMs { get; set; }
    }

    public class CreateXfighterRequest
    {
        [JsonPropertyName("moduleId")]
        public string? ModuleId { get; set; }

        [JsonPropertyName("chainId")]
        public string? ChainId { get; set; }

        [JsonPropertyName("leaderboardAppId")]
        public string? LeaderboardAppId { get; set; }

        [JsonPropertyName("maxRetries")]
        public int? MaxRetries { get; set; }

        [JsonPropertyName("retryDelayMs")]
        public int? RetryDelayMs { get; set; }
    }
    // DTO “ngoại giao”/đặc biệt (những DTO bạn giao tiếp trực tiếp với service bên ngoài
    // và muốn đảm bảo không phụ thuộc cấu hình global)
    // Payload gửi xuống GraphQL recordScore(matchResult: MatchResultInput!)
    public class MatchResult
    {
        [JsonPropertyName("matchId")]
        public string MatchId { get; set; } = string.Empty;

        [JsonPropertyName("player1Username")]
        public string Player1Username { get; set; } = string.Empty;

        [JsonPropertyName("player2Username")]
        public string Player2Username { get; set; } = string.Empty;

        [JsonPropertyName("winnerUsername")]
        public string WinnerUsername { get; set; } = string.Empty;

        [JsonPropertyName("loserUsername")]
        public string LoserUsername { get; set; } = string.Empty;

        [JsonPropertyName("durationSeconds")]
        public int DurationSeconds { get; set; } = 0;

        [JsonPropertyName("timestamp")] // GraphQL expects Int! — dùng int (non-nullable)
        public int Timestamp { get; set; } = 0;

        [JsonPropertyName("player1Score")]
        public int Player1Score { get; set; } = 0;

        [JsonPropertyName("player2Score")]
        public int Player2Score { get; set; } = 0;

        [JsonPropertyName("mapName")]
        public string MapName { get; set; } = string.Empty;

        [JsonPropertyName("matchType")]
        public string MatchType { get; set; } = string.Empty;

    }
    // Request từ client tới controller để submit điểm
    public class MatchRequest
    {
        public string ChainId { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string Player { get; set; } = string.Empty;
        public int Score { get; set; }

    }
    // Request lấy leaderboard
    public class LeaderboardRequest
    {
        public string ChainId { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
    }
    public class MatchMapping
    {
        [JsonPropertyName("chainId")]
        public string ChainId { get; set; } = string.Empty;

        [JsonPropertyName("appId")]
        public string AppId { get; set; } = string.Empty;

        // "created" | "submitting" | "submitted" | "failed"
        [JsonPropertyName("status")]
        public string Status { get; set; } = "created";

        // opId returned from linera service / GraphQL (hex)
        [JsonPropertyName("submittedOpId")]
        public string? SubmittedOpId { get; set; }

        // ISO 8601 UTC timestamp of submission
        [JsonPropertyName("submittedAt")]
        public string? SubmittedAt { get; set; }
    }
    public class ServiceStatus
    {
        public bool IsRunning { get; set; }
        public string? GraphQLUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }


}
