namespace ZevEbixWeb.Models;

public sealed class AppConfig
{
    public string DataFolderPath { get; set; } = string.Empty;
    public string PvDistributionMode { get; set; } = "FairShare";
    public string ZevImportMeterId { get; set; } = string.Empty;
    public string ZevExportMeterId { get; set; } = string.Empty;
    public List<PropertyMapping> PropertyMappings { get; set; } = [];
    public List<MonthlyPrice> MonthlyPrices { get; set; } = [];
}
