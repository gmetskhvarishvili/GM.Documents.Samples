# GM.Documents.Samples

Runnable usage for [GM.Documents](https://github.com/gmetskhvarishvili/GM.Documents) — the document
processing library for the `GM.*` ecosystem.

The sample covers the whole library:

- **GM.Documents.Images** — the KYC document/liveness image normalization that sits between a raw
  upload and persistence (the focus below).
- **GM.Documents.Pdf** — `GET /kyc/report.pdf` generates a KYC verification report (QuestPDF).
- **GM.Documents.Excel** — `GET /reports/transactions.xlsx` generates a tabular export (ClosedXML).

The PDF report is built from the shared, format-neutral `DocumentContent` model — the same content
could be handed to `GM.Documents.Word` to emit a `.docx` instead.

## Run it

```bash
dotnet run --project GM.Documents.Sample.API
```

Then upload a photo (ideally one straight off a phone, so it carries GPS EXIF):

```bash
curl -F "file=@passport.jpg" http://localhost:5xxx/kyc/documents
```

You get back a before/after report showing the original had GPS, and the normalized JPEG is capped
to ≤ 2000 px, compressed to ≤ 2 MB, and has **all EXIF/GPS stripped**:

```jsonc
{
  "original":   { "fileName": "passport.jpg", "sizeBytes": 3894221, "width": 4032, "height": 3024,
                  "hadExif": true, "hadGps": true },
  "normalized": { "fileName": "passport.jpg", "contentType": "image/jpeg", "sizeBytes": 421887,
                  "width": 2000, "height": 1500, "metadataStripped": true },
  "note": "Normalized stream is ready to hand to GM.FileStorage; any GPS/EXIF has been removed."
}
```

`POST /kyc/documents/download` runs the same pipeline and streams the normalized JPEG back so you can
inspect it.

## What it shows

- `AddGMDocuments().AddImages(...)` DI wiring with a KYC-tuned profile (`Program.cs`).
- The **upload → normalize → persist** boundary: the `DocumentResult` stream is drop-in compatible
  with `GM.FileStorage.FileUploadRequest(content, fileName, contentType)`. The hand-off is shown in a
  comment — this sample doesn't reference GM.FileStorage, demonstrating the packages aren't coupled.
- `InspectAsync` reading upload metadata (dimensions, GPS presence) **before** committing to a decode.

## Tests

```bash
dotnet test
```

`WebApplicationFactory` spins the real app up in-memory and drives the endpoint end to end: it builds
a JPEG carrying real GPS coordinates, posts it, and asserts the response is a capped JPEG whose bytes
contain **no EXIF profile** — proving location data can't leak through to storage.

> The sample references the sibling `GM.Documents` source repo by project path so it builds against
> current code. Once the packages are published, swap the `ProjectReference`s in
> `GM.Documents.Sample.API.csproj` for `PackageReference`s.
