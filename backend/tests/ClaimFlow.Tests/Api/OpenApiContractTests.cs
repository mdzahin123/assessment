using System.Text.Json;

namespace ClaimFlow.Tests.Api;

/// <summary>
/// The OpenAPI document is the frontend/backend contract. These tests guard its completeness:
/// a Stage 5 check found 11 endpoints with NO documented success response (declaring error responses
/// on a controller stops .NET from inferring the success type), so their data types were missing.
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenApiContractTests(ApiFactory factory)
{
    private async Task<JsonElement> GetContractAsync()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Every_endpoint_documents_a_success_response()
    {
        var doc = await GetContractAsync();
        var missing = new List<string>();

        foreach (var path in doc.GetProperty("paths").EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            var hasSuccess = operation.Value.GetProperty("responses").EnumerateObject().Any(r => r.Name.StartsWith('2'));
            if (!hasSuccess)
                missing.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
        }

        Assert.True(missing.Count == 0, "No success response documented for: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("UserDto")]
    [InlineData("PolicyDto")]
    [InlineData("ClaimSummaryDto")]
    [InlineData("ClaimDetailDto")]
    [InlineData("NotificationDto")]
    public async Task Response_types_used_by_the_frontend_are_described(string schemaName)
    {
        var schemas = (await GetContractAsync()).GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(schemaName, out _), $"{schemaName} is missing from the OpenAPI contract");
    }
}
