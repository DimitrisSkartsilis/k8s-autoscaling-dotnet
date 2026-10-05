using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

// IClassFixture: xUnit starts the app once and shares it across the tests in this class.
public class DocumentsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DocumentsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();   // HttpClient wired directly to the in-memory app
    }

    [Fact]
    public async Task Post_then_get_returns_the_document()
    {
        var response = await _client.PostAsJsonAsync("/documents", new { title = "First", content = "Hello" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<DocumentDto>();
        var fetched = await _client.GetFromJsonAsync<DocumentDto>($"/documents/{created!.Id}");

        Assert.Equal("First", fetched!.Title);
        Assert.Equal("Hello", fetched.Content);
    }

    [Fact]
    public async Task Post_with_empty_title_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/documents", new { title = "", content = "Hello" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_id_returns_404()
    {
        var response = await _client.GetAsync($"/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record DocumentDto(Guid Id, string Title, string Content);
}