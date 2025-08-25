using Microsoft.AspNetCore.Mvc;
using LineraOrchestrator.Services;

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
                    linera_keystore = config.LineraKeystore
                });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // API để dừng Linera Node
        [HttpPost("stop-linera-node")]
        public IActionResult StopLineraNet()
        {
            try
            {
                _svc.StopLineraNode();
                return Ok(new { success = true, message = "Linera Node đã được dừng." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
