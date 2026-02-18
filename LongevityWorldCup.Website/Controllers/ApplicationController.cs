using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApplicationController : ControllerBase
    {
        private readonly ApplicationService _appService;

        public ApplicationController(ApplicationService appService)
        {
            _appService = appService;
        }

        [HttpPost("application")]
        public async Task<IActionResult> Application([FromBody] ApplicantData applicantData)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var (success, error) = await _appService.ProcessApplicationAsync(applicantData);

            if (success)
            {
                return Ok("Email sent successfully.");
            }

            if (error?.StartsWith("Internal server error") == true)
            {
                return StatusCode(500, error);
            }

            return BadRequest(error);
        }
    }
}
