using System.Linq;
using Xunit;

namespace ExCSS.Tests
{
    /// <summary>
    /// The <see cref="ParserOptions.RawDeclarations"/> option: every declaration is kept with the
    /// name and value as written, instead of being resolved to a typed property. It exists for a
    /// host whose property set is not the web's, where value normalisation and shorthand expansion
    /// are wrong rather than merely unhelpful.
    /// </summary>
    public class RawDeclarationsTests : CssConstructionFunctions
    {
        private static StyleRule ParseRawRule(string source) =>
            (StyleRule)ParseStyleSheet(source, includeUnknownDeclarations: true, rawDeclarations: true).Rules.First();

        [Fact]
        public void AShorthandIsNotExpandedIntoLonghands()
        {
            var rule = ParseRawRule(".a { flex: 1; }");
            Assert.Equal(1, rule.Style.Length);
            Assert.Equal("flex", rule.Style[0]);
            Assert.Equal("1", rule.Style["flex"]);
        }

        [Fact]
        public void AValueIsNotNormalised()
        {
            var rule = ParseRawRule(".a { color: RED; }");
            Assert.Equal("RED", rule.Style["color"]);
        }

        [Fact]
        public void AnUnknownPropertyIsKeptAsWritten()
        {
            var rule = ParseRawRule(".a { -unity-slice-scale: 2; }");
            Assert.Equal("2", rule.Style["-unity-slice-scale"]);
        }

        [Fact]
        public void ImportanceIsStillRecorded()
        {
            var rule = ParseRawRule(".a { color: red !important; }");
            Assert.Equal("important", rule.Style.GetPropertyPriority("color"));
        }

        [Fact]
        public void TheOptionIsOffByDefault()
        {
            var rule = (StyleRule)ParseStyleSheet(".a { flex: 1; color: RED; }").Rules.First();
            Assert.Equal("1", rule.Style["flex-grow"]);
            Assert.Equal("rgb(255, 0, 0)", rule.Style["color"]);
        }
    }
}
