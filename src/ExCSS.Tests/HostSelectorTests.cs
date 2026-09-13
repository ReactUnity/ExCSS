using System.Linq;
using Xunit;

namespace ExCSS.Tests;

public class HostSelectorTests : CssConstructionFunctions
{
    private static ISelector SelectorOf(string selector)
    {
        var sheet = ParseStyleSheet(selector + "{color:red}");
        var rule = Assert.IsType<StyleRule>(sheet.StyleRules.FirstOrDefault());
        return rule.Selector;
    }

    [Theory]
    [InlineData(":host")]
    [InlineData(":host(.dark)")]
    [InlineData(":host([data-theme=\"dark\"])")]
    [InlineData(":host(.a.b)")]
    [InlineData(":host-context(.dark)")]
    public void HostSelectorIsParsed(string selector)
    {
        var parsed = SelectorOf(selector);
        Assert.IsType<PseudoClassSelector>(parsed);
        Assert.Equal(selector, parsed.Text);
        Assert.Equal(new Priority(0, 0, 1, 0), parsed.Specificity);
    }

    // Tailwind writes its theme block this way, and one unreadable branch used to cost the whole list.
    [Fact]
    public void HostIsReadAlongsideRootInOneList()
    {
        var parsed = Assert.IsType<ListSelector>(SelectorOf(":root,:host"));
        Assert.Equal(2, parsed.Length);
        Assert.Equal(":root,:host", parsed.Text);
    }

    [Fact]
    public void HostWithAnUnreadableArgumentIsNotASelector()
    {
        var sheet = ParseStyleSheet(":host(.){color:red}");
        Assert.Empty(sheet.StyleRules);
    }
}
