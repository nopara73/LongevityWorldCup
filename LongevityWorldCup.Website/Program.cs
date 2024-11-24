using LongevityWorldCup.Website.Middleware;

namespace LongevityWorldCup.Website
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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
            app.UseStaticFiles();   // Serve static files

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}