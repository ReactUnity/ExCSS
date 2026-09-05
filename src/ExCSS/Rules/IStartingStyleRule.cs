namespace ExCSS
{
    /// <summary>
    /// The <c>@starting-style</c> at-rule (<c>@starting-style { rules }</c>), per
    /// <see href="https://www.w3.org/TR/css-transitions-2/#defining-before-change-style">CSS Transitions 2</see>:
    /// the style an element has before its first style change, and so what a transition on
    /// insertion starts from. Groups ordinary style rules. Nested inside a style rule it holds an
    /// implicit rule carrying the enclosing rule's selector, as a nested <c>@media</c> does.
    /// </summary>
    public interface IStartingStyleRule : IGroupingRule
    {
    }
}
