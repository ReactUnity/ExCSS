using System.Linq;
using Xunit;

namespace ExCSS.Tests
{
    /// <summary>
    /// <c>@starting-style</c> (<see href="https://www.w3.org/TR/css-transitions-2/#defining-before-change-style">CSS
    /// Transitions 2</see>): a grouping rule at the top level, and a nested group rule inside a style
    /// rule's block, where its declarations belong to an implicit rule with the enclosing selector.
    /// </summary>
    public class StartingStyleTests : CssConstructionFunctions
    {
        [Fact]
        public void TopLevelBlockIsAGroupingRule()
        {
            var sheet = ParseStyleSheet("@starting-style { .a { opacity: 0; } .b { color: red; } }");
            var rule = Assert.IsAssignableFrom<IStartingStyleRule>(Assert.Single(sheet.Rules));
            Assert.Equal(RuleType.StartingStyle, rule.Type);
            Assert.Equal(2, rule.Rules.Length);
            Assert.Equal(".a", ((IStyleRule) rule.Rules[0]).SelectorText);
            Assert.Equal(".b", ((IStyleRule) rule.Rules[1]).SelectorText);
        }

        [Fact]
        public void SerializesBackToStartingStyle()
        {
            var sheet = ParseStyleSheet("@starting-style { .a { opacity: 0; } }");
            Assert.Equal("@starting-style { .a { opacity: 0 } }", sheet.Rules[0].ToCss());
        }

        [Fact]
        public void NestedBlockHoldsAnImplicitRuleWithTheParentSelector()
        {
            var sheet = ParseStyleSheet(".card { color: red; @starting-style { opacity: 0; } }");
            var parent = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            var rule = Assert.IsAssignableFrom<IStartingStyleRule>(Assert.Single(parent.NestedRules));
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(rule.Rules));
            Assert.Equal(":is(.card)", implicitRule.SelectorText);
            Assert.Equal("0", implicitRule.Style["opacity"]);
        }

        [Fact]
        public void ADeclarationAfterANestedBlockStillApplies()
        {
            var sheet = ParseStyleSheet(".card { @starting-style { opacity: 0; } color: red; }");
            var parent = (StyleRule) sheet.Rules[0];
            Assert.Equal("rgb(255, 0, 0)", parent.Style["color"]);
            Assert.Single(parent.NestedRules);
        }

        [Fact]
        public void NestedInsideAConditionalRule()
        {
            var sheet = ParseStyleSheet("@media (min-width: 100px) { @starting-style { .a { opacity: 0; } } }");
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(sheet.Rules));
            var rule = Assert.IsAssignableFrom<IStartingStyleRule>(Assert.Single(media.Rules));
            Assert.Equal(".a", ((IStyleRule) rule.Rules[0]).SelectorText);
        }

        [Theory]
        [InlineData("@starting-style foo { .a { opacity: 0; } } .b { color: red; }")]
        [InlineData("@starting-style; .b { color: red; }")]
        public void APreludeOrStatementFormIsDroppedAndTheNextRuleSurvives(string source)
        {
            var sheet = ParseStyleSheet(source);
            var rule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            Assert.Equal(".b", rule.SelectorText);
        }
    }
}
