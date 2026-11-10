namespace ZevEbixWeb.Models;

public sealed class PropertyMapping
{
    public string PropertyName { get; set; } = string.Empty;
    public string GridMeterId { get; set; } = string.Empty;
    public string PvMeterId { get; set; } = string.Empty;
    public string CurrentProductionEntityId { get; set; } = string.Empty;
    public int PvPriority { get; set; }
}

public sealed class MonthlyPrice
{
    public string MonthKey { get; set; } = string.Empty;
    public decimal GridPricePerKwh { get; set; }
    public decimal PvPricePerKwh { get; set; }
    public decimal FeedInTariffPerKwh { get; set; }
}

public sealed class MeterReading
{
    public MeterReading(string meterId, DateTime periodStart, decimal kwh)
    {
        MeterId = meterId;
        PeriodStart = periodStart;
        Kwh = kwh;
    }

    public string MeterId { get; }
    public DateTime PeriodStart { get; }
    public decimal Kwh { get; }
}

public sealed class SummaryRow
{
    public string MonthLabel { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public decimal TotalKwh { get; init; }
    public decimal GridKwh { get; init; }
    public decimal PvKwh { get; init; }
    public decimal FeedInKwh { get; init; }
    public decimal ZevImportKwh { get; init; }
    public decimal ZevExportKwh { get; init; }
    public decimal GridCost { get; init; }
    public decimal PvCost { get; init; }
    public decimal SavedByNeighborPv { get; init; }
    public decimal AdditionalRevenueVsFeedIn { get; init; }
    public decimal PvRevenue { get; init; }
    public decimal TotalCost => GridCost + PvCost - PvRevenue;
}

public sealed class PropertyHourlyConsumptionRow
{
    public int Hour { get; init; }
    public decimal Kwh { get; init; }
}

public sealed class PropertyHourlyConsumptionView
{
    public string PropertyName { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public decimal DailyTotalKwh { get; init; }
    public List<DateOnly> AvailableDates { get; init; } = [];
    public List<PropertyHourlyConsumptionRow> Hours { get; init; } = [];
}

public sealed class PropertyDailyConsumptionPoint
{
    public DateOnly Date { get; init; }
    public decimal TotalKwh { get; init; }
    public decimal GridKwh { get; init; }
    public decimal PvKwh { get; init; }
}

public sealed class PropertyDailyConsumptionView
{
    public string PropertyName { get; init; } = string.Empty;
    public List<PropertyDailyConsumptionPoint> Points { get; init; } = [];
}

public sealed class BillingLoadResult
{
    public required List<MeterReading> Readings { get; init; }
    public required List<string> AvailableMeters { get; init; }
    public required string StatusText { get; init; }
    public required List<string> Warnings { get; init; }
}
