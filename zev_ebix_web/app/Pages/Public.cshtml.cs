using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Models;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

public sealed class PublicModel(AppStateStore store, BillingEngine engine) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "property")]
    public string? PropertyName { get; set; }

    [BindProperty(SupportsGet = true, Name = "day")]
    public string? Day { get; set; }

    public string StatusText => store.StatusText;
    public List<string> AvailableProperties { get; private set; } = [];
    public PropertyHourlyConsumptionView? HourlyConsumption { get; private set; }
    public string? InfoText { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);

        AvailableProperties = engine.GetAvailableProperties(store.Config);
        if (AvailableProperties.Count == 0)
        {
            InfoText = "Keine Liegenschaften mit Bezugszähler gefunden.";
            return;
        }

        if (string.IsNullOrWhiteSpace(PropertyName)
            || !AvailableProperties.Contains(PropertyName, StringComparer.OrdinalIgnoreCase))
        {
            PropertyName = AvailableProperties[0];
        }

        DateOnly? selectedDate = null;
        if (!string.IsNullOrWhiteSpace(Day)
            && DateOnly.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            selectedDate = parsedDate;
        }

        HourlyConsumption = engine.BuildHourlyConsumption(store.Config, store.MeterReadings, PropertyName, selectedDate);
        if (HourlyConsumption is null)
        {
            InfoText = "Für die ausgewählte Liegenschaft sind keine Daten verfügbar.";
            return;
        }

        Day = HourlyConsumption.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
