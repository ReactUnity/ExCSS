using System.Linq;
using Xunit;

namespace ExCSS.Tests
{
    /// <summary>
    /// CSS Nesting (<see href="https://www.w3.org/TR/css-nesting-1/">CSS Nesting 1</see>): the <c>&amp;</c>
    /// nesting selector and nested style rules inside a declaration block. Nested rules are resolved at
    /// parse time into ordinary rules with absolute selectors (<c>&amp;</c> → <c>:is(parent)</c>) and
    /// exposed via <see cref="IStyleRule.NestedRules"/>. Includes the regression guards that the
    /// declaration path is unaffected (hex colors, custom properties, a declaration after a nested rule).
    /// </summary>
    public class CssNestingTests : CssConstructionFunctions
    {
        private static StyleRule ParseRule(string source) =>
            (StyleRule)ParseStyleSheet(source).Rules.First();

        /// <summary>
        /// The single rule nested in <paramref name="rule"/>, as a style rule. NestedRules holds
        /// IRule, since a nested conditional group rule is not a style rule.
        /// </summary>
        private static IStyleRule Nested(IStyleRule rule) =>
            Assert.IsAssignableFrom<IStyleRule>(Assert.Single(rule.NestedRules));

        [Fact]
        public void ImplicitAmpersandCompoundResolvesToIsParent()
        {
            // `&.active` == `:is(.box).active` — the same element carrying both classes.
            var rule = ParseRule(".box { &.active { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.Equal(":is(.box).active", nested.SelectorText);
        }

        [Fact]
        public void ImplicitDescendantResolvesToIsParentDescendant()
        {
            // `& span` == `:is(.box) span` (descendant).
            var rule = ParseRule(".box { color: #ff0000; & span { color: #0000ff; } }");
            Assert.Equal(".box", rule.SelectorText);
            var nested = Nested(rule);
            Assert.Equal(":is(.box) span", nested.SelectorText);
        }

        [Fact]
        public void TypeSelectorNestedRuleIsNotMistakenForADeclaration()
        {
            // `p { ... }` inside a block starts with an Ident (like a property name) — the classifier must
            // still see the `{` before any `;` and treat it as a nested rule.
            var rule = ParseRule(".card { p { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.Equal(":is(.card) p", nested.SelectorText);
        }

        [Fact]
        public void NoAmpersandPreludeIsScopedUnderParent()
        {
            // A prelude with no `&` is made relative to the parent: `.inner` → `:is(.card) .inner`.
            var rule = ParseRule(".card { .inner { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.Equal(":is(.card) .inner", nested.SelectorText);
        }

        [Fact]
        public void LeadingChildCombinatorIsScopedUnderParent()
        {
            // `> p` == `:is(.card) > p` — only a direct child p matches.
            var rule = ParseRule(".card { > p { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.Equal(":is(.card)>p", nested.SelectorText);
        }

        [Fact]
        public void HexColorInNestedValueSurvivesTheLookahead()
        {
            // Regression: the classify-then-rewind must not corrupt a `#rrggbb` value (value-mode `#`
            // tokenization) the way a token buffer would. Compare against a non-nested equivalent.
            var nested = Nested(ParseRule(".card { p { color: #123456; } }"));
            var direct = ParseRule("p { color: #123456; }");
            Assert.Equal(direct.Style["color"], nested.Style["color"]);
            Assert.NotEqual(string.Empty, nested.Style["color"]);
        }

        [Fact]
        public void HexColorInTopLevelValueUnaffected()
        {
            // The whole feature must leave an ordinary (non-nested) declaration block unaffected.
            var direct = ParseRule("p { color: #123456; }");
            var control = ParseRule("q { color: #123456; }");
            Assert.Equal(control.Style["color"], direct.Style["color"]);
            Assert.Empty(direct.NestedRules);
        }

        [Fact]
        public void DeclarationAfterNestedRuleStillApplies()
        {
            // A declaration written AFTER a nested rule still applies to the parent (the outer loop resumes
            // correctly); here the later blue wins over the earlier red.
            var rule = ParseRule(".card { color: #ff0000; & span { color: #00ff00; } color: #0000ff; }");
            var blue = ParseRule("x { color: #0000ff; }").Style["color"];
            Assert.Equal(blue, rule.Style["color"]);
            Assert.Single(rule.NestedRules);
        }

        [Fact]
        public void CustomPropertyIsNotMistakenForANestedRule()
        {
            // `--x: …` is always a declaration, never a nested rule, even though the classifier scans for `{`.
            // (Custom-property value storage is a separate feature; here we only assert the `--x` declaration
            // is not swallowed as a nested rule and the real nested rule is still captured.)
            var rule = ParseRule(".card { --x: 1; & span { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.Equal(":is(.card) span", nested.SelectorText);
        }

        [Fact]
        public void MultiLevelNestingResolvesAgainstEachParent()
        {
            // `.a { & .b { & .c { … } } }` → `:is(.a) .b`, then `:is(:is(.a) .b) .c`.
            var a = ParseRule(".a { & .b { & .c { color: #0000ff; } } }");
            var b = Nested(a);
            Assert.Equal(":is(.a) .b", b.SelectorText);
            var c = Nested(b);
            Assert.Equal(":is(:is(.a) .b) .c", c.SelectorText);
        }

        [Fact]
        public void AmpersandInsideAttributeStringIsPreserved()
        {
            // Regression: a `&` inside an attribute-value string must NOT be substituted, and a prelude whose
            // only `&` is inside a string is still scoped under the parent (not the replace branch).
            var rule = ParseRule(".card { [data-x=\"a&b\"] { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.StartsWith(":is(.card)", nested.SelectorText);
            Assert.Contains("a&b", nested.SelectorText);
        }
        [Fact]
        public void NestedSupportsBecomesAConditionalRule()
        {
            // `.a { @supports (c) { color: … } }` == `@supports (c) { :is(.a) { color: … } }`: the
            // declarations belong to an implicit rule carrying the enclosing selector.
            var rule = ParseRule(".card { color: #ff0000; @supports (color: #0000ff) { color: #0000ff; } }");
            var supports = Assert.IsAssignableFrom<ISupportsRule>(Assert.Single(rule.NestedRules));
            Assert.Equal("(color: #0000ff)", supports.ConditionText);
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(((IGroupingRule)supports).Rules));
            Assert.Equal(":is(.card)", implicitRule.SelectorText);
            Assert.Equal(ParseRule("x { color: #0000ff; }").Style["color"], implicitRule.Style["color"]);
        }

        [Fact]
        public void NestedMediaBecomesAConditionalRule()
        {
            var rule = ParseRule(".card { @media (min-width: 600px) { color: #0000ff; } }");
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(rule.NestedRules));
            Assert.Equal("(min-width: 600px)", media.ConditionText);
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(media.Rules));
            Assert.Equal(":is(.card)", implicitRule.SelectorText);
        }

        [Fact]
        public void ARuleNestedInsideANestedConditionalRuleResolvesAgainstTheEnclosingSelector()
        {
            var rule = ParseRule(".card { @supports (color: #0000ff) { & span { color: #0000ff; } } }");
            var supports = Assert.IsAssignableFrom<ISupportsRule>(Assert.Single(rule.NestedRules));
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(((IGroupingRule)supports).Rules));
            // Wrapped twice, since it resolves against the implicit rule's own `:is(.card)`.
            Assert.Equal(":is(:is(.card)) span", Nested(implicitRule).SelectorText);
        }

        [Fact]
        public void ADeclarationAfterANestedConditionalRuleStillApplies()
        {
            // The at-rule's block has to be consumed exactly, or the outer loop resumes in the wrong place.
            var rule = ParseRule(".card { @media (min-width: 600px) { color: #00ff00; } color: #0000ff; }");
            Assert.Equal(ParseRule("x { color: #0000ff; }").Style["color"], rule.Style["color"]);
            Assert.Single(rule.NestedRules);
        }

        [Fact]
        public void ANestedConditionalRuleWithNoBlockIsSkipped()
        {
            var rule = ParseRule(".card { @media (min-width: 600px); color: #0000ff; }");
            Assert.Empty(rule.NestedRules);
            Assert.Equal(ParseRule("x { color: #0000ff; }").Style["color"], rule.Style["color"]);
        }

        [Fact]
        public void AnEscapedSelectorKeepsItsEscapesWhenNestedInto()
        {
            // The parent's serialized selector is `.hover:underline` -- a class followed by an
            // unknown pseudo-class -- which does not parse, so before this the nested rule was lost.
            var rule = ParseRule(@".hover\:underline { & span { color: #0000ff; } }");
            var nested = Nested(rule);
            Assert.Equal(@":is(.hover\:underline) span", nested.Selector.StylesheetText.Text);
        }

        [Fact]
        public void AHexEscapeSurvivesResolution()
        {
            // A class name starting with a digit can only be written this way.
            var rule = ParseRule(@".\32 xl\:flex { & span { color: #0000ff; } }");
            Assert.Equal(@":is(.\32 xl\:flex) span", Nested(rule).Selector.StylesheetText.Text);
        }

        [Fact]
        public void AnEscapeSurvivesEveryLevelOfNesting()
        {
            var rule = ParseRule(@".w-1\/2 { & .b { & .c { color: #0000ff; } } }");
            var b = Nested(rule);
            Assert.Equal(@":is(.w-1\/2) .b", b.Selector.StylesheetText.Text);
            Assert.Equal(@":is(:is(.w-1\/2) .b) .c", Nested(b).Selector.StylesheetText.Text);
        }

        [Fact]
        public void AnEscapedSelectorKeepsItsEscapesInANestedConditionalRule()
        {
            var rule = ParseRule(@".hover\:underline { @media (min-width: 600px) { color: #0000ff; } }");
            var media = Assert.IsAssignableFrom<IMediaRule>(Assert.Single(rule.NestedRules));
            var implicitRule = Assert.IsAssignableFrom<IStyleRule>(Assert.Single(media.Rules));
            Assert.Equal(@":is(.hover\:underline)", implicitRule.Selector.StylesheetText.Text);
        }
    }
}
