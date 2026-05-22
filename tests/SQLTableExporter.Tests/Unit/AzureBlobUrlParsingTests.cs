using Xunit;

namespace SQLTableExporter.Tests.Unit;

public class AzureBlobUrlParsingTests
{
    [Fact]
    public void IsSasUrl_returns_false_for_plain_url()
    {
        Assert.False(AzureBlobStorageUploader.IsSasUrl(
            "https://acct.blob.core.windows.net/container"));
    }

    [Fact]
    public void IsSasUrl_returns_true_when_query_has_sv_signature_version()
    {
        Assert.True(AzureBlobStorageUploader.IsSasUrl(
            "https://acct.blob.core.windows.net/container?sv=2021-08-06&sig=abc"));
    }

    [Fact]
    public void IsSasUrl_returns_false_when_query_lacks_sv_token()
    {
        // A query string by itself doesn't make a SAS URL — the SDK signals
        // SAS via the sv (signature version) parameter.
        Assert.False(AzureBlobStorageUploader.IsSasUrl(
            "https://acct.blob.core.windows.net/container?foo=bar"));
    }

    [Fact]
    public void ParseUrl_extracts_account_and_container_with_no_folder()
    {
        AzureBlobStorageUploader.ParseUrl(
            "https://myacct.blob.core.windows.net/mycontainer",
            out var account, out var container, out var folder);

        Assert.Equal("myacct", account);
        Assert.Equal("mycontainer", container);
        Assert.Empty(folder);
    }

    [Fact]
    public void ParseUrl_extracts_folder_path_and_appends_trailing_slash()
    {
        AzureBlobStorageUploader.ParseUrl(
            "https://myacct.blob.core.windows.net/mycontainer/exports/2024",
            out var account, out var container, out var folder);

        Assert.Equal("myacct", account);
        Assert.Equal("mycontainer", container);
        Assert.Equal("exports/2024/", folder);
    }

    [Fact]
    public void ParseUrl_strips_query_string_before_matching()
    {
        AzureBlobStorageUploader.ParseUrl(
            "https://myacct.blob.core.windows.net/mycontainer/folder?sv=2021&sig=abc",
            out var account, out var container, out var folder);

        Assert.Equal("myacct", account);
        Assert.Equal("mycontainer", container);
        Assert.Equal("folder/", folder);
    }

    [Fact]
    public void ParseUrl_throws_on_malformed_url()
    {
        Assert.Throws<ArgumentException>(() =>
            AzureBlobStorageUploader.ParseUrl(
                "ftp://nope.example.com/whatever",
                out _, out _, out _));
    }

    [Fact]
    public void Constructor_throws_for_null_or_empty_url()
    {
        Assert.Throws<ArgumentException>(() => new AzureBlobStorageUploader(""));
        Assert.Throws<ArgumentException>(() => new AzureBlobStorageUploader("   "));
    }
}
