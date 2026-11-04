using EasyLangApi.Explaining;

namespace EasyLangApi.Tests.Unit;

public class PromptBuilderTests
{
    [Fact]
    public void Full_context_is_put_into_prompt_in_order()
    {
        var context = new PhraseContext(
            Title: "Night Shift S01E01",
            Speaker: "LEO",
            Before: ["Rain on the window.", "You look tired."],
            CurrentLine: "LEO: I'm beat.",
            After: ["Coffee's on the house.", "Thanks."]);

        var prompt = PromptBuilder.BuildUserPrompt("beat", context);

        Assert.Equal("""
            Phrase: "beat"
            Work: Night Shift S01E01
            Speaker: LEO
            Previous lines:
            - Rain on the window.
            - You look tired.
            Line with the phrase: LEO: I'm beat.
            Next lines:
            - Coffee's on the house.
            - Thanks.
            """.ReplaceLineEndings(), prompt.ReplaceLineEndings());
    }

    [Fact]
    public void Missing_parts_are_skipped_without_empty_headers()
    {
        var context = new PhraseContext(Before: ["", "  "], CurrentLine: "Hit the road.");

        var prompt = PromptBuilder.BuildUserPrompt("hit the road", context);

        Assert.DoesNotContain("Work:", prompt);
        Assert.DoesNotContain("Speaker:", prompt);
        Assert.DoesNotContain("Previous lines:", prompt);
        Assert.DoesNotContain("Next lines:", prompt);
        Assert.Contains("Line with the phrase: Hit the road.", prompt);
        Assert.DoesNotContain("No context", prompt);
    }

    [Fact]
    public void Without_any_context_asks_for_common_meaning()
    {
        var prompt = PromptBuilder.BuildUserPrompt("no cap", PhraseContext.Empty);

        Assert.StartsWith("Phrase: \"no cap\"", prompt);
        Assert.Contains("No context is available", prompt);
    }

    [Fact]
    public void System_prompt_asks_for_json_in_requested_language()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("Russian");

        Assert.Contains("Russian-speaking learner", prompt);
        Assert.Contains("\"translation\"", prompt);
        Assert.Contains("JSON", prompt);
    }
}
