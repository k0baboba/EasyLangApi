using EasyLangApi.Review;

namespace EasyLangApi.Tests.Unit;

public class SingleUnknownSelectorTests
{
    private static readonly CandidateLine[] Lines =
    [
        new(1, "Come on, spill the beans and break a leg."),
        new(2, "Just spill the beans already."),
        new(3, "Break a leg tonight."),
        new(4, "Spill the BEANS, no cap."),
    ];

    [Fact]
    public void Keeps_only_lines_where_target_is_the_only_unknown()
    {
        var selected = SingleUnknownSelector.Select("spill the beans", Lines, ["break a leg", "no cap"]);

        Assert.Equal([2], selected.Select(l => l.LineId));
    }

    [Fact]
    public void Analysis_tells_what_else_is_unknown()
    {
        var analyzed = SingleUnknownSelector.Analyze("spill the beans", Lines, ["break a leg", "no cap"]);

        Assert.Equal([1, 2, 4], analyzed.Select(l => l.LineId));
        Assert.Equal(["break a leg"], analyzed[0].OtherUnknown);
        Assert.True(analyzed[1].IsSingleUnknown);
        Assert.Equal(["no cap"], analyzed[2].OtherUnknown);
    }

    [Fact]
    public void Without_other_unknown_items_every_line_with_target_fits()
    {
        var selected = SingleUnknownSelector.Select("break a leg", Lines, []);

        Assert.Equal([1, 3], selected.Select(l => l.LineId));
    }

    [Fact]
    public void Partial_word_match_is_not_a_match()
    {
        var selected = SingleUnknownSelector.Select("bean", Lines, []);

        Assert.Empty(selected);
    }
}
