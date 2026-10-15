using Hero.Core.Providers;

namespace Hero.Core.Tests;

public sealed class ModelCatalogParseTests
{
    [Fact]
    public void ParseModelsJson_ReadsDeepSeekShape()
    {
        var json =
            """
            {
              "object": "list",
              "data": [
                { "id": "deepseek-chat", "object": "model", "owned_by": "deepseek", "name": "DeepSeek Chat" },
                { "id": "deepseek-reasoner", "object": "model", "owned_by": "deepseek" }
              ]
            }
            """;

        var models = OpenAiCompatibleModelCatalog.ParseModelsJson(json);
        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Id == "deepseek-chat" && m.Name == "DeepSeek Chat");
        Assert.Contains(models, m => m.Id == "deepseek-reasoner");
    }

    [Fact]
    public void ProviderCatalog_FindsMoonshotAliases()
    {
        Assert.Equal("moonshot", LlmProviderCatalog.Find("kimi")!.Id);
        Assert.Equal("moonshot", LlmProviderCatalog.Find("Moonshot")!.Id);
        Assert.Equal("deepseek", LlmProviderCatalog.Find("DeepSeek")!.Id);
    }
}
