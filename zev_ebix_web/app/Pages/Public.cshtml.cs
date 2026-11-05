using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Models;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

public sealed class PublicModel(AppStateStore store) : PageModel
{
    public string StatusText => store.StatusText;
    public List<SummaryRow> SummaryRows => store.SummaryRows;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        await store.SaveConfigAndRefreshAsync(selectedMonth: null, cancellationToken);
    }
}
