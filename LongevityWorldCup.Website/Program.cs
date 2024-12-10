using LongevityWorldCup.Website.Middleware;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Text.Json;

namespace LongevityWorldCup.Website
{
    public class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true }; // Cached options

        public static void Main(string[] args)
        {
            InitializeDefaultConfig(); // Ensure default config file is created

            var builder = WebApplication.CreateBuilder(args);

            // Configure Kestrel to use settings from appsettings.json
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                var kestrelConfig = context.Configuration.GetSection("Kestrel");
                options.Configure(kestrelConfig);
            });

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddHttpClient();
            builder.Services.AddMemoryCache();

            // Add CORS policy
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder =>
                    {
                        builder.WithOrigins("https://www.longevityworldcup.com")
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // Use CORS
            app.UseCors("AllowSpecificOrigin");

            app.UseRouting();

            // Register the custom HTML injection middleware
            app.UseMiddleware<HtmlInjectionMiddleware>();

            // Enable default file serving (index.html) and static file serving
            app.UseDefaultFiles();  // Serve default files like index.html
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000";
                    ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddYears(1).ToString("R");
                }
            });

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }

        private static void InitializeDefaultConfig()
        {
            string configFilePath = "config.json";

            // Check if the config file already exists
            if (File.Exists(configFilePath))
                return;

            // Create default configuration
            var defaultConfig = new
            {
                EmailFrom = "longevityworldcup@gmail.com",
                EmailTo = "longevityworldcup@gmail.com",
                SmtpServer = "smtp.gmail.com",
                SmtpPort = 587,
                SmtpUser = "longevityworldcup@gmail.com",
                SmtpPassword = "your_password"
            };

            // Serialize to JSON and save to file
            string json = JsonSerializer.Serialize(defaultConfig, JsonOptions); // Use cached options
            File.WriteAllText(configFilePath, json);
        }
    }
}