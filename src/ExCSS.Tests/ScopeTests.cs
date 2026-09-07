using System.Linq;
using Xunit;

namespace ExCSS.Tests
{
    /// <summary>
    /// <c>@scope</c> (<see href="https://www.w3.org/TR/css-cascade-6/#scoped-styles">CSS Cascade 6</see>):
    /// both preludes are kept as written, the rules in the block are scoped rather than nested, and a
    /// declaration written directly in the block styles the scoping root.
    /// </summary>
    public class ScopeTests : CssConstructionFunctions
    {
        private static IScopeRule ParseScope(string source) =>
            Assert.IsAssignableFrom<IScopeRule>(Assert.Single(ParseStyleSheet(source).Rules));

        private static string Selector(IRule rule) => Assert.IsAssignableFrom<IStyleRule>(rule).SelectorText;

        [Fact]
        public void KeepsBothPreludesAsWritten()
        {
            var rule = ParseScope("@scope (.card, .panel > .body) to (.content, [data-x=\"a) b\"]) { img { color: red; } }");
            Assert.Equal(RuleType.Scope, rule.Type);
            Assert.Equal(".card, .panel > .body", rule.StartText);
            Assert.Equal(".content, [data-x=\"a) b\"]", rule.EndText);
            Assert.Equal("img", Selector(Assert.Single(rule.Rules)));
        }

        [Fact]
        public void EitherPreludeMayBeLeftOut()
        {
            var bare = ParseScope("@scope { img { color: red; } }");
            Assert.Null(bare.StartText);
            Assert.Null(bare.EndText);

            var limitOnly = ParseScope("@scope to (.content) { img { color: red; } }");
            Assert.Null(limitOnly.StartText);
            Assert.Equal(".content", limitOnly.EndText);

            var startOnly = ParseScope("@scope (.card) { img { color: red; } }");
            Assert.Equal(".card", startOnly.StartText);
            Assert.Null(startOnly.EndText);
        }

        [Fact]
        public void SerializesBackToScope()
        {
            Assert.Equal("@scope (.card) to (.content) { img { color: rgb(255, 0, 0) } }",
                ParseScope("@scope (.card) to (.content) { img { color: red; } }").ToCss());
            Assert.Equal("@scope { img { color: rgb(255, 0, 0) } }", ParseScope("@scope { img { color: red; } }").ToCss());
            Assert.Equal("@scope to (.x) { img { color: rgb(255, 0, 0) } }", ParseScope("@scope to (.x) { img { color: red; } }").ToCss());
        }

        [Fact]
        public void TheNestingSelectorIsTheScopingRootAtZeroSpecificity()
        {
            var rule = ParseScope("@scope (.card) { & img { color: red; } &:hover { color: blue; } .a, & .b { color: green; } }");
            Assert.Equal(":where(:scope) img", Selector(rule.Rules[0]));
            Assert.Equal(":where(:scope):hover", Selector(rule.Rules[1]));
            Assert.Equal(".a,:where(:scope) .b", Selector(rule.Rules[2]));
        }

        [Fact]
        public void ARelativeSelectorIsRelativeToTheScopingRoot()
        {
            var rule = ParseScope("@scope (.card) { > img, + .x { color: red; } ~ .y { color: blue; } }");
            Assert.Equal(":scope>img,:scope+.x", Selector(rule.Rules[0]));
            Assert.Equal(":scope~.y", Selector(rule.Rules[1]));
        }

        [Fact]
        public void DeclarationsInTheBlockStyleTheScopingRoot()
        {
            var rule = ParseScope("@scope (.card) { color: red; border: 0; img { color: blue; } padding: 1px; }");
            Assert.Equal(3, rule.Rules.Length);

            var first = Assert.IsAssignableFrom<IStyleRule>(rule.Rules[0]);
            Assert.Equal(":where(:scope)", first.SelectorText);
            Assert.Equal("rgb(255, 0, 0)", first.Style["color"]);
            Assert.Equal("0", first.Style["border-width"]);

            Assert.Equal("img", Selector(rule.Rules[1]));

            var last = Assert.IsAssignableFrom<IStyleRule>(rule.Rules[2]);
            Assert.Equal(":where(:scope)", last.SelectorText);
            Assert.Equal("1px", last.Style["padding-top"]);
        }

        [Fact]
        public void AStyleRuleInTheBlockNestsAsUsual()
        {
            var rule = ParseScope("@scope (.card) { img { color: red; &:hover { color: blue; } .caption { color: green; } } }");
            var img = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(rule.Rules));
            Assert.Equal("img", img.SelectorText);
            Assert.Equal(2, img.NestedRules.Count);
            Assert.Equal(":is(img):hover", Selector(img.NestedRules[0]));
            Assert.Equal(":is(img) .caption", Selector(img.NestedRules[1]));
        }

        [Fact]
        public void RulesInAConditionalInsideTheBlockAreScopedToo()
        {
            var rule = ParseScope("@scope (.card) { @media (min-width: 100px) { & img { color: red; } color: blue; } }");
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(rule.Rules));
            Assert.Equal(":where(:scope) img", Selector(media.Rules[0]));
            Assert.Equal(":where(:scope)", Selector(media.Rules[1]));
        }

        [Fact]
        public void NestedInAStyleRuleOnlyTheStartSelectorNests()
        {
            var sheet = ParseStyleSheet(".a { color: red; @scope (.b) to (.c) { img { color: blue; } & { color: green; } } }");
            var parent = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            var rule = Assert.IsAssignableFrom<IScopeRule>(Assert.Single(parent.NestedRules));

            Assert.Equal(":is(.a) .b", rule.StartText);
            Assert.Equal(".c", rule.EndText);
            Assert.Equal("img", Selector(rule.Rules[0]));
            Assert.Equal(":where(:scope)", Selector(rule.Rules[1]));
            Assert.Equal("rgb(255, 0, 0)", parent.Style["color"]);
        }

        [Fact]
        public void NestedStartSelectorMayNameTheParentWithTheNestingSelector()
        {
            var sheet = ParseStyleSheet(".a { @scope (& > .b) { img { color: blue; } } }");
            var parent = (IStyleRule) sheet.Rules[0];
            var rule = Assert.IsAssignableFrom<IScopeRule>(Assert.Single(parent.NestedRules));
            Assert.Equal(":is(.a) > .b", rule.StartText);
        }

        [Fact]
        public void ScopesNestInEachOther()
        {
            var outer = ParseScope("@scope (.a) { @scope (.b) to (.c) { img { color: red; } } }");
            var inner = Assert.IsAssignableFrom<IScopeRule>(Assert.Single(outer.Rules));
            Assert.Equal(".b", inner.StartText);
            Assert.Equal(".c", inner.EndText);
            Assert.Equal("img", Selector(Assert.Single(inner.Rules)));
        }

        [Theory]
        [InlineData("@scope foo { img { color: red; } } .b { color: red; }")]
        [InlineData("@scope (.a) (.b) { img { color: red; } } .b { color: red; }")]
        [InlineData("@scope (.a) to { img { color: red; } } .b { color: red; }")]
        [InlineData("@scope (.a) to (.b) extra { img { color: red; } } .b { color: red; }")]
        [InlineData("@scope (.a); .b { color: red; }")]
        public void AnInvalidPreludeDropsTheRuleAndTheNextOneSurvives(string source)
        {
            var sheet = ParseStyleSheet(source);
            var rule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            Assert.Equal(".b", rule.SelectorText);
        }

        [Fact]
        public void AnInvalidNestedPreludeDropsTheBlockAndTheRestOfTheParentSurvives()
        {
            var sheet = ParseStyleSheet(".a { @scope foo { img { color: red; } } color: blue; }");
            var parent = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(sheet.Rules));
            Assert.Empty(parent.NestedRules);
            Assert.Equal("rgb(0, 0, 255)", parent.Style["color"]);
        }

        [Fact]
        public void TheRuleAfterAScopeBlockSurvives()
        {
            var sheet = ParseStyleSheet("@scope (.a) { color: red; img { color: blue; } } .b { color: green; }");
            Assert.Equal(2, sheet.Rules.Length);
            Assert.Equal(".b", Selector(sheet.Rules[1]));
        }
    }
}
