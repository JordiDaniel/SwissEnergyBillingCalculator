using System.Net.Http.Headers;
using System.Text.Json;

namespace ZevEbixWeb.Services;

public sealed class HomeAssistantEntityService(
    IHttpClientFactory httpClientFactory,
    ILogger<HomeAssistantEntityService> logger)
{
    private const string HomeAssistantUrlEnv = "HA_URL";
    private const string HomeAssistantTokenEnv = "HA_TOKEN";

    public async Task<HomeAssistantEntityState?> GetEntityStateAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            logger.LogWarning("Home Assistant entity id is empty.");
            return null;
        }

        var token = Environment.GetEnvironmentVariable(HomeAssistantTokenEnv);

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogError(
                "Environment variable {Variable} is missing.",
                HomeAssistantTokenEnv);

            return null;
        }

        var baseUrl =
            Environment.GetEnvironmentVariable(HomeAssistantUrlEnv)
            ?? "http://192.168.178.60:8123";

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
}

public sealed record HomeAssistantEntityState(
    string State,
    string? UnitOfMeasurement);