using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController : Controller
    {
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
    }
}