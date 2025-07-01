using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GuessController(AthleteDataService svc) : Controller
    {
        private readonly AthleteDataService _svc = svc;

        [HttpPost("athlete-age")]
        public IActionResult PostAthleteAgeGuess(string athleteName, int ageGuess)
        {
            return Ok();
        }
    }
}