using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GM.Documents.Sample.Tests;

// Drives the document-generation endpoints end to end through the real app.
public class GenerationSampleTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReportPdf_ReturnsAValidPdf()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/kyc/report.pdf");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task TransactionsXlsx_ReturnsAValidWorkbook()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/reports/transactions.xlsx");
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(0x50, bytes[0]); // ZIP "PK" signature
        Assert.Equal(0x4B, bytes[1]);
    }
}
