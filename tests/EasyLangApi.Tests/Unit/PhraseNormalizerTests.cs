using EasyLangApi.Episodes;
using EasyLangApi.Vocabulary;

namespace EasyLangApi.Tests.Unit;

public class PhraseNormalizerTests
{
    [Theory]
    [InlineData("Beat", "beat")]
    [InlineData("  Spill   the BEANS!! ", "spill the beans")]
    [InlineData("Don’t", "don't")]
    [InlineData("...", "")]
    public void Normalizes_case_apostrophes_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, PhraseNormalizer.Normalize(input));

    [Theory]
    [InlineData("So, are you gonna spill the beans or what?", "spill the beans", true)]
    [InlineData("So, are you gonna spill the beans or what?", "spill beans", false)]
    [InlineData("I'm beat.", "beat", true)]
    [InlineData("I'm beaten.", "beat", false)]
    public void Finds_phrase_as_whole_words(string text, string phrase, bool expected) =>
        Assert.Equal(expected, PhraseNormalizer.ContainsPhrase(text, phrase));

    [Theory]
    [InlineData("MAYA: Coffee's on the house.", "MAYA")]
    [InlineData("- DR. STONE: Hold still.", "DR. STONE")]
    [InlineData("Coffee's on the house: really.", null)]
    [InlineData("Note: lowercase is not a speaker", null)]
    public void Extracts_speaker_prefix(string line, string? expected) =>
        Assert.Equal(expected, SpeakerParser.TryExtract(line));
}
