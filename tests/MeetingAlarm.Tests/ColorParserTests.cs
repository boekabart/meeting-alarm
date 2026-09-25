public class ColorParserTests
{
    [Theory]
    [InlineData("#c62828", 0xc6, 0x28, 0x28)]
    [InlineData("#0f0", 0x00, 0xff, 0x00)]
    [InlineData("teal", 0x00, 0x80, 0x80)]
    [InlineData("RebeccaPurple", 0x66, 0x33, 0x99)]
    [InlineData("slategrey", 0x70, 0x80, 0x90)]
    [InlineData(" firebrick ", 0xb2, 0x22, 0x22)]
    public void Hex_and_css_names(string input, int r, int g, int b)
    {
        var c = ColorParser.Parse(input);

        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData("notacolor")]
    [InlineData("#12")]
    [InlineData("")]
    [InlineData(null)]
    public void Garbage_becomes_the_default_color(string? input) => Assert.Equal(ColorParser.Default, ColorParser.Parse(input));
}
