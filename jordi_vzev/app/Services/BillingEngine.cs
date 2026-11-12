using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ZevEbixWeb.Models;

namespace ZevEbixWeb.Services;

public sealed class BillingEngine(IHttpClientFactory httpClientFactory)
{
    public const string PvDistributionModeFair = "FairShare";
    public const string PvDistributionModePriority = "Priority";

    public async Task<BillingLoadResult> LoadReadingsAsync(string sourcePathOrUrl, CancellationToken cancellationToken)
    {
        var localFolder = await ResolveDataSourceFolderAsync(sourcePathOrUrl, cancellationToken);
        var validation = ValidateDataFolderStructure(localFolder);
        var files = validation.DataFiles;

        var parsedFileCount = 0;
        var failedFileCount = 0;
        var uniqueReadings = new Dictionary<(string MeterId, DateTime PeriodStart), MeterReading>();

        var orderedFiles = files
            .OrderBy(x => File.GetLastWriteTimeUtc(x))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in orderedFiles)
        {
            try
            {
                var fileReadings = ParseDataFile(file);
                if (fileReadings.Count == 0)
                {
                    failedFileCount++;
                    continue;
                }

                foreach (var reading in fileReadings)
                {
                    uniqueReadings[(reading.MeterId, reading.PeriodStart)] = reading;
                }

                parsedFileCount++;
            }
            catch
            {
                failedFileCount++;
            }
        }

        var readings = uniqueReadings.Values
            .OrderBy(x => x.PeriodStart)
            .ThenBy(x => x.MeterId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var warnings = validation.Warnings;
        if (failedFileCount > 0)
        {
            warnings.Add($"{failedFileCount} Datei(en) konnten nicht ausgewertet werden.");
        }

        return new BillingLoadResult
        {
            Readings = readings,
            AvailableMeters = readings.Select(x => x.MeterId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            StatusText = $"Geladen: {parsedFileCount}/{files.Count} Dateien, {readings.Count} Werte",
            Warnings = warnings
        };
    }

    public List<string> GetAvailableMonths(IEnumerable<MeterReading> readings)
    {
        return readings
            .Select(x => ToMonthKey(x.PeriodStart))
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    public List<string> GetAvailableProperties(AppConfig config)
    {
        return config.PropertyMappings
            .Where(x => !string.IsNullOrWhiteSpace(x.PropertyName) && !string.IsNullOrWhiteSpace(x.GridMeterId))
            .Select(x => x.PropertyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PropertyHourlyConsumptionView? BuildHourlyConsumption(
        AppConfig config,
        IReadOnlyCollection<MeterReading> readings,
        string propertyName,
        DateOnly? selectedDate)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var mapping = config.PropertyMappings.FirstOrDefault(x =>
            string.Equals(x.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.GridMeterId));

        if (mapping is null)
        {
            return null;
        }

        var meterReadings = readings
            .Where(x => string.Equals(x.MeterId, mapping.GridMeterId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.PeriodStart)
            .ToList();

        if (meterReadings.Count == 0)
        {
            return null;
        }

        var availableDates = meterReadings
            .Select(x => DateOnly.FromDateTime(x.PeriodStart))
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();

        var targetDate = selectedDate ?? availableDates.First();
        if (!availableDates.Contains(targetDate))
        {
            targetDate = availableDates.First();
        }

        var hourlyValues = meterReadings
            .Where(x => DateOnly.FromDateTime(x.PeriodStart) == targetDate)
            .GroupBy(x => x.PeriodStart.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Kwh));

        var hourRows = Enumerable.Range(0, 24)
            .Select(hour => new PropertyHourlyConsumptionRow
            {
                Hour = hour,
                Kwh = hourlyValues.TryGetValue(hour, out var value) ? value : 0m
            })
            .ToList();

        return new PropertyHourlyConsumptionView
        {
            PropertyName = mapping.PropertyName,
            Date = targetDate,
            DailyTotalKwh = hourRows.Sum(x => x.Kwh),
            AvailableDates = availableDates,
            Hours = hourRows
        };
    }

    public PropertyDailyConsumptionView? BuildDailyConsumption(
        AppConfig config,
        IReadOnlyCollection<MeterReading> readings,
        string propertyName,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (readings.Count == 0)
        {
            return null;
        }

        var rangeStart = fromDate ?? DateOnly.FromDateTime(readings.Min(x => x.PeriodStart));
        var rangeEnd = toDate ?? DateOnly.FromDateTime(readings.Max(x => x.PeriodStart));

        var points = BuildConsumptionSeries(config, readings, propertyName, rangeStart, rangeEnd, ConsumptionGrouping.Day);
        if (points.Count == 0)
        {
            return null;
        }

        return new PropertyDailyConsumptionView
        {
            PropertyName = propertyName,
            Points = points.Select(x => new PropertyDailyConsumptionPoint
            {
                Date = DateOnly.FromDateTime(x.SortKey),
                GridKwh = x.GridKwh,
                PvKwh = x.PvKwh,
                TotalKwh = x.TotalKwh
            }).ToList()
        };
    }

    public List<ConsumptionSeriesPoint> BuildConsumptionSeries(
        AppConfig config,
        IReadOnlyCollection<MeterReading> readings,
        string propertyName,
        DateOnly fromDate,
        DateOnly toDate,
        ConsumptionGrouping grouping)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || config.PropertyMappings.Count == 0 || readings.Count == 0)
        {
            return [];
        }

        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var mode = NormalizeDistributionMode(config.PvDistributionMode);
        var mappings = config.PropertyMappings
            .OrderBy(x => x.PvPriority <= 0 ? int.MaxValue : x.PvPriority)
            .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var target = mappings.FirstOrDefault(x =>
            string.Equals(x.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.GridMeterId));

        if (target is null)
        {
            return [];
        }

        var demandByProperty = mappings.ToDictionary(x => x, x => GetMeterKwhByPeriodInRange(readings, x.GridMeterId, fromDate, toDate));
        var supplyByProducer = mappings.ToDictionary(x => x, x => GetMeterKwhByPeriodInRange(readings, x.PvMeterId, fromDate, toDate));

        var gridByBucket = new Dictionary<DateTime, decimal>();
        foreach (var kv in demandByProperty[target])
        {
            var bucket = ToBucketStart(kv.Key, grouping);
            gridByBucket[bucket] = gridByBucket.TryGetValue(bucket, out var existing) ? existing + kv.Value : kv.Value;
        }

        var pvByBucket = new Dictionary<DateTime, decimal>();
        var periods = demandByProperty.Values
            .SelectMany(x => x.Keys)
            .Concat(supplyByProducer.Values.SelectMany(x => x.Keys))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        foreach (var period in periods)
        {
            var remainingDemand = new Dictionary<PropertyMapping, decimal>();
            foreach (var mapping in mappings)
            {
                if (demandByProperty[mapping].TryGetValue(period, out var demand) && demand > 0m)
                {
                    remainingDemand[mapping] = demand;
                }
            }

            foreach (var producer in mappings)
            {
                if (!supplyByProducer[producer].TryGetValue(period, out var availableSupply) || availableSupply <= 0m)
                {
                    continue;
                }

                var consumers = remainingDemand.Where(x => x.Key != producer && x.Value > 0m).Select(x => x.Key).ToList();
                var states = mappings.ToDictionary(x => x, _ => new AllocationState());
                _ = mode == PvDistributionModePriority
                    ? DistributeByPriority(availableSupply, producer, consumers, remainingDemand, states)
                    : DistributeFairShare(availableSupply, producer, consumers, remainingDemand, states);

                if (states[target].PvKwh > 0m)
                {
                    var bucket = ToBucketStart(period, grouping);
                    pvByBucket[bucket] = pvByBucket.TryGetValue(bucket, out var existing) ? existing + states[target].PvKwh : states[target].PvKwh;
                }
            }
        }

        return gridByBucket.Keys
            .Union(pvByBucket.Keys)
            .OrderBy(x => x)
            .Select(bucket =>
            {
                var grid = gridByBucket.TryGetValue(bucket, out var g) ? g : 0m;
                var pv = pvByBucket.TryGetValue(bucket, out var p) ? p : 0m;
                return new ConsumptionSeriesPoint
                {
                    SortKey = bucket,
                    Label = FormatBucketLabel(bucket, grouping),
                    GridKwh = grid,
                    PvKwh = pv
                };
            })
            .Where(x => x.TotalKwh > 0m)
            .ToList();
    }

    public List<SummaryRow> BuildSummary(AppConfig config, IReadOnlyCollection<MeterReading> readings, string? selectedMonth)
    {
        if (config.PropertyMappings.Count == 0 || readings.Count == 0)
        {
            return [];
        }

        var mode = NormalizeDistributionMode(config.PvDistributionMode);
        var mappings = config.PropertyMappings
            .OrderBy(x => x.PvPriority <= 0 ? int.MaxValue : x.PvPriority)
            .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allMonths = readings
            .Select(x => ToMonthKey(x.PeriodStart))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var rows = new List<SummaryRow>();
        var monthsToEvaluate = selectedMonth is null ? allMonths : [selectedMonth];

        foreach (var month in monthsToEvaluate)
        {
            var price = GetPriceForMonth(config.MonthlyPrices, month);
            var states = mappings.ToDictionary(x => x, _ => new AllocationState());
            var demandByProperty = mappings.ToDictionary(x => x, x => GetMeterKwhByPeriod(readings, x.GridMeterId, month));
            var supplyByProducer = mappings.ToDictionary(x => x, x => GetMeterKwhByPeriod(readings, x.PvMeterId, month));
            var zevImportKwh = GetMeterKwhByPeriod(readings, config.ZevImportMeterId, month).Values.Sum();
            var zevExportKwh = GetMeterKwhByPeriod(readings, config.ZevExportMeterId, month).Values.Sum();

            foreach (var mapping in mappings)
            {
                states[mapping].TotalKwh = demandByProperty[mapping].Values.Sum();
            }

            var periods = demandByProperty.Values
                .SelectMany(x => x.Keys)
                .Concat(supplyByProducer.Values.SelectMany(x => x.Keys))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            foreach (var period in periods)
            {
                var remainingDemand = new Dictionary<PropertyMapping, decimal>();
                foreach (var mapping in mappings)
                {
                    if (demandByProperty[mapping].TryGetValue(period, out var demand) && demand > 0m)
                    {
                        remainingDemand[mapping] = demand;
                    }
                }

                foreach (var producer in mappings)
                {
                    if (!supplyByProducer[producer].TryGetValue(period, out var availableSupply) || availableSupply <= 0m)
                    {
                        continue;
                    }

                    var consumers = remainingDemand.Where(x => x.Key != producer && x.Value > 0m).Select(x => x.Key).ToList();
                    var distributedKwh = mode == PvDistributionModePriority
                        ? DistributeByPriority(availableSupply, producer, consumers, remainingDemand, states)
                        : DistributeFairShare(availableSupply, producer, consumers, remainingDemand, states);

                    var feedInKwh = availableSupply - distributedKwh;
                    if (feedInKwh > 0m)
                    {
                        states[producer].FeedInKwh += feedInKwh;
                    }
                }
            }

            foreach (var mapping in mappings)
            {
                var state = states[mapping];
                if (state.TotalKwh <= 0m && state.InternalSoldKwh <= 0m && state.FeedInKwh <= 0m)
                {
                    continue;
                }

                var gridKwh = Math.Max(0m, state.TotalKwh - state.PvKwh);
                var gridCost = gridKwh * price.GridPricePerKwh;
                var pvCost = state.PvKwh * price.PvPricePerKwh;
                var pvRevenue = (state.InternalSoldKwh * price.PvPricePerKwh) + (state.FeedInKwh * price.FeedInTariffPerKwh);
                var savedByNeighborPv = state.PvKwh * Math.Max(0m, price.GridPricePerKwh - price.PvPricePerKwh);
                var additionalRevenueVsFeedIn = state.InternalSoldKwh * (price.PvPricePerKwh - price.FeedInTariffPerKwh);

                rows.Add(new SummaryRow
                {
                    MonthLabel = month,
                    PropertyName = mapping.PropertyName,
                    TotalKwh = state.TotalKwh,
                    GridKwh = gridKwh,
                    PvKwh = state.PvKwh,
                    FeedInKwh = state.FeedInKwh,
                    ZevImportKwh = zevImportKwh,
                    ZevExportKwh = zevExportKwh,
                    GridCost = gridCost,
                    PvCost = pvCost,
                    PvRevenue = pvRevenue,
                    SavedByNeighborPv = savedByNeighborPv,
                    AdditionalRevenueVsFeedIn = additionalRevenueVsFeedIn
                });
            }
        }

        return rows;
    }

    private async Task<string> ResolveDataSourceFolderAsync(string source, CancellationToken cancellationToken)
    {
        var input = source.Trim();
        if (TryGetHttpUri(input, out var uri))
        {
            return await DownloadCloudDataToTempFolderAsync(uri, cancellationToken);
        }

        if (!Directory.Exists(input))
        {
            throw new DirectoryNotFoundException("Der konfigurierte Ordner wurde nicht gefunden.");
        }

        return input;
    }

    private async Task<string> DownloadCloudDataToTempFolderAsync(Uri cloudUri, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(ToDropboxDownloadUri(cloudUri), cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(contentType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Der Link liefert eine Webseite statt Daten.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "ZevEbixWeb", "CloudData", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? Path.GetFileName(cloudUri.LocalPath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "cloud-data.zip";
        }

        fileName = fileName.Trim('"');
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            fileName = "cloud-data.zip";
        }

        var downloadedFilePath = Path.Combine(tempRoot, fileName);
        await using (var fs = File.Create(downloadedFilePath))
        {
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        var extension = Path.GetExtension(downloadedFilePath);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-zip-compressed", StringComparison.OrdinalIgnoreCase))
        {
            var extractFolder = Path.Combine(tempRoot, "extracted");
            Directory.CreateDirectory(extractFolder);
            if (ExtractZipSafely(downloadedFilePath, extractFolder) == 0)
            {
                throw new InvalidOperationException("Die ZIP-Datei enthält keine gültigen CSV/XML-Datendateien.");
            }

            return extractFolder;
        }

        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return tempRoot;
        }

        throw new InvalidOperationException("Der Cloud-Link muss eine ZIP-, CSV- oder XML-Datei liefern.");
    }

    private static int ExtractZipSafely(string zipPath, string destinationFolder)
    {
        var extractedFileCount = 0;
        var destinationRoot = Path.GetFullPath(destinationFolder);
        if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            destinationRoot += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, true);
            var ext = Path.GetExtension(targetPath);
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase))
            {
                extractedFileCount++;
            }
        }

        return extractedFileCount;
    }

    private static Uri ToDropboxDownloadUri(Uri uri)
    {
        if (!uri.Host.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var url = uri.ToString();
        if (Regex.IsMatch(url, @"([?&])dl=\d", RegexOptions.IgnoreCase))
        {
            url = Regex.Replace(url, @"([?&])dl=\d", "$1dl=1", RegexOptions.IgnoreCase);
        }
        else
        {
            url = url.Contains('?') ? $"{url}&dl=1" : $"{url}?dl=1";
        }

        return new Uri(url);
    }

    private static bool TryGetHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static FolderValidationResult ValidateDataFolderStructure(string rootFolder)
    {
        var result = new FolderValidationResult();
        result.DataFiles = Directory
            .EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories)
            .Where(x => string.Equals(Path.GetExtension(x), ".xml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(x), ".csv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .ToList();

        var monthFolders = Directory.EnumerateDirectories(rootFolder).OrderBy(x => x).ToList();
        if (monthFolders.Count == 0)
        {
            result.Warnings.Add("Es wurden keine Monats-Unterordner im Root-Ordner gefunden.");
            return result;
        }

        return result;
    }

    private static List<MeterReading> ParseDataFile(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase)
            ? ParseCsv(filePath)
            : ParseEbix(filePath);
    }

    private static List<MeterReading> ParseCsv(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 3)
        {
            return [];
        }

        var separator = lines[0].Contains(';') ? ';' : ',';
        var rows = lines.Select(x => x.Split(separator)).ToList();

        var meteringCodeRow = rows.FirstOrDefault(x => x.Length > 1
            && string.Equals(x[0].Trim(), "Metering-Code", StringComparison.OrdinalIgnoreCase));
        if (meteringCodeRow is null)
        {
            return [];
        }

        var meterColumns = new List<(int ColumnIndex, string MeterId)>();
        for (var i = 1; i < meteringCodeRow.Length; i++)
        {
            var meterId = meteringCodeRow[i].Trim();
            if (!string.IsNullOrWhiteSpace(meterId))
            {
                meterColumns.Add((i, meterId));
            }
        }

        var result = new List<MeterReading>();
        var deCh = CultureInfo.GetCultureInfo("de-CH");

        foreach (var row in rows)
        {
            if (row.Length == 0)
            {
                continue;
            }

            var firstColumn = row[0].Trim();
            if (!DateTime.TryParse(firstColumn, deCh, DateTimeStyles.AssumeLocal, out var timestamp)
                && !DateTime.TryParse(firstColumn, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp))
            {
                continue;
            }

            timestamp = NormalizeToQuarterHour(timestamp);
            foreach (var column in meterColumns)
            {
                if (column.ColumnIndex >= row.Length)
                {
                    continue;
                }

                var value = ParseDecimalFlexible(row[column.ColumnIndex]);
                if (!value.HasValue || value.Value <= 0m)
                {
                    continue;
                }

                result.Add(new MeterReading(column.MeterId, timestamp, value.Value));
            }
        }

        return result
            .GroupBy(x => new { x.MeterId, x.PeriodStart })
            .Select(g => new MeterReading(g.Key.MeterId, g.Key.PeriodStart, g.Sum(x => x.Kwh)))
            .ToList();
    }

    private static List<MeterReading> ParseEbix(string filePath)
    {
        var document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        var fallbackDate = File.GetLastWriteTime(filePath);
        var result = new List<MeterReading>();

        var periods = document.Descendants().Where(x => x.Name.LocalName.Contains("period", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var period in periods)
        {
            var meterId = ExtractMeterId(period)
                ?? period.Ancestors().Select(ExtractMeterId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (string.IsNullOrWhiteSpace(meterId))
            {
                continue;
            }

            var values = ExtractQuantities(period);
            if (values.Count == 0)
            {
                continue;
            }

            var date = ExtractDateTime(period)
                ?? period.Ancestors().Select(ExtractDateTime).FirstOrDefault(x => x.HasValue)
                ?? fallbackDate;

            result.Add(new MeterReading(meterId, NormalizeToQuarterHour(date), values.Sum()));
        }

        return result
            .GroupBy(x => new { x.MeterId, x.PeriodStart })
            .Select(g => new MeterReading(g.Key.MeterId, g.Key.PeriodStart, g.Sum(x => x.Kwh)))
            .ToList();
    }

    private static string? ExtractMeterId(XElement scope)
    {
        var direct = scope.Descendants().FirstOrDefault(x => x.Name.LocalName.Contains("meter", StringComparison.OrdinalIgnoreCase)
            || x.Name.LocalName.Contains("point", StringComparison.OrdinalIgnoreCase));

        if (direct is not null)
        {
            var match = Regex.Match(direct.Value, @"\bCH[0-9A-Z]{10,}\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value;
            }
        }

        var anyTextMatch = Regex.Match(scope.Value, @"\bCH[0-9A-Z]{10,}\b", RegexOptions.IgnoreCase);
        return anyTextMatch.Success ? anyTextMatch.Value : null;
    }

    private static List<decimal> ExtractQuantities(XElement scope)
    {
        return scope.Descendants()
            .Where(x => x.Name.LocalName.Contains("qty", StringComparison.OrdinalIgnoreCase)
                || x.Name.LocalName.Contains("quantity", StringComparison.OrdinalIgnoreCase)
                || x.Name.LocalName.Contains("value", StringComparison.OrdinalIgnoreCase))
            .Select(x => ParseDecimalFlexible(x.Value))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Where(x => x > 0m)
            .ToList();
    }

    private static DateTime? ExtractDateTime(XElement scope)
    {
        var dateCandidates = scope.Descendants()
            .Where(x => x.Name.LocalName.Contains("start", StringComparison.OrdinalIgnoreCase)
                || x.Name.LocalName.Contains("date", StringComparison.OrdinalIgnoreCase)
                || x.Name.LocalName.Contains("time", StringComparison.OrdinalIgnoreCase));

        foreach (var candidate in dateCandidates)
        {
            if (DateTime.TryParse(candidate.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                || DateTime.TryParse(candidate.Value, CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.AssumeLocal, out parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static decimal? ParseDecimalFlexible(string value)
    {
        var trimmed = value.Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedInvariant))
        {
            return parsedInvariant;
        }

        var normalized = trimmed.Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedNormalized))
        {
            return parsedNormalized;
        }

        return null;
    }

    private static DateTime NormalizeToQuarterHour(DateTime value)
    {
        var roundedMinute = (value.Minute / 15) * 15;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, roundedMinute, 0, value.Kind);
    }

    private static string ToMonthKey(DateTime value)
    {
        return value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    private static DateTime ToBucketStart(DateTime value, ConsumptionGrouping grouping)
    {
        return grouping switch
        {
            ConsumptionGrouping.Hour => new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Kind),
            ConsumptionGrouping.Day => value.Date,
            ConsumptionGrouping.Month => new DateTime(value.Year, value.Month, 1, 0, 0, 0, value.Kind),
            ConsumptionGrouping.Year => new DateTime(value.Year, 1, 1, 0, 0, 0, value.Kind),
            _ => value.Date
        };
    }

    private static string FormatBucketLabel(DateTime bucket, ConsumptionGrouping grouping)
    {
        return grouping switch
        {
            ConsumptionGrouping.Hour => bucket.ToString("HH:00", CultureInfo.InvariantCulture),
            ConsumptionGrouping.Day => bucket.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ConsumptionGrouping.Month => bucket.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            ConsumptionGrouping.Year => bucket.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => bucket.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        };
    }

    private static Dictionary<DateTime, decimal> GetMeterKwhByPeriod(IEnumerable<MeterReading> readings, string meterId, string month)
    {
        if (string.IsNullOrWhiteSpace(meterId))
        {
            return [];
        }

        return readings
            .Where(x => string.Equals(x.MeterId, meterId, StringComparison.OrdinalIgnoreCase)
                        && ToMonthKey(x.PeriodStart) == month)
            .GroupBy(x => x.PeriodStart)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Kwh));
    }

    private static Dictionary<DateTime, decimal> GetMeterKwhByPeriodInRange(
        IEnumerable<MeterReading> readings,
        string meterId,
        DateOnly from,
        DateOnly to)
    {
        if (string.IsNullOrWhiteSpace(meterId))
        {
            return [];
        }

        return readings
            .Where(x => string.Equals(x.MeterId, meterId, StringComparison.OrdinalIgnoreCase))
            .Where(x =>
            {
                var day = DateOnly.FromDateTime(x.PeriodStart);
                return day >= from && day <= to;
            })
            .GroupBy(x => x.PeriodStart)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Kwh));
    }

    private static string NormalizeDistributionMode(string? mode)
    {
        return string.Equals(mode, PvDistributionModePriority, StringComparison.OrdinalIgnoreCase)
            ? PvDistributionModePriority
            : PvDistributionModeFair;
    }

    private static MonthlyPrice GetPriceForMonth(IEnumerable<MonthlyPrice> prices, string month)
    {
        return prices.FirstOrDefault(x => string.Equals(x.MonthKey, month, StringComparison.Ordinal))
            ?? new MonthlyPrice { MonthKey = month };
    }

    private static decimal DistributeByPriority(
        decimal availableSupply,
        PropertyMapping producer,
        IReadOnlyCollection<PropertyMapping> consumers,
        IDictionary<PropertyMapping, decimal> remainingDemand,
        IDictionary<PropertyMapping, AllocationState> states)
    {
        var remainingSupply = availableSupply;
        var orderedConsumers = consumers
            .OrderBy(x => x.PvPriority <= 0 ? int.MaxValue : x.PvPriority)
            .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var consumer in orderedConsumers)
        {
            if (!remainingDemand.TryGetValue(consumer, out var demand) || demand <= 0m || remainingSupply <= 0m)
            {
                continue;
            }

            var allocated = Math.Min(demand, remainingSupply);
            remainingDemand[consumer] -= allocated;
            remainingSupply -= allocated;
            states[consumer].PvKwh += allocated;
            states[producer].InternalSoldKwh += allocated;
        }

        return availableSupply - remainingSupply;
    }

    private static decimal DistributeFairShare(
        decimal availableSupply,
        PropertyMapping producer,
        IReadOnlyCollection<PropertyMapping> consumers,
        IDictionary<PropertyMapping, decimal> remainingDemand,
        IDictionary<PropertyMapping, AllocationState> states)
    {
        var remainingSupply = availableSupply;
        var active = consumers.Where(x => remainingDemand.TryGetValue(x, out var d) && d > 0m).ToList();

        while (remainingSupply > 0m && active.Count > 0)
        {
            var totalDemand = active.Sum(x => remainingDemand[x]);
            if (totalDemand <= 0m)
            {
                break;
            }

            var distributedInRound = 0m;
            foreach (var consumer in active)
            {
                var demand = remainingDemand[consumer];
                if (demand <= 0m)
                {
                    continue;
                }

                var share = remainingSupply * (demand / totalDemand);
                var allocated = Math.Min(demand, share);
                if (allocated <= 0m)
                {
                    continue;
                }

                remainingDemand[consumer] -= allocated;
                distributedInRound += allocated;
                states[consumer].PvKwh += allocated;
                states[producer].InternalSoldKwh += allocated;
            }

            remainingSupply -= distributedInRound;
            active = active.Where(x => remainingDemand[x] > 0m).ToList();
            if (distributedInRound > 0m)
            {
                continue;
            }

            foreach (var consumer in active.OrderBy(x => x.PvPriority <= 0 ? int.MaxValue : x.PvPriority)
                         .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase))
            {
                if (remainingSupply <= 0m)
                {
                    break;
                }

                var demand = remainingDemand[consumer];
                if (demand <= 0m)
                {
                    continue;
                }

                var allocated = Math.Min(demand, remainingSupply);
                remainingDemand[consumer] -= allocated;
                remainingSupply -= allocated;
                states[consumer].PvKwh += allocated;
                states[producer].InternalSoldKwh += allocated;
            }
        }

        return availableSupply - remainingSupply;
    }

    private sealed class AllocationState
    {
        public decimal TotalKwh { get; set; }
        public decimal PvKwh { get; set; }
        public decimal InternalSoldKwh { get; set; }
        public decimal FeedInKwh { get; set; }
    }

    private sealed class FolderValidationResult
    {
        public List<string> Warnings { get; } = [];
        public List<string> DataFiles { get; set; } = [];
    }
}
