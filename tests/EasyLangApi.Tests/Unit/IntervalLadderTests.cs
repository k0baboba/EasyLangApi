using EasyLangApi.Review;

namespace EasyLangApi.Tests.Unit;

public class IntervalLadderTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(3, 7)]
    [InlineData(5, 7)]
    [InlineData(120, 240)]
    public void Correct_answer_moves_one_step_up(double current, double expected) =>
        Assert.Equal(expected, IntervalLadder.Next(current, wasCorrect: true));

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Wrong_answer_starts_over(double current) =>
        Assert.Equal(1, IntervalLadder.Next(current, wasCorrect: false));
}
