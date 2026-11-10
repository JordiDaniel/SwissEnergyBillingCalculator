using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Models;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

[Authorize(Roles = UserAccessService.AdminRole)]
public class AdminModel(AppStateStore store) : PageModel
{
    [BindProperty]
    public string DataSource { get; set; } = string.Empty;

    public List<string> AvailableMeters { get; private set; } = [];

    [BindProperty]
    public string PvDistributionMode { get; set; } = BillingEngine.PvDistributionModeFair;

    [BindProperty]
    public string ZevImportMeterId { get; set; } = string.Empty;

    [BindProperty]
    public string ZevExportMeterId { get; set; } = string.Empty;

    [BindProperty]
    public List<PropertyMapping> PropertyMappings { get; set; } = [];

    [BindProperty]
    public List<MonthlyPrice> MonthlyPrices { get; set; } = [];

    public string? Message { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(store.Config.DataFolderPath))
        {
            await store.LoadDataAsync(store.Config.DataFolderPath, selectedMonth: null, cancellationToken);
        }

        LoadFromState();
    }

    public async Task<IActionResult> OnPostAsync(string? command, int? removePropertyIndex, int? removePriceIndex, CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();

        if (removePropertyIndex.HasValue && removePropertyIndex.Value >= 0 && removePropertyIndex.Value < store.Config.PropertyMappings.Count)
        {
            store.Config.PropertyMappings.RemoveAt(removePropertyIndex.Value);
            await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
            LoadFromState();
            Message = "Liegenschaft entfernt.";
            return Page();
        }

        if (removePriceIndex.HasValue && removePriceIndex.Value >= 0 && removePriceIndex.Value < store.Config.MonthlyPrices.Count)
        {
            store.Config.MonthlyPrices.RemoveAt(removePriceIndex.Value);
            await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
            LoadFromState();
            Message = "Preiszeile entfernt.";
            return Page();
        }

        switch ((command ?? string.Empty).ToLowerInvariant())
        {
            case "addproperty":
                store.Config.PropertyMappings.Add(new PropertyMapping { PvPriority = store.Config.PropertyMappings.Count + 1 });
                Message = "Liegenschaft hinzugefügt.";
                break;
            case "addprice":
                store.Config.MonthlyPrices.Add(new MonthlyPrice());
                Message = "Preiszeile hinzugefügt.";
                break;
            case "reload":
                Message = "Daten neu geladen.";
                break;
            default:
                Message = "Konfiguration gespeichert.";
                break;
        }

        if (!string.IsNullOrWhiteSpace(store.Config.DataFolderPath))
        {
            await store.LoadDataAsync(store.Config.DataFolderPath, selectedMonth: null, cancellationToken);
        }

        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
        LoadFromState();
        return Page();
    }

    private void ApplyToState()
    {
        store.Config.DataFolderPath = string.IsNullOrWhiteSpace(DataSource) ? "/share/vzev" : DataSource.Trim();
        store.Config.PvDistributionMode = string.Equals(PvDistributionMode, BillingEngine.PvDistributionModePriority, StringComparison.OrdinalIgnoreCase)
            ? BillingEngine.PvDistributionModePriority
            : BillingEngine.PvDistributionModeFair;
        store.Config.ZevImportMeterId = ZevImportMeterId?.Trim() ?? string.Empty;
        store.Config.ZevExportMeterId = ZevExportMeterId?.Trim() ?? string.Empty;

        store.Config.PropertyMappings = PropertyMappings
            .Where(x => !string.IsNullOrWhiteSpace(x.PropertyName) || !string.IsNullOrWhiteSpace(x.GridMeterId) || !string.IsNullOrWhiteSpace(x.PvMeterId))
            .ToList();

        store.Config.MonthlyPrices = MonthlyPrices
            .Where(x => !string.IsNullOrWhiteSpace(x.MonthKey))
            .ToList();
    }

    private void LoadFromState()
    {
        DataSource = string.IsNullOrWhiteSpace(store.Config.DataFolderPath) ? "/share/vzev" : store.Config.DataFolderPath;
        PvDistributionMode = store.Config.PvDistributionMode;
        ZevImportMeterId = store.Config.ZevImportMeterId;
        ZevExportMeterId = store.Config.ZevExportMeterId;
        AvailableMeters = store.AvailableMeters;

        PropertyMappings = store.Config.PropertyMappings.Select(x => new PropertyMapping
        {
            PropertyName = x.PropertyName,
            GridMeterId = x.GridMeterId,
            PvMeterId = x.PvMeterId,
            CurrentProductionEntityId = x.CurrentProductionEntityId,
            PvPriority = x.PvPriority
        }).ToList();

        MonthlyPrices = store.Config.MonthlyPrices.Select(x => new MonthlyPrice
        {
            MonthKey = x.MonthKey,
            GridPricePerKwh = x.GridPricePerKwh,
            PvPricePerKwh = x.PvPricePerKwh,
            FeedInTariffPerKwh = x.FeedInTariffPerKwh
        }).ToList();
    }
}
