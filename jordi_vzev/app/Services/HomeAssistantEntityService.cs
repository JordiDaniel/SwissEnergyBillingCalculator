using System.Net.Http.Headers;
using System.Text.Json;

namespace ZevEbixWeb.Services;

public sealed class HomeAssistantEntityService(IHttpClientFactory httpClientFactory)
{
    private const string SupervisorTokenHeader = "X-Supervisor-Token";
    private const string SupervisorTokenEnv = "SUPERVISOR_TOKEN";
    private const string HaStatesUrlPrefix = "http://supervisor/core/api/states/";

    public async Task<HomeAssistantEntityState?> GetEntityStateAsync(string entityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var token = Environment.GetEnvironmentVariable(SupervisorTokenEnv);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, HaStatesUrlPrefix + Uri.EscapeDataString(entityId));
        request.Headers.Add(SupervisorTokenHeader, token);
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

            var state = doc.RootElement.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
            string? unit = null;
            if (doc.RootElement.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
            {
                if (attributes.TryGetProperty("unit_of_measurement", out var unitProp))
                {
                    unit = unitProp.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(state))
            {
                return null;
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
