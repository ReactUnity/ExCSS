namespace ExCSS
{
    /// <summary>
    /// The <c>@scope</c> at-rule (<c>@scope (start) to (end) { rules }</c>), per
    /// <see href="https://www.w3.org/TR/css-cascade-6/#scoped-styles">CSS Cascade 6</see>. Groups
    /// style rules whose subjects are limited to the subtree of an element matching
    /// <see cref="StartText"/>, less the subtrees of elements matching <see cref="EndText"/>. Both
    /// preludes are kept as written: which elements they name is the matcher's business.
    /// </summary>
    public interface IScopeRule : IGroupingRule
    {
        /// <summary>The scope-start selector list, or null when the prelude names none.</summary>
        string StartText { get; set; }

        /// <summary>The scope-end selector list, or null when the rule has no limit.</summary>
        string EndText { get; set; }
    }
}
