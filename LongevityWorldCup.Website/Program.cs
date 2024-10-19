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

            var app = builder.Build();

            // Register the custom HTML injection middleware BEFORE static files
            app.UseMiddleware<HtmlInjectionMiddleware>();

            // Enable default file serving (index.html) and static file serving
            app.UseDefaultFiles();  // <-- Add this line to serve default files like index.html
            app.UseStaticFiles();   // <-- Make sure this comes after UseDefaultFiles()

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();

            app.MapControllerRoute(
             name: "default",
             pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}