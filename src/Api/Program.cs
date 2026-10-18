using System.Collections.Concurrent;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// In-memory store for now; replaced by a real database later.
var documents = new ConcurrentDictionary<Guid, Document>();

app.MapPost("/documents", (CreateDocumentRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
        return Results.BadRequest("Title and content are required.");

    var document = new Document(Guid.NewGuid(), request.Title, request.Content);
    documents[document.Id] = document;
    return Results.Created($"/documents/{document.Id}", document);
});

app.MapGet("/documents/{id:guid}", (Guid id) =>
    documents.TryGetValue(id, out var document) ? Results.Ok(document) : Results.NotFound());

// Deliberately CPU-heavy endpoint, used to generate load for the autoscaling demo.
app.MapGet("/work", (int? iterations) =>
{
    var count = Math.Clamp(iterations ?? 100_000, 1, 1_000_000);   // default 100k, capped so one request can't hang a pod
    var hash = "flagship"u8.ToArray();
    for (var i = 0; i < count; i++)
        hash = SHA256.HashData(hash);                               // hash the previous result, over and over
    return Results.Ok(new { iterations = count, result = Convert.ToHexString(hash) });
});

app.Run();

record CreateDocumentRequest(string Title, string Content);
record Document(Guid Id, string Title, string Content);