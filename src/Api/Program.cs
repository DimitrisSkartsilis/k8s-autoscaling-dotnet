using System.Collections.Concurrent;

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

app.Run();

record CreateDocumentRequest(string Title, string Content);
record Document(Guid Id, string Title, string Content);