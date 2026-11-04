using System.Text.Json;
using ZevEbixWeb.Models;

namespace ZevEbixWeb.Services;

public sealed class AppStateStore(BillingEngine engine, IWebHostEnvironment env)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _configPath = Path.Combine(GetDataDirectory(env), "zev-config-web.json");

    public AppConfig Config { get; private set; } = new();
    public List<MeterReading> MeterReadings { get; private set; } = [];
    public List<string> AvailableMeters { get; private set; } = [];
    public List<string> AvailableMonths { get; private set; } = [];
    public List<SummaryRow> SummaryRows { get; private set; } = [];
    public List<string> Warnings { get; private set; } = [];
    public string StatusText { get; private set; } = "Kein Ordner geladen";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            if (File.Exists(_configPath))
            {
                var json = await File.ReadAllTextAsync(_configPath, cancellationToken);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded is not null)
                {
                    Config = loaded;
                }
            }

            NormalizePriorities();
            EnsureMonthlyPricesForYear(DateTime.UtcNow.Year);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoadDataAsync(string sourcePathOrUrl, string? selectedMonth, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = await engine.LoadReadingsAsync(sourcePathOrUrl, cancellationToken);
            MeterReadings = result.Readings;
            AvailableMeters = result.AvailableMeters;
            AvailableMonths = engine.GetAvailableMonths(MeterReadings);
            Warnings = result.Warnings;
            StatusText = result.StatusText;

            EnsureMonthlyPricesForReadings();
            SummaryRows = engine.BuildSummary(Config, MeterReadings, selectedMonth);
            await SaveConfigInternalAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveConfigAndRefreshAsync(string? selectedMonth, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            NormalizePriorities();
            EnsureMonthlyPricesForReadings();
            SummaryRows = engine.BuildSummary(Config, MeterReadings, selectedMonth);
            await SaveConfigInternalAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveConfigInternalAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, json, cancellationToken);
    }

    private void NormalizePriorities()
    {
        var used = new HashSet<int>();
        var next = 1;
        foreach (var mapping in Config.PropertyMappings)
        {
            if (mapping.PvPriority <= 0 || used.Contains(mapping.PvPriority))
            {
                while (used.Contains(next))
                {
                    next++;
                }

                mapping.PvPriority = next;
            }

            used.Add(mapping.PvPriority);
        }
    }

    private void EnsureMonthlyPricesForReadings()
    {
        var months = MeterReadings.Select(x => x.PeriodStart.ToString("yyyy-MM")).Distinct().ToList();
        foreach (var month in months)
        {
            if (Config.MonthlyPrices.All(x => !string.Equals(x.MonthKey, month, StringComparison.Ordinal)))
            {
                Config.MonthlyPrices.Add(new MonthlyPrice { MonthKey = month });
            }
        }

        Config.MonthlyPrices = Config.MonthlyPrices.OrderBy(x => x.MonthKey, StringComparer.Ordinal).ToList();
    }

    private void EnsureMonthlyPricesForYear(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            var key = $"{year:D4}-{month:D2}";
            if (Config.MonthlyPrices.All(x => !string.Equals(x.MonthKey, key, StringComparison.Ordinal)))
            {
                Config.MonthlyPrices.Add(new MonthlyPrice { MonthKey = key });
            }
        }

        Config.MonthlyPrices = Config.MonthlyPrices.OrderBy(x => x.MonthKey, StringComparer.Ordinal).ToList();
    }

    private static string GetDataDirectory(IWebHostEnvironment env)
    {
        var configured = Environment.GetEnvironmentVariable("ZEVEBIX_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(env.ContentRootPath, "data");
    }
}
