using LongevityWorldCup.Website.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

            // Register the in-memory cached athlete data
            builder.Services.AddSingleton<AthleteDataService>();
            builder.Services.AddDbContext<AgeGuessContext>(o => o.UseSqlite("Data Source=ageguesses.db"));
            builder.Services.AddScoped<AgeGuessService>();

            // Add CORS policy
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder =>
                    {
                        builder.WithOrigins(
                            "https://www.longevityworldcup.com",
                            "http://lwc7tszawiykmkjoq4u2yxramezkwbdys2wxr2fmf6sdr6ug5t36ckqd.onion",
                            "https://lwc7tszawiykmkjoq4u2yxramezkwbdys2wxr2fmf6sdr6ug5t36ckqd.onion")
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    });
            });

           var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AgeGuessContext>();
                db.Database.Migrate();
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // Use CORS
            app.UseCors("AllowSpecificOrigin");

            app.UseStatusCodePagesWithReExecute("/error/{0}");

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
                SmtpUser = "longevityworldcup@gmail.com"
            };

            // Serialize to JSON and save to file
            string json = JsonSerializer.Serialize(defaultConfig, JsonOptions); // Use cached options
            File.WriteAllText(configFilePath, json);
        }
    }
}