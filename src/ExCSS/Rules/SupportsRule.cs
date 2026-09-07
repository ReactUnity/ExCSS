using System;
using System.IO;
using System.Linq;

namespace ExCSS
{
    internal sealed class SupportsRule : ConditionRule, ISupportsRule
    {
        private string _condition;

        internal SupportsRule(StylesheetParser parser)
            : base(RuleType.Supports, parser)
        {
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            writer.Write(formatter.Rule("@supports", ConditionText, rules));
        }


        /// <summary>
        /// The condition serialized when the grammar here holds it in full, and otherwise as written:
        /// <c>selector()</c> and every other function query are valid CSS the grammar has no node
        /// for, which a consumer with its own evaluator can still read.
        /// </summary>
        public string ConditionText
        {
            get => _condition ?? Condition.ToCss();
            set
            {
                var condition = Parser.ParseCondition(value);

                if (condition != null)
                {
                    _condition = null;
                    Condition = condition;
                }
                else
                {
                    RemoveChild(Condition);
                    _condition = value;
                }
            }
        }

        public IConditionFunction Condition
        {
            get => Children.OfType<IConditionFunction>().FirstOrDefault() ?? new EmptyCondition();
            set
            {
                if (value == null) return;

                RemoveChild(Condition);
                AppendChild(value);
            }
        }
    }
}