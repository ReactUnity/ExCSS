using System.Collections.Generic;

namespace ExCSS
{
    public interface IStyleRule : IRule
    {
        string SelectorText { get; set; }
        StyleDeclaration Style { get; }
        ISelector Selector { get; set; }

        /// <summary>
        /// CSS Nesting: rules nested inside this rule's block. A nested style rule is already
        /// resolved to an absolute selector against this (parent) rule; a nested conditional group
        /// rule holds an implicit style rule carrying this rule's own selector. Empty for a
        /// non-nesting rule.
        /// </summary>
        IReadOnlyList<IRule> NestedRules { get; }
    }
}
