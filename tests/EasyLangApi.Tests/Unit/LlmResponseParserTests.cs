using EasyLangApi.Explaining;

namespace EasyLangApi.Tests.Unit;

public class LlmResponseParserTests
{
    private const string Answer = """
        {"translation":"выкладывай","meaning":"Просьба рассказать секрет.","partOfSpeech":"idiom",
         "contextNote":"Разговорное, дружеский тон.","example":"Come on, spill the beans!"}
        """;

    [Fact]
    public void Parses_plain_json()
    {
        var result = LlmResponseParser.Parse("spill the beans", Answer);

        Assert.True(result.Found);
        Assert.Equal("llm", result.Source);
        Assert.Equal("выкладывай", result.Translation);
        Assert.Equal("Просьба рассказать секрет.", result.Summary);
        Assert.Equal("Разговорное, дружеский тон.", result.ContextNote);
        var sense = Assert.Single(result.Senses);
        Assert.Equal("idiom", sense.PartOfSpeech);
        Assert.Equal("Come on, spill the beans!", sense.Example);
    }

    [Fact]
    public void Parses_json_wrapped_in_markdown_and_chatter()
    {
        var content = $"Sure! Here is the explanation:\n```json\n{Answer}\n```\nHope it helps.";

        var result = LlmResponseParser.Parse("spill the beans", content);

        Assert.Equal("выкладывай", result.Translation);
    }

    [Fact]
    public void Keeps_free_text_when_json_is_missing()
    {
        var result = LlmResponseParser.Parse("beat", "It means very tired.");

        Assert.True(result.Found);
        Assert.Equal("It means very tired.", result.Summary);
        Assert.Null(result.Translation);
        Assert.Empty(result.Senses);
    }

    [Fact]
    public void Keeps_free_text_when_json_is_broken()
    {
        var result = LlmResponseParser.Parse("beat", "{ meaning: tired, oops }");

        Assert.Equal("{ meaning: tired, oops }", result.Summary);
    }

    [Fact]
    public void Translation_alone_is_enough()
    {
        var result = LlmResponseParser.Parse("beat", """{"translation":"вымотан","contextNote":""}""");

        Assert.Equal("вымотан", result.Summary);
        Assert.Null(result.ContextNote);
    }
}
