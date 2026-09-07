using System.IO;

namespace ExCSS
{
    internal sealed class ScopeRule : GroupingRule, IScopeRule
    {
        internal ScopeRule(StylesheetParser parser)
            : base(RuleType.Scope, parser)
        {
        }

        public string StartText { get; set; }

        public string EndText { get; set; }

        /// <summary>
        /// Reads a prelude of the form <c>(start)? [to (end)]?</c>, setting both texts. Returns false
        /// for anything else, which makes the rule invalid.
        /// </summary>
        internal bool SetPrelude(string prelude)
        {
            StartText = null;
            EndText = null;

            var text = (prelude ?? string.Empty).Trim();
            if (text.Length == 0) return true;

            var i = 0;
            if (text[0] == '(')
            {
                if (!ReadParenthesized(text, ref i, out var start)) return false;
                StartText = start;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i == text.Length) return true;
            }

            if (i + 2 > text.Length || !text.Substring(i, 2).Isi("to")) return false;
            i += 2;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;

            if (i >= text.Length || text[i] != '(' || !ReadParenthesized(text, ref i, out var end)) return false;
            EndText = end;

            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            return i == text.Length;
        }

        // The text between the parenthesis at i and its match, quotes respected; i lands after it.
        private static bool ReadParenthesized(string text, ref int i, out string inner)
        {
            inner = null;
            var depth = 0;
            var quote = '\0';
            var start = i + 1;

            for (; i < text.Length; i++)
            {
                var c = text[i];

                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'') quote = c;
                else if (c == '(') depth++;
                else if (c == ')' && --depth == 0)
                {
                    inner = text.Substring(start, i - start).Trim();
                    i++;
                    return inner.Length > 0;
                }
            }

            return false;
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            var prelude = StartText == null ? null : "(" + StartText + ")";
            if (EndText != null) prelude = (prelude == null ? "" : prelude + " ") + "to (" + EndText + ")";
            writer.Write(formatter.Rule("@scope", prelude, rules));
        }
    }
}
