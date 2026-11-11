using System.Net.Http.Headers;
using System.Text.Json;

namespace ZevEbixWeb.Services;

public sealed class HomeAssistantEntityService(IHttpClientFactory httpClientFactory)
{
    private const string HomeAssistantUrlEnv = "HA_URL";
    private const string HomeAssistantTokenEnv = "HA_TOKEN";

    public async Task<HomeAssistantEntityState?> GetEntityStateAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var token = Environment.GetEnvironmentVariable(HomeAssistantTokenEnv);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var baseUrl = Environment.GetEnvironmentVariable(HomeAssistantUrlEnv) ?? "http://192.168.178.60:8123";

        var requestUrl = $"{baseUrl.TrimEnd('/')}/api/states/{Uri.EscapeDataString(entityId)}";

        var client = httpClientFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var state = doc.RootElement.TryGetProperty("state", out var stateProp)
                ? stateProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(state) ||
                string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "unavailable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? unit = null;

            if (doc.RootElement.TryGetProperty("attributes", out var attributes) &&
                attributes.ValueKind == JsonValueKind.Object &&
                attributes.TryGetProperty("unit_of_measurement", out var unitProp))
            {
                unit = unitProp.GetString();
            }

            return new HomeAssistantEntityState(state, unit);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record HomeAssistantEntityState(string State, string? UnitOfMeasurement);