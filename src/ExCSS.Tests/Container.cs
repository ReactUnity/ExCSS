namespace ExCSS.Tests
{
    using ExCSS;
    using Xunit;
    using System.Linq;

    public class CssContainerTests : CssConstructionFunctions
    {
        [Fact]
        public void SimpleContainer()
        {
            const string source = "@container tall (min-width: 500px) and (min-height: 300px) {h2 { line-height: 1.6; } }";
            var result = ParseStyleSheet(source);
            Assert.Equal(source, result.StylesheetText.Text);
            var rule = result.Rules[0] as ContainerRule;
            Assert.NotNull(rule);
            Assert.Equal("@container tall (min-width: 500px) and (min-height: 300px) { h2 { line-height: 1.6 } }", rule.Text);
            Assert.Equal("tall", rule.Name);
            Assert.Equal("(min-width: 500px) and (min-height: 300px)", rule.ConditionText);
            var childRule = rule.Children.OfType<StyleRule>().First();
            Assert.Equal("h2 { line-height: 1.6 }", childRule.ToCss());
        }

        [Fact]
        public void ContainerWithoutName()
        {
            const string source = "@container (min-width: 500px) and (min-height: 300px) {h2 { line-height: 1.6; } }";
            var result = ParseStyleSheet(source);
            Assert.Equal(source, result.StylesheetText.Text);
            var rule = result.Rules[0] as ContainerRule;
            Assert.NotNull(rule);
            Assert.Equal("@container (min-width: 500px) and (min-height: 300px) { h2 { line-height: 1.6 } }", rule.Text);
            Assert.Equal(string.Empty, rule.Name);
            Assert.Equal("(min-width: 500px) and (min-height: 300px)", rule.ConditionText);
            var childRule = rule.Children.OfType<StyleRule>().First();
            Assert.Equal("h2 { line-height: 1.6 }", childRule.ToCss());
        }

        [Fact]
        public void ContainerWithoutCondition()
        {
            const string source = "@container tall {h2 { line-height: 1.6; } }";
            var result = ParseStyleSheet(source);
            Assert.Equal(source, result.StylesheetText.Text);
            var rule = result.Rules[0] as ContainerRule;
            Assert.NotNull(rule);
            Assert.Equal("@container tall { h2 { line-height: 1.6 } }", rule.Text);
            Assert.Equal("tall", rule.Name);
            Assert.Equal(string.Empty, rule.ConditionText);
            var childRule = rule.Children.OfType<StyleRule>().First();
            Assert.Equal("h2 { line-height: 1.6 }", childRule.ToCss());
        }

        [Fact]
        public void ContainerWithComparisonOperators()
        {
            const string source = "@container tall (width < 500px) and (height >= 300px) {h2 { line-height: 1.6; } }";
            var result = ParseStyleSheet(source);
            Assert.Equal(source, result.StylesheetText.Text);
            var rule = result.Rules[0] as ContainerRule;
            Assert.NotNull(rule);
            Assert.Equal("@container tall (width < 500px) and (height >= 300px) { h2 { line-height: 1.6 } }", rule.Text);
            Assert.Equal("tall", rule.Name);
            Assert.Equal("(width < 500px) and (height >= 300px)", rule.ConditionText);
            var childRule = rule.Children.OfType<StyleRule>().First();
            Assert.Equal("h2 { line-height: 1.6 }", childRule.ToCss());
        }

        [Fact]
        public void CSSWithTwoContainers()
        {
            const string source = @"li {
  container-type: inline-size;
}

@container (min-width: 45ch) {
  li span {
    color: rgb(255, 0, 0);
    font-size: 2rem !important;
  }
}

@container (min-width: 70ch) {
  li span {
    color: rgb(0, 0, 255);
    font-size: 3rem !important;
  }
}";
            var result = ParseStyleSheet(source);
            Assert.Equal(source, result.StylesheetText.Text);
            Assert.Equal(3, result.Rules.Length);
            var rule1 = result.Rules[0] as StyleRule;
            var rule2 = result.Rules[1] as ContainerRule;
            var rule3 = result.Rules[2] as ContainerRule;
            Assert.NotNull(rule1);
            Assert.NotNull(rule2);
            Assert.NotNull(rule3);
            Assert.Equal("li { container-type: inline-size }", rule1.ToCss());
            Assert.Equal("@container (min-width: 45ch) { li span { color: rgb(255, 0, 0); font-size: 2rem !important } }", rule2.ToCss());
            Assert.Equal("@container (min-width: 70ch) { li span { color: rgb(0, 0, 255); font-size: 3rem !important } }", rule3.ToCss());
        }

        // A container query is not a media query: the range syntax, style() and `not` have no
        // media-list form, so the condition is kept as written and the media list is best effort.
        [Theory]
        [InlineData("@container (width > 400px) {h2 { line-height: 1.6; } }", "", "(width > 400px)")]
        [InlineData("@container card (400px <= width <= 800px) {h2 { line-height: 1.6; } }", "card", "(400px <= width <= 800px)")]
        [InlineData("@container style(--theme: dark) {h2 { line-height: 1.6; } }", "", "style(--theme: dark)")]
        [InlineData("@container card style(--x: 1) and (min-width: 10px) {h2 { line-height: 1.6; } }", "card", "style(--x: 1) and (min-width: 10px)")]
        [InlineData("@container not (min-width: 10px) {h2 { line-height: 1.6; } }", "", "not (min-width: 10px)")]
        [InlineData("@container   card   (min-width:10px)   {h2 { line-height: 1.6; } }", "card", "(min-width:10px)")]
        public void ConditionIsKeptAsWritten(string source, string name, string condition)
        {
            var result = ParseStyleSheet(source);
            var rule = Assert.IsType<ContainerRule>(Assert.Single(result.Rules));
            Assert.Equal(name, rule.Name);
            Assert.Equal(condition, rule.ConditionText);
            Assert.Equal("h2 { line-height: 1.6 }", rule.Children.OfType<StyleRule>().First().ToCss());
        }

        [Fact]
        public void MediaListStillReadsTheSharedFeatures()
        {
            var rule = ParseStyleSheet("@container card (min-width: 500px) and (max-width: 800px) { h2 { color: red } }").Rules[0] as ContainerRule;
            Assert.Equal(1, rule.Media.Length);
            Assert.Equal("(min-width: 500px) and (max-width: 800px)", rule.Media.MediaText);

            rule = ParseStyleSheet("@container style(--x: 1) { h2 { color: red } }").Rules[0] as ContainerRule;
            Assert.Equal(0, rule.Media.Length);
            Assert.Equal("style(--x: 1)", rule.ConditionText);
        }

        [Fact]
        public void ContainerNestedInAStyleRuleIsAGroupRuleWithAnImplicitRule()
        {
            var sheet = ParseStyleSheet(".card { color: red; @container sidebar (width > 400px) { color: blue; } }");
            var parent = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            var container = Assert.IsAssignableFrom<IContainerRule>(Assert.Single(parent.NestedRules));
            Assert.Equal("sidebar", container.Name);
            Assert.Equal("(width > 400px)", container.ConditionText);
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(container.Rules));
            Assert.Equal(":is(.card)", implicitRule.SelectorText);
            Assert.Equal("rgb(0, 0, 255)", implicitRule.Style["color"]);
        }

        [Fact]
        public void ADeclarationAfterANestedContainerStillApplies()
        {
            var sheet = ParseStyleSheet(".card { @container (min-width: 10px) { color: blue; } color: red; }");
            var parent = (StyleRule) sheet.Rules[0];
            Assert.Equal("rgb(255, 0, 0)", parent.Style["color"]);
            Assert.Single(parent.NestedRules);
        }

        [Fact]
        public void StatementFormIsDroppedAndTheNextRuleSurvives()
        {
            var sheet = ParseStyleSheet("@container (min-width: 10px); .b { color: red; }");
            var rule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            Assert.Equal(".b", rule.SelectorText);
        }
    }
}
