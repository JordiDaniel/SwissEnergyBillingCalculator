using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Models;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

[Authorize]
public class DashboardModel(AppStateStore store, BillingEngine engine, HomeAssistantEntityService haEntityService) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "property")]
    public string? SelectedProperty { get; set; }

    [BindProperty(SupportsGet = true, Name = "range")]
    public string Range { get; set; } = "7days";

    [BindProperty(SupportsGet = true, Name = "from")]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public string? To { get; set; }

    public string PropertyName { get; private set; } = string.Empty;
    public List<string> AvailableProperties { get; private set; } = [];
    public string CurrentProductionEntityId { get; private set; } = string.Empty;
    public string? CurrentProductionLiveValue { get; private set; }
    public string? CurrentProductionUnit { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<SummaryRow> MonthRows { get; private set; } = [];
    public List<PropertyDailyConsumptionPoint> DailyPoints { get; private set; } = [];
    public List<PropertyProductionLiveRow> AllPropertyProductionRows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.InitializeAsync(cancellationToken);
            await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);

            AvailableProperties = engine.GetAvailableProperties(store.Config);
            if (User.IsInRole(UserAccessService.AdminRole))
            {
                if (string.IsNullOrWhiteSpace(SelectedProperty)
                    || !AvailableProperties.Contains(SelectedProperty, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedProperty = AvailableProperties.FirstOrDefault();
                }

                PropertyName = SelectedProperty ?? string.Empty;
            }
            else
            {
                PropertyName = User.FindFirstValue("property") ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(PropertyName))
            {
                ErrorMessage = "Für diesen Benutzer ist keine Liegenschaft hinterlegt.";
                return Page();
            }

            var mapping = store.Config.PropertyMappings.FirstOrDefault(x => string.Equals(x.PropertyName, PropertyName, StringComparison.OrdinalIgnoreCase));
            CurrentProductionEntityId = mapping?.CurrentProductionEntityId ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(CurrentProductionEntityId))
            {
                var live = await haEntityService.GetEntityStateAsync(CurrentProductionEntityId, cancellationToken);
                CurrentProductionLiveValue = live?.State;
                CurrentProductionUnit = live?.UnitOfMeasurement;
            }

            var mappingsWithEntity = store.Config.PropertyMappings
                .Where(x => !string.IsNullOrWhiteSpace(x.PropertyName) && !string.IsNullOrWhiteSpace(x.CurrentProductionEntityId))
                .OrderBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var rowMapping in mappingsWithEntity)
            {
                var live = await haEntityService.GetEntityStateAsync(rowMapping.CurrentProductionEntityId, cancellationToken);
                AllPropertyProductionRows.Add(new PropertyProductionLiveRow
                {
                    PropertyName = rowMapping.PropertyName,
                    EntityId = rowMapping.CurrentProductionEntityId,
                    LiveValue = live?.State,
                    Unit = live?.UnitOfMeasurement
                });
            }

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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Daten konnten nicht geladen werden: {ex.Message}";
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

public sealed class PropertyProductionLiveRow
{
    public string PropertyName { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string? LiveValue { get; init; }
    public string? Unit { get; init; }
}
