using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Models;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

public class IndexModel(AppStateStore store) : PageModel
{
    [BindProperty]
    public string DataSource { get; set; } = string.Empty;

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

    public string StatusText => store.StatusText;
    public List<string> Warnings => store.Warnings;
    public List<string> AvailableMeters => store.AvailableMeters;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        LoadFromState();
    }

    public async Task<IActionResult> OnPostLoadDataAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();

        if (string.IsNullOrWhiteSpace(DataSource))
        {
            ModelState.AddModelError(string.Empty, "Bitte einen lokalen Ordner oder Dropbox-Link eingeben.");
            LoadFromState();
            return Page();
        }

        try
        {
            await store.LoadDataAsync(DataSource, selectedMonth: null, cancellationToken);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        LoadFromState();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveConfigAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
        LoadFromState();
        return Page();
    }

    public async Task<IActionResult> OnPostAddPropertyAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();
        store.Config.PropertyMappings.Add(new PropertyMapping { PvPriority = store.Config.PropertyMappings.Count + 1 });
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
        LoadFromState();
        return Page();
    }

    public async Task<IActionResult> OnPostRemovePropertyAsync(int index, CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();
        if (index >= 0 && index < store.Config.PropertyMappings.Count)
        {
            store.Config.PropertyMappings.RemoveAt(index);
        }

        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
        LoadFromState();
        return Page();
    }

    public async Task<IActionResult> OnPostAddPriceAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();
        store.Config.MonthlyPrices.Add(new MonthlyPrice());
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
        LoadFromState();
        return Page();
    }

    public async Task<IActionResult> OnPostRemovePriceAsync(int index, CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        ApplyToState();
        if (index >= 0 && index < store.Config.MonthlyPrices.Count)
        {
            store.Config.MonthlyPrices.RemoveAt(index);
        }

        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
        LoadFromState();
        return Page();
    }

    private void ApplyToState()
    {
        store.Config.DataFolderPath = DataSource.Trim();
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
        DataSource = store.Config.DataFolderPath;
        PvDistributionMode = store.Config.PvDistributionMode;
        ZevImportMeterId = store.Config.ZevImportMeterId;
        ZevExportMeterId = store.Config.ZevExportMeterId;
        PropertyMappings = store.Config.PropertyMappings.Select(x => new PropertyMapping
        {
            PropertyName = x.PropertyName,
            GridMeterId = x.GridMeterId,
            PvMeterId = x.PvMeterId,
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
