using Microsoft.AspNetCore.Mvc;
using MailKit.Net.Smtp;
using MimeKit;
using System.IO;
using System.Threading.Tasks;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApplicationController : ControllerBase
    {
        [HttpPost("apply")]
        public async Task<IActionResult> Apply()
        {
            using var reader = new StreamReader(Request.Body);
            string content = await reader.ReadToEndAsync();

            // Load SMTP configuration
            Config config;
            try
            {
                config = await Config.LoadAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to load configuration: {ex.Message}");
            }

            // Create the email message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Application Bot", config.EmailFrom));
            message.To.Add(new MailboxAddress("", config.EmailTo));
            message.Subject = "New Application Submitted";
            message.Body = new TextPart("plain")
            {
                Text = content
            };

            // Send the email
            try
            {
                using var client = new SmtpClient();

                await client.ConnectAsync(config.SmtpServer, config.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);

                // If the SMTP server requires authentication
                await client.AuthenticateAsync(config.SmtpUser, config.SmtpPassword);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                return Ok("Email sent successfully.");
            }
            catch (Exception ex)
            {
                // Handle exception
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}