using System.Text;
using System.Text.Json;
using ZevEbixWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Public", "/strom");
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BillingEngine>();
builder.Services.AddSingleton<AppStateStore>();

var publicAccess = LoadPublicAccessSettings();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRouting();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/strom", StringComparison.OrdinalIgnoreCase) || !publicAccess.Enabled)
    {
        await next();
        return;
    }

    if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), publicAccess))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"ZEV Strom\"";
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var state = scope.ServiceProvider.GetRequiredService<AppStateStore>();
    await state.InitializeAsync(CancellationToken.None);
}

app.Run();

static PublicAccessSettings LoadPublicAccessSettings()
{
    var username = Environment.GetEnvironmentVariable("ZEV_PUBLIC_USERNAME")?.Trim() ?? string.Empty;
    var password = Environment.GetEnvironmentVariable("ZEV_PUBLIC_PASSWORD")?.Trim() ?? string.Empty;

    const string optionsPath = "/data/options.json";
    if (!File.Exists(optionsPath))
    {
        return new PublicAccessSettings(username, password);
    }

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(optionsPath));
        if (document.RootElement.TryGetProperty("public_username", out var userProp))
        {
            username = userProp.GetString()?.Trim() ?? username;
        }

        if (document.RootElement.TryGetProperty("public_password", out var pwdProp))
        {
            password = pwdProp.GetString()?.Trim() ?? password;
        }
    }
    catch
    {
        // ignore invalid options file and keep env-based defaults
    }

    return new PublicAccessSettings(username, password);
}

static bool IsAuthorized(string authorizationHeader, PublicAccessSettings settings)
{
    if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    try
    {
        var encoded = authorizationHeader["Basic ".Length..].Trim();
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var username = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        return string.Equals(username, settings.Username, StringComparison.Ordinal)
               && string.Equals(password, settings.Password, StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

sealed record PublicAccessSettings(string Username, string Password)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
