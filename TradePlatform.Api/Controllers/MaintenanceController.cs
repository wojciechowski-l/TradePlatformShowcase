using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Api.Controllers
{
    [ApiController]
    [Route("api/maintenance/projections")]
    [Authorize]
    public class MaintenanceController(
        IAccountActivityProjectionRebuilder rebuilder,
        IWebHostEnvironment environment) : ControllerBase
    {
        [HttpPost("account-activity/rebuild")]
        public async Task<IActionResult> RebuildAccountActivityProjection(CancellationToken cancellationToken)
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Test"))
            {
                return NotFound();
            }

            var projectionCount = await rebuilder.RebuildAsync(cancellationToken);

            return Ok(new
            {
                message = "Account activity projection rebuilt from transaction records.",
                projectionCount
            });
        }
    }
}
