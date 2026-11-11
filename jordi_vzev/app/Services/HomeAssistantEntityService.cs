using System.Net.Http.Headers;
using System.Text.Json;

namespace ZevEbixWeb.Services;

public sealed class HomeAssistantEntityService(
    IHttpClientFactory httpClientFactory,
    ILogger<HomeAssistantEntityService> logger)
{
    private const string HomeAssistantUrlEnv = "HA_URL";
    private const string HomeAssistantTokenEnv = "HA_TOKEN";
    private const string OptionsPath = "/data/options.json";

    public async Task<HomeAssistantEntityState?> GetEntityStateAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            logger.LogWarning("Home Assistant entity id is empty.");
            return null;
        }

        var (baseUrl, token) = LoadHomeAssistantOptions();

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogError("ha_token is missing in /data/options.json (or HA_TOKEN env fallback).");
            return null;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogError("ha_url is missing in /data/options.json (or HA_URL env fallback).");
            return null;
        }

        logger.LogInformation(
            "Reading Home Assistant entity {EntityId} from {BaseUrl}",
            entityId,
            baseUrl);

        var requestUrl =
            $"{baseUrl.TrimEnd('/')}/api/states/{Uri.EscapeDataString(entityId)}";

        var client = httpClientFactory.CreateClient();

        using var request =
            new HttpRequestMessage(HttpMethod.Get, requestUrl);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response =
                await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody =
                    await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogError(
                    "Home Assistant API returned {StatusCode} for {EntityId}. Response: {Response}",
                    (int)response.StatusCode,
                    entityId,
                    responseBody);

                return null;
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            using var doc =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

            var state =
                doc.RootElement.TryGetProperty("state", out var stateProp)
                    ? stateProp.GetString()
                    : null;

            logger.LogInformation(
                "Home Assistant entity {EntityId} returned state '{State}'",
                entityId,
                state);

            if (string.IsNullOrWhiteSpace(state) ||
                string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "unavailable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "none", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Entity {EntityId} has invalid state '{State}'",
                    entityId,
                    state);

                return null;
            }

            string? unit = null;

            if (doc.RootElement.TryGetProperty("attributes", out var attributes) &&
                attributes.ValueKind == JsonValueKind.Object &&
                attributes.TryGetProperty("unit_of_measurement", out var unitProp))
            {
                unit = unitProp.GetString();
            }

            logger.LogInformation("Successfully read entity {EntityId}: {State} {Unit}", entityId, state, unit);

            return new HomeAssistantEntityState(state, unit);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Unable to reach Home Assistant API at {BaseUrl}", baseUrl);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout while reading entity {EntityId}", entityId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while reading entity {EntityId}",entityId);
            return null;
        }
    }

    private (string BaseUrl, string Token) LoadHomeAssistantOptions()
    {
        var envUrl = Environment.GetEnvironmentVariable(HomeAssistantUrlEnv)?.Trim() ?? string.Empty;
        var envToken = Environment.GetEnvironmentVariable(HomeAssistantTokenEnv)?.Trim() ?? string.Empty;

        if (!File.Exists(OptionsPath))
        {
            return (envUrl, envToken);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(OptionsPath));

            var fileUrl = doc.RootElement.TryGetProperty("ha_url", out var haUrlProp)
                ? (haUrlProp.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var fileToken = doc.RootElement.TryGetProperty("ha_token", out var haTokenProp)
                ? (haTokenProp.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var resolvedUrl = string.IsNullOrWhiteSpace(fileUrl) ? envUrl : fileUrl;
            var resolvedToken = string.IsNullOrWhiteSpace(fileToken) ? envToken : fileToken;

            return (resolvedUrl, resolvedToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse /data/options.json for ha_url/ha_token.");
            return (envUrl, envToken);
        }
    }
}

public sealed record HomeAssistantEntityState(
    string State,
    string? UnitOfMeasurement);
