//LineraConfig.cs
namespace LineraOrchestrator.Models
{
    public class LineraConfig
    {
        public string? LineraWallet { get; set; }
        public string? LineraStorage { get; set; }
        public string? LineraKeystore { get; set; }
        public int? LineraNetPid { get; set; }
        public string LineraCliPath { get; set; } = "/home/roycrypto/.cargo/bin/linera";
        // Thêm các thuộc tính đường dẫn đến thư mục xfighter và leaderboard
        public string XFighterPath { get; set; } = "/mnt/d/workspace/linera-protocol/examples/target/wasm32-unknown-unknown/release";
        public string LeaderboardPath { get; set; } = "/mnt/d/workspace/linera-protocol/examples/target/wasm32-unknown-unknown/release";
        // Stored IDs (in-memory). These are set by orchestrator when publish/create succeed.
        public string? XFighterModuleId { get; set; }
        public string? XFighterAppId { get; set; }
        public string? LeaderboardAppId { get; set; }
        public bool IsReady => !string.IsNullOrEmpty(LineraWallet)
                       && !string.IsNullOrEmpty(LineraStorage)
                       && !string.IsNullOrEmpty(XFighterModuleId)
                       && !string.IsNullOrEmpty(LeaderboardAppId);
        // Liên quan đến bật tắt service
        public int? LineraServicePid { get; set; }
    }
}
