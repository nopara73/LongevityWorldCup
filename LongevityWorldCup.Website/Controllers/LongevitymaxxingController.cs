using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/longevitymaxxing")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
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
            var result = await _challenge.SignupAsync(request, context: HttpContext, ct: ct).ConfigureAwait(false);
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

    [HttpPost("commitment-payment")]
    public async Task<IActionResult> CreateCommitmentPayment(
        [FromBody] LongevitymaxxingCommitmentPaymentRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _challenge.CreateCommitmentPaymentInvoiceAsync(request.AccessToken, context: HttpContext, ct: ct).ConfigureAwait(false));
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

    [HttpPost("commitment-payment/status")]
    public async Task<IActionResult> RefreshCommitmentPayment(
        [FromBody] LongevitymaxxingCommitmentPaymentRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _challenge.RefreshCommitmentPaymentStatusAsync(request.AccessToken, context: HttpContext, ct: ct).ConfigureAwait(false));
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
    [Consumes("application/json")]
    public IActionResult CheckIn([FromBody] LongevitymaxxingCheckInRequest request)
    {
        try
        {
            return Ok(_challenge.SubmitCheckIn(request, context: HttpContext));
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
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(LongevitymaxxingChallengeService.MaxCheckInPhotoRequestBytes)]
    public async Task<IActionResult> CheckInWithPhotos(
        [FromForm] LongevitymaxxingCheckInFormRequest request,
        [FromForm(Name = "notePhotos")] List<IFormFile>? notePhotos,
        CancellationToken ct)
    {
        try
        {
            var checkIn = new LongevitymaxxingCheckInRequest(
                request.AccessToken,
                request.ChallengeDay,
                request.Sleep,
                request.Exercise,
                request.Nutrition,
                request.Vices,
                request.Note);
            return Ok(await _challenge.SubmitCheckInAsync(checkIn, notePhotos, context: HttpContext, ct: ct).ConfigureAwait(false));
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

    [HttpPost("stop-community-call-emails")]
    public IActionResult StopCommunityCallEmails([FromBody] LongevitymaxxingTokenRequest request)
    {
        try
        {
            _challenge.StopCommunityCallEmails(request.Token);
            return Ok(new { message = "Community call emails stopped." });
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

public sealed class LongevitymaxxingCheckInFormRequest
{
    public string AccessToken { get; set; } = "";
    public int ChallengeDay { get; set; }
    public int Sleep { get; set; }
    public int Exercise { get; set; }
    public int Nutrition { get; set; }
    public int Vices { get; set; }
    public string? Note { get; set; }
}
