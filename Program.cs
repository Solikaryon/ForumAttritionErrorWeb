using System.Security.Claims;
using ForoAttritionErrorWeb.Data;
using ForoAttritionErrorWeb.Infrastructure;
using ForoAttritionErrorWeb.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
/* Created by: Luis Fernando Monjaraz Briseño */
// When running a published .exe, the working directory can differ from the executable folder.
// Anchor the ContentRoot so appsettings*.json are resolved next to the executable.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// NOTE: launchSettings.json only affects `dotnet run`. For published executables we default to the
// requested LAN URL unless the user/admin explicitly configured URLs via env var or Kestrel config.
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var configuredKestrelHttpUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
if (string.IsNullOrWhiteSpace(configuredUrls) && string.IsNullOrWhiteSpace(configuredKestrelHttpUrl))
{
    builder.WebHost.UseUrls("http://172.22.127.129:5055");
}

builder.Services.AddRazorPages();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.SlidingExpiration = true;
        options.Cookie.Name = "AttritionForo.Auth";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Authenticated", policy => policy.RequireClaim(ClaimTypes.NameIdentifier));
});

builder.Services.AddScoped<SqlUserRepository>();
builder.Services.AddScoped<SqlForumPostRepository>();
builder.Services.AddScoped<SqlForumPostAnswerRepository>();
builder.Services.AddSingleton<IPasswordHasher<ForumUser>, PasswordHasher<ForumUser>>();

var app = builder.Build();

await DatabaseBootstrapper.EnsureCreatedAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();

    // Only redirect to HTTPS when an HTTPS endpoint is actually configured.
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? string.Empty;
    if (urls.Contains("https://", StringComparison.OrdinalIgnoreCase))
    {
        app.UseHttpsRedirection();
    }
}

app.UseRouting();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
