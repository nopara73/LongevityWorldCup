using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController(AthleteDataService svc, AgeGuessService ageSvc) : Controller
    {
        private readonly AthleteDataService _svc = svc;
        private readonly AgeGuessService _ageSvc = ageSvc;

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
        public async Task<IActionResult> GetAthletes()
        {
            var array = new JsonArray();
            foreach (var a in _svc.Athletes)
            {
                var obj = a!.AsObject();
                int id = obj["Id"]!.GetValue<int>();
                var clone = JsonNode.Parse(obj.ToJsonString())!.AsObject();
                double crowd = await _ageSvc.GetCrowdAgeAsync(id);
                clone["CrowdAge"] = Math.Round(crowd, 1);
                array.Add(clone);
            }
            return Ok(array);
        }
    }
}