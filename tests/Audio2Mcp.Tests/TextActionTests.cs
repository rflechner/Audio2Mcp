using Audio2Mcp.Chat;
using Xunit;

namespace Audio2Mcp.Tests;

public sealed class TextActionTests
{
    [Fact]
    public void QueryCannotInjectUrlParameters()
    {
        var action = new TextAction("search", "Search", "https://example.com/?q={query}", TextActionKind.OpenBrowser);
        Assert.Equal("https://example.com/?q=caf%C3%A9%20%26x%3D1%23fragment", action.BuildUri("café &x=1#fragment").AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///tmp/{query}")]
    [InlineData("https://{query}.example.com/")]
    [InlineData("https://{query}/")]
    [InlineData("https://example.com/")]
    [InlineData("https://user:secret@example.com/?q={query}")]
    public void RejectsInvalidDestinations(string template)
    {
        var action = new TextAction("search", "Search", template, TextActionKind.HttpGet);
        Assert.Throws<InvalidOperationException>(action.Validate);
    }

    [Fact]
    public void StoreRoundTripsAndPreservesPreviousCatalogOnInvalidSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "actions.json");
        var store = new TextActionStore(path);
        var action = new TextAction("search", "Search", "https://example.com/?q={query}", TextActionKind.HttpGet, false);
        try
        {
            Assert.Empty(store.Load());
            store.Save([action]);
            Assert.Equal(action, Assert.Single(store.Load()));
            Assert.Throws<InvalidOperationException>(() => store.Save([action, action]));
            Assert.Equal(action, Assert.Single(store.Load()));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
