using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/longevitymaxxing")]
public sealed class LongevitymaxxingController(LongevitymaxxingChallengeService challenge) : ControllerBase
{
    private readonly LongevitymaxxingChallengeService _challenge = challenge;

    [HttpGet("state")]
    public IActionResult GetPublicState()
        => Ok(_challenge.GetPublicState());

    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] LongevitymaxxingSignupRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _challenge.SignupAsync(request, ct: ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] LongevitymaxxingTokenRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _challenge.ConfirmAsync(request.Token, ct: ct).ConfigureAwait(false));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("participant")]
    public IActionResult GetParticipantState([FromBody] LongevitymaxxingTokenRequest request)
    {
        try
        {
            return Ok(_challenge.GetParticipantState(request.Token));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("resend")]
    public async Task<IActionResult> Resend([FromBody] LongevitymaxxingEmailRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _challenge.ResendAccessLinkAsync(request.Email, ct: ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("edit")]
    public IActionResult Edit([FromBody] LongevitymaxxingParticipantEditRequest request)
    {
        try
        {
            return Ok(_challenge.EditParticipant(request));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("profile-picture")]
    [RequestSizeLimit(LongevitymaxxingChallengeService.MaxProfilePictureUploadBytes + 64 * 1024)]
    public async Task<IActionResult> UploadProfilePicture(
        [FromForm] string accessToken,
        [FromForm(Name = "profilePicture")] IFormFile? profilePicture,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _challenge.UploadParticipantProfilePictureAsync(accessToken, profilePicture, ct).ConfigureAwait(false));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("check-in")]
    public IActionResult CheckIn([FromBody] LongevitymaxxingCheckInRequest request)
    {
        try
        {
            return Ok(_challenge.SubmitCheckIn(request));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("stop-emails")]
    public IActionResult StopEmails([FromBody] LongevitymaxxingTokenRequest request)
    {
        try
        {
            _challenge.StopChallengeEmails(request.Token);
            return Ok(new { message = "Challenge emails stopped." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    private static bool IsClientError(Exception ex)
        => ex is InvalidOperationException or ArgumentException;
}

public sealed record LongevitymaxxingTokenRequest(string Token);

public sealed record LongevitymaxxingEmailRequest(string Email);
