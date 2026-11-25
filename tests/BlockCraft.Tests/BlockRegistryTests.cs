using BlockCraft.Core.Blocks;
using Xunit;

namespace BlockCraft.Tests;

public sealed class BlockRegistryTests
{
    private const string ValidJson = """
    [
      {"id":0,"name":"air","solid":false,"transparent":true},
      {"id":1,"name":"stone","solid":true,"transparent":false}
    ]
    """;

    [Fact]
    public void LoadsDefinitionsAndLooksUpIdsWithoutCaseSensitivity()
    {
        var registry = BlockRegistry.LoadFromJson(ValidJson);
        Assert.Equal(2, registry.Count);
        Assert.Equal((ushort)1, registry.GetId("STONE"));
        Assert.Equal("stone", registry[1].Name);
        Assert.False(registry[1].Transparent);
    }

    [Fact]
    public void RejectsDuplicateIds()
    {
        const string json = """
        [
          {"id":0,"name":"air","solid":false},
          {"id":0,"name":"stone","solid":true}
        ]
        """;
        Assert.Throws<InvalidDataException>(() => BlockRegistry.LoadFromJson(json));
    }

    [Fact]
    public void RequiresAirAtZero()
    {
        const string json = """[{"id":1,"name":"stone"}]""";
        Assert.Throws<InvalidDataException>(() => BlockRegistry.LoadFromJson(json));
    }
}
