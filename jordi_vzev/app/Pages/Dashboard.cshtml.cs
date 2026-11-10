using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Models;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

[Authorize]
public class DashboardModel(AppStateStore store, BillingEngine engine) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "range")]
    public string Range { get; set; } = "7days";

    [BindProperty(SupportsGet = true, Name = "from")]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public string? To { get; set; }

    public string PropertyName { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public List<SummaryRow> MonthRows { get; private set; } = [];
    public List<PropertyDailyConsumptionPoint> DailyPoints { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.IsInRole(UserAccessService.AdminRole))
        {
            return RedirectToPage("/Admin");
        }

        PropertyName = User.FindFirstValue("property") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(PropertyName))
        {
            ErrorMessage = "Für diesen Benutzer ist keine Liegenschaft hinterlegt.";
            return Page();
        }

        await store.InitializeAsync(cancellationToken);
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);

        MonthRows = engine.BuildSummary(store.Config, store.MeterReadings, selectedMonth: null)
            .Where(x => string.Equals(x.PropertyName, PropertyName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.MonthLabel)
            .ToList();

        var (fromDate, toDate) = ResolveRange();
        var daily = engine.BuildDailyConsumption(store.Config, store.MeterReadings, PropertyName, fromDate, toDate);
        if (daily is not null)
        {
            DailyPoints = daily.Points;
        }

        return Page();
    }

    private (DateOnly from, DateOnly to) ResolveRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (string.Equals(Range, "today", StringComparison.OrdinalIgnoreCase))
        {
            return (today, today);
        }

        if (string.Equals(Range, "custom", StringComparison.OrdinalIgnoreCase)
            && DateOnly.TryParseExact(From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            && DateOnly.TryParseExact(To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            return from <= to ? (from, to) : (to, from);
        }

        return (today.AddDays(-6), today);
    }
}
