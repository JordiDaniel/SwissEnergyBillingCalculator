using ZevEbixWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BillingEngine>();
builder.Services.AddSingleton<AppStateStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var state = scope.ServiceProvider.GetRequiredService<AppStateStore>();
    await state.InitializeAsync(CancellationToken.None);
}

app.Run();
