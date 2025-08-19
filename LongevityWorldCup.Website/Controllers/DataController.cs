using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController(AthleteDataService svc) : Controller
    {
        private readonly AthleteDataService _svc = svc;

        [HttpGet("flags")]
        public IActionResult GetFlags()
        {
            return Ok(Flags.Flag);
        }

        [HttpGet("divisions")]
        public IActionResult GetDivisions()
        {
            return Ok(Divisions.Division);
        }

        [HttpGet("athletes")]
        public IActionResult GetAthletes() => Ok(_svc.Athletes);
        
        [HttpGet("events")]
        public IActionResult GetEvents() => Ok(_svc.Events);
    }
}