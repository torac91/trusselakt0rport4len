using System.Net;
using System.Net.Http.Json;
using Saksbehandling.Api.Tests.TestInfrastructure;

namespace Saksbehandling.Api.Tests.SecurityRegression;

// See SecurityRegression/README.md for what these tests are and how to use
// them once you've fixed the underlying issue.
[Collection("Sequential")]
[Trait("Category", "SecurityRegressionTrainingFixture")]
public class FileUploadRegressionTests
{
    private readonly CustomWebApplicationFactory _factory;

    public FileUploadRegressionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// V18 - upload validation is a short extension blocklist, not an
    /// allowlist. A .html upload (not on the blocklist) is accepted.
    /// </summary>
    [Fact]
    public async Task V18_Upload_AcceptsHtmlExtension()
    {
        var client = await _factory.LoginAsAsync("saksbehandler1");
        var caseId = await client.CreateOwnCaseAsync("Sak for opplastingstest");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
        fileContent.Headers.ContentType = new("text/html");
        form.Add(fileContent, "file", "rapport.html");

        var response = await client.PostAsync($"/api/v2/cases/{caseId}/documents", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// V20 - the upload filename is used directly in the storage path with
    /// no traversal sanitization. A filename containing "../" segments
    /// escapes the intended per-case folder entirely.
    /// </summary>
    [Fact]
    public async Task V20_Upload_PathTraversalEscapesCaseFolder()
    {
        var client = await _factory.LoginAsAsync("saksbehandler1");
        var caseId = await client.CreateOwnCaseAsync("Sak for sti-traversering-test");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("traversed content"));
        fileContent.Headers.ContentType = new("text/plain");
        form.Add(fileContent, "file", "../../traversed-regression-marker.txt");

        var response = await client.PostAsync($"/api/v2/cases/{caseId}/documents", form);
        response.EnsureSuccessStatusCode();

        var escapedPath = Path.Combine(CustomWebApplicationFactory.TestStorageRoot, "traversed-regression-marker.txt");
        Assert.True(File.Exists(escapedPath), $"Expected the upload to escape to {escapedPath}, but it wasn't there.");
    }
}
