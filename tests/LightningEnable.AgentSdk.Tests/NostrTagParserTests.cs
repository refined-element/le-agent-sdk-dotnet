using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

public class NostrTagParserTests
{
    private readonly string[][] _sampleTags = new[]
    {
        new[] { "e", "event-id-1" },
        new[] { "e", "event-id-2" },
        new[] { "p", "pubkey-1" },
        new[] { "d", "dtag-value" },
        new[] { "t", "category1" },
        new[] { "t", "category2" },
        new[] { "param", "key1", "value1" },
        new[] { "param", "key2", "value2" },
        new[] { "price", "100" }
    };

    [Fact]
    public void GetTagValue_ReturnsFirstMatch()
    {
        var value = NostrTagParser.GetTagValue(_sampleTags, "e");
        Assert.Equal("event-id-1", value);
    }

    [Fact]
    public void GetTagValue_ReturnsNullWhenNotFound()
    {
        var value = NostrTagParser.GetTagValue(_sampleTags, "nonexistent");
        Assert.Null(value);
    }

    [Fact]
    public void GetTagValues_ReturnsAllMatches()
    {
        var values = NostrTagParser.GetTagValues(_sampleTags, "e");
        Assert.Equal(2, values.Count);
        Assert.Contains("event-id-1", values);
        Assert.Contains("event-id-2", values);
    }

    [Fact]
    public void HasTag_ReturnsTrueWhenExists()
    {
        Assert.True(NostrTagParser.HasTag(_sampleTags, "p"));
    }

    [Fact]
    public void HasTag_ReturnsFalseWhenMissing()
    {
        Assert.False(NostrTagParser.HasTag(_sampleTags, "missing"));
    }

    [Fact]
    public void GetParams_ParsesKeyValuePairs()
    {
        var paramDict = NostrTagParser.GetParams(_sampleTags);

        Assert.Equal(2, paramDict.Count);
        Assert.Equal("value1", paramDict["key1"]);
        Assert.Equal("value2", paramDict["key2"]);
    }

    [Fact]
    public void GetTags_ReturnsFullTagArrays()
    {
        var tags = NostrTagParser.GetTags(_sampleTags, "t");
        Assert.Equal(2, tags.Count);
        Assert.Equal("category1", tags[0][1]);
        Assert.Equal("category2", tags[1][1]);
    }

    [Fact]
    public void BuildFilter_ConstructsCorrectFilter()
    {
        var filter = NostrTagParser.BuildFilter(
            kinds: new[] { 38401 },
            authors: new[] { "author1" },
            tagFilters: new Dictionary<string, string[]>
            {
                ["t"] = new[] { "ai" }
            },
            limit: 10,
            since: 1700000000
        );

        Assert.Equal(new[] { 38401 }, filter["kinds"]);
        Assert.Equal(new[] { "author1" }, filter["authors"]);
        Assert.Equal(new[] { "ai" }, filter["#t"]);
        Assert.Equal(10, filter["limit"]);
        Assert.Equal(1700000000L, filter["since"]);
    }
}
