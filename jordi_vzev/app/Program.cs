using System.Text.Json;
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
builder.Services.AddSingleton<HomeAssistantEntityService>();

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

app.MapPost("/api/reload", async (HttpContext context, AppStateStore store, CancellationToken cancellationToken) =>
{
    var configuredToken = GetReloadApiToken();
    if (string.IsNullOrWhiteSpace(configuredToken))
    {
        return Results.Problem("Reload API token is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var providedToken = context.Request.Headers["X-Api-Key"].ToString().Trim();
    if (!string.Equals(providedToken, configuredToken, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    await store.InitializeAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(store.Config.DataFolderPath))
    {
        return Results.BadRequest("Data source path is empty.");
    }

    try
    {
        await store.LoadDataAsync(store.Config.DataFolderPath, selectedMonth: null, cancellationToken);
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);

        return Results.Ok(new
        {
            status = "reloaded",
            source = store.Config.DataFolderPath,
            readings = store.MeterReadings.Count,
            message = store.StatusText
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Reload failed: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
    }
}).AllowAnonymous();

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

static string GetReloadApiToken()
{
    var envToken = Environment.GetEnvironmentVariable("JORDI_VZEV_RELOAD_TOKEN")?.Trim();
    if (!string.IsNullOrWhiteSpace(envToken))
    {
        return envToken;
    }

    const string optionsPath = "/data/options.json";
    if (!File.Exists(optionsPath))
    {
        return string.Empty;
    }

    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(optionsPath));
        if (doc.RootElement.TryGetProperty("api_reload_token", out var tokenProp))
        {
            return tokenProp.GetString()?.Trim() ?? string.Empty;
        }
    }
    catch
    {
        return string.Empty;
    }

    return string.Empty;
}
