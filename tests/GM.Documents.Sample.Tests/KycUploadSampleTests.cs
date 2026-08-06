using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace GM.Documents.Sample.Tests;

// Spins up the real sample app in-memory and drives the KYC upload endpoint end to end — proving the
// full "upload → normalize → ready-for-storage" path strips location data and caps size.
public class KycUploadSampleTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Upload_StripsGps_ResizesToCap_AndConvertsToJpeg()
    {
        var client = factory.CreateClient();
        var photo = MakeJpegWithGps(4032, 3024); // a typical phone photo, 4:3, with location EXIF

        using var response = await client.PostAsync("/kyc/documents", MultipartOf(photo, "passport.jpg", "image/jpeg"));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // The upload really did carry GPS...
        Assert.True(root.GetProperty("original").GetProperty("hadGps").GetBoolean());

        // ...and the normalized output is a capped, metadata-stripped JPEG.
        var normalized = root.GetProperty("normalized");
        Assert.Equal("image/jpeg", normalized.GetProperty("contentType").GetString());
        Assert.Equal("passport.jpg", normalized.GetProperty("fileName").GetString());
        Assert.True(normalized.GetProperty("metadataStripped").GetBoolean());
        Assert.True(normalized.GetProperty("width").GetInt32() <= 2000);
        Assert.True(normalized.GetProperty("height").GetInt32() <= 2000);
        Assert.Equal(2000, normalized.GetProperty("width").GetInt32()); // long edge hits the cap
    }

    [Fact]
    public async Task Download_ReturnsJpeg_WithNoGpsMetadata()
    {
        var client = factory.CreateClient();
        var photo = MakeJpegWithGps(1000, 800);

        using var response = await client.PostAsync("/kyc/documents/download", MultipartOf(photo, "id.jpg", "image/jpeg"));
        response.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        var info = Image.Identify(ms);

        // The stored image carries no EXIF at all — so no GPS could possibly leak.
        Assert.Null(info.Metadata.ExifProfile);
    }

    private static MultipartFormDataContent MultipartOf(byte[] bytes, string fileName, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static byte[] MakeJpegWithGps(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx => ctx.BackgroundColor(Color.DarkSlateGray));

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(51), new Rational(30), new Rational(0)]);
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(0), new Rational(7), new Rational(0)]);
        exif.SetValue(ExifTag.Make, "TestCam");
        image.Metadata.ExifProfile = exif;

        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 95 });
        return ms.ToArray();
    }
}
