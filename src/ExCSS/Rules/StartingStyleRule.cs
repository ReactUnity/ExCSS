using System.IO;

namespace ExCSS
{
    internal sealed class StartingStyleRule : GroupingRule, IStartingStyleRule
    {
        internal StartingStyleRule(StylesheetParser parser)
            : base(RuleType.StartingStyle, parser)
        {
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            writer.Write(formatter.Rule("@starting-style", null, rules));
        }
    }
}
