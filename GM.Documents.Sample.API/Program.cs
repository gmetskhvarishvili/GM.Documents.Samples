using GM.Documents;
using GM.Documents.Images;

var builder = WebApplication.CreateBuilder(args);

// GM.Documents core + the Images type package. The Images defaults below are the "normalize a KYC
// document photo for storage" profile: cap the dimensions, compress to a sane size, and — most
// importantly — strip EXIF/GPS so a phone photo's location never reaches the file store.
builder.Services
    .AddGMDocuments(o => o.MaxInputSizeInBytes = 15 * 1024 * 1024) // reject > 15 MB uploads up front
    .AddImages(o =>
    {
        o.MaxWidth = 2000;
        o.MaxHeight = 2000;
        o.ResizeMode = ResizeMode.Fit;
        o.TargetFormat = DocumentFormat.Jpeg;
        o.Quality = 80;
        o.StripMetadata = true;                       // EXIF + GPS removed (default; explicit here for the demo)
        o.MaxOutputSizeInBytes = 2 * 1024 * 1024;     // step quality down to keep stored photos ≤ 2 MB
    });

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "GM.Documents sample — KYC document/liveness image normalization",
    try_it = new[]
    {
        "POST /kyc/documents          multipart/form-data, field 'file' → JSON report (before/after, GPS stripped)",
        "POST /kyc/documents/download multipart/form-data, field 'file' → the normalized JPEG bytes",
    },
    pipeline = "orient → resize (≤2000px) → flatten → strip EXIF/GPS → JPEG q80 (≤2 MB)",
}));

// ---- The integration point ---------------------------------------------------------------------
// Raw upload → GM.Documents normalizes → (in production) GM.FileStorage persists. This endpoint
// reports what happened; the commented lines show the hand-off, which compiles without this project
// referencing GM.FileStorage — DocumentResult is stream-compatible with FileUploadRequest.
app.MapPost("/kyc/documents", async (IFormFile file, IImageProcessor images, CancellationToken ct) =>
{
    if (file.Length == 0)
        return Results.BadRequest(new { error = "Empty upload." });

    // 1. Peek at the raw upload (header only, no full decode) to show what we're about to strip.
    ImageInfo before;
    await using (var peek = file.OpenReadStream())
        before = await images.InspectAsync(DocumentSource.From(peek, file.FileName, file.ContentType), ct);

    // 2. Normalize using the DI-configured KYC defaults.
    await using var upload = file.OpenReadStream();
    var source = DocumentSource.From(upload, file.FileName, file.ContentType);
    await using var result = await images.NormalizeAsync(source, ct);

    // 3. Persist. In production:
    //    var stored = await storage.UploadAsync(
    //        new FileUploadRequest(result.Content, result.FileName, result.ContentType), ct);
    //    return Results.Ok(new { key = stored.Key });

    return Results.Ok(new
    {
        original = new
        {
            file.FileName,
            file.ContentType,
            sizeBytes = file.Length,
            before.Width,
            before.Height,
            hadExif = before.HasExifMetadata,
            hadGps = before.HasGpsMetadata,
        },
        normalized = new
        {
            result.FileName,
            result.ContentType,
            sizeBytes = result.SizeInBytes,
            width = result.Metadata.GetInt("width"),
            height = result.Metadata.GetInt("height"),
            metadataStripped = result.Metadata.Properties["metadataStripped"] == "true",
        },
        note = "Normalized stream is ready to hand to GM.FileStorage; any GPS/EXIF has been removed.",
    });
}).DisableAntiforgery();

// Same normalization, but streams the resulting JPEG back so you can eyeball it.
app.MapPost("/kyc/documents/download", async (IFormFile file, IImageProcessor images, CancellationToken ct) =>
{
    if (file.Length == 0)
        return Results.BadRequest(new { error = "Empty upload." });

    await using var upload = file.OpenReadStream();
    var source = DocumentSource.From(upload, file.FileName, file.ContentType);
    using var result = await images.NormalizeAsync(source, ct);

    // Copy out before the result is disposed, so Results.File owns an independent buffer.
    var bytes = await result.ToByteArrayAsync(ct);
    return Results.File(bytes, result.ContentType, result.FileName);
}).DisableAntiforgery();

app.Run();

// Exposed so the test project can spin the app up with WebApplicationFactory.
public partial class Program;
