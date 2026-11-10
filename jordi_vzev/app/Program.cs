using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ZevEbixWeb.Services;

var builder = WebApplication.CreateBuilder(args);

var appPort = ReadPort("JORDI_VZEV_PORT", 8082);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(appPort);
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BillingEngine>();
builder.Services.AddSingleton<AppStateStore>();
builder.Services.AddSingleton<UserAccessService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" && context.User.Identity?.IsAuthenticated == true)
    {
        var isAdmin = context.User.IsInRole(UserAccessService.AdminRole);
        context.Response.Redirect(isAdmin ? "/Admin" : "/Dashboard");
        return;
    }

    await next();
});

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var state = scope.ServiceProvider.GetRequiredService<AppStateStore>();
    await state.InitializeAsync(CancellationToken.None);
}

app.Run();

static int ReadPort(string envVar, int fallback)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
