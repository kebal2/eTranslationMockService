using eTranslationMockService.Services;

using Microsoft.AspNetCore.Mvc;

namespace eTranslationMockService.Controllers;

/// <summary>
/// The system status endpoint of eTranslation: no authentication, GET or POST, answers
/// <c>{ "level": 0, "message": "Normal service" }</c>.
/// </summary>
[ApiController]
[Route("/api/status")]
public class StatusController : ControllerBase
{
    private readonly IServiceStatus status;

    public StatusController(IServiceStatus status)
    {
        this.status = status;
    }

    [HttpGet]
    [HttpPost]
    public IActionResult Get() => Ok(Project(this.status.Level));

    /// <summary>
    /// Sets the level the mock reports from now on. Not part of the eTranslation API.
    /// </summary>
    [HttpPut]
    public IActionResult Set(StatusUpdate update)
    {
        if (!Enum.IsDefined(typeof(StatusLevel), update.level))
        {
            return BadRequest(new { error = "level must be 0, 1, 2 or 3" });
        }

        this.status.Level = (StatusLevel)update.level;

        return Ok(Project(this.status.Level));
    }

    private static object Project(StatusLevel level) => new { level = (int)level, message = ServiceStatus.Message(level) };

    public record StatusUpdate(int level);
}
