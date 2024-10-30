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

        [HttpGet("athletes")]
        public IActionResult GetAthletes()
        {
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Athletes.json");
            var jsonData = System.IO.File.ReadAllText(jsonFilePath);
            return Ok(jsonData);
        }
    }
}