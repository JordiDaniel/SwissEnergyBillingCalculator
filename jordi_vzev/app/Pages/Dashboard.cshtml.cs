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

    [BindProperty(SupportsGet = true, Name = "mode")]
    public string Mode { get; set; } = "day";

    [BindProperty(SupportsGet = true, Name = "day")]
    public string? Day { get; set; }

    [BindProperty(SupportsGet = true, Name = "week")]
    public int? Week { get; set; }

    [BindProperty(SupportsGet = true, Name = "weekValue")]
    public string? WeekValue { get; set; }

    [BindProperty(SupportsGet = true, Name = "month")]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true, Name = "year")]
    public int? Year { get; set; }

    public string PropertyName { get; private set; } = string.Empty;
    public List<string> AvailableProperties { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public List<SummaryRow> MonthRows { get; private set; } = [];
    public List<ConsumptionSeriesPoint> ConsumptionPoints { get; private set; } = [];
    public List<PropertyProductionLiveRow> AllPropertyProductionRows { get; private set; } = [];

    public DateOnly SelectedDay { get; private set; }
    public DateOnly PrevDay { get; private set; }
    public DateOnly NextDay { get; private set; }
    public int SelectedWeek { get; private set; }
    public string SelectedWeekValue { get; private set; } = string.Empty;
    public int SelectedMonth { get; private set; }
    public int SelectedYear { get; private set; }
    public int PrevWeek { get; private set; }
    public int PrevWeekYear { get; private set; }
    public int NextWeek { get; private set; }
    public int NextWeekYear { get; private set; }
    public int PrevMonth { get; private set; }
    public int PrevMonthYear { get; private set; }
    public int NextMonth { get; private set; }
    public int NextMonthYear { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.InitializeAsync(cancellationToken);
            await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);

            if (store.MeterReadings.Count == 0)
            {
                ErrorMessage = "Keine Verbrauchsdaten vorhanden.";
                return Page();
            }

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

            var mappingsWithEntity = store.Config.PropertyMappings
                .Where(x => !string.IsNullOrWhiteSpace(x.PropertyName) && !string.IsNullOrWhiteSpace(x.CurrentProductionEntityId))
                .OrderBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var rowMapping in mappingsWithEntity)
            {
                var live = await haEntityService.GetEntityStateAsync(rowMapping.CurrentProductionEntityId, cancellationToken);
                decimal normalizedKw = 0m;
                if (live is not null
                    && decimal.TryParse(live.State, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    && parsed > 0m)
                {
                    var unit = (live.UnitOfMeasurement ?? string.Empty).Trim();
                    normalizedKw = unit.Equals("W", StringComparison.OrdinalIgnoreCase)
                        ? parsed / 1000m
                        : parsed;
                }

                AllPropertyProductionRows.Add(new PropertyProductionLiveRow
                {
                    PropertyName = rowMapping.PropertyName,
                    LiveKw = normalizedKw,
                    IsAvailable = live is not null
                });
            }

            MonthRows = engine.BuildSummary(store.Config, store.MeterReadings, selectedMonth: null)
                .Where(x => string.Equals(x.PropertyName, PropertyName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.MonthLabel)
                .ToList();

            var latestDataDate = DateOnly.FromDateTime(store.MeterReadings.Max(x => x.PeriodStart));
            var firstDataDate = DateOnly.FromDateTime(store.MeterReadings.Min(x => x.PeriodStart));
            var parsedIsoWeek = ParseIsoWeekValue(WeekValue);
            SelectedYear = parsedIsoWeek?.Year ?? Year ?? latestDataDate.Year;
            SelectedMonth = Month ?? latestDataDate.Month;
            SelectedWeek = parsedIsoWeek?.Week ?? Week ?? ISOWeek.GetWeekOfYear(latestDataDate.ToDateTime(TimeOnly.MinValue));
            SelectedWeekValue = FormatIsoWeekValue(SelectedYear, SelectedWeek);
            SelectedDay = ParseDateOrDefault(Day, latestDataDate);

            var normalizedMode = (Mode ?? "day").ToLowerInvariant();
            if (normalizedMode is not ("day" or "week" or "month" or "year"))
            {
                normalizedMode = "day";
            }

            Mode = normalizedMode;

            DateOnly fromDate;
            DateOnly toDate;
            ConsumptionGrouping grouping;

            switch (Mode)
            {
                case "day":
                    fromDate = SelectedDay;
                    toDate = SelectedDay;
                    grouping = ConsumptionGrouping.Hour;
                    PrevDay = SelectedDay.AddDays(-1);
                    NextDay = SelectedDay.AddDays(1);
                    break;
                case "week":
                    var weekStart = FirstDateOfIsoWeek(SelectedYear, SelectedWeek);
                    fromDate = DateOnly.FromDateTime(weekStart);
                    toDate = fromDate.AddDays(6);
                    grouping = ConsumptionGrouping.Day;

                    var prevWeekStart = weekStart.AddDays(-7);
                    PrevWeekYear = ISOWeek.GetYear(prevWeekStart);
                    PrevWeek = ISOWeek.GetWeekOfYear(prevWeekStart);
                    var nextWeekStart = weekStart.AddDays(7);
                    NextWeekYear = ISOWeek.GetYear(nextWeekStart);
                    NextWeek = ISOWeek.GetWeekOfYear(nextWeekStart);
                    break;
                case "month":
                    fromDate = new DateOnly(SelectedYear, 1, 1);
                    toDate = new DateOnly(SelectedYear, 12, 31);
                    grouping = ConsumptionGrouping.Month;
                    break;
                default:
                    fromDate = new DateOnly(firstDataDate.Year, 1, 1);
                    toDate = new DateOnly(latestDataDate.Year, 12, 31);
                    grouping = ConsumptionGrouping.Year;
                    break;
            }

            if (fromDate < firstDataDate)
            {
                fromDate = firstDataDate;
            }

            if (toDate > latestDataDate)
            {
                toDate = latestDataDate;
            }

            if (fromDate <= toDate)
            {
                ConsumptionPoints = engine.BuildConsumptionSeries(store.Config, store.MeterReadings, PropertyName, fromDate, toDate, grouping);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Daten konnten nicht geladen werden: {ex.Message}";
        }

        return Page();
    }

    private static DateOnly ParseDateOrDefault(string? value, DateOnly fallback)
    {
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }

    private static (int Year, int Week)? ParseIsoWeekValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split("-W", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var week)
            || year < 1
            || week is < 1 or > 53)
        {
            return null;
        }

        return (year, week);
    }

    private static string FormatIsoWeekValue(int year, int week)
    {
        var normalizedWeek = Math.Clamp(week, 1, 53);
        return $"{year:D4}-W{normalizedWeek:D2}";
    }

    private static DateTime FirstDateOfIsoWeek(int year, int weekOfYear)
    {
        var jan4 = new DateTime(year, 1, 4);
        var dayOfWeek = (int)jan4.DayOfWeek;
        if (dayOfWeek == 0)
        {
            dayOfWeek = 7;
        }

        var monday = jan4.AddDays(1 - dayOfWeek);
        return monday.AddDays((weekOfYear - 1) * 7);
    }
}

public sealed class PropertyProductionLiveRow
{
    public string PropertyName { get; init; } = string.Empty;
    public decimal LiveKw { get; init; }
    public bool IsAvailable { get; init; }
}
