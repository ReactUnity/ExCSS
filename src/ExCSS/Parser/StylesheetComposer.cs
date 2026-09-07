using System;
using System.Collections.Generic;
using System.Linq;

namespace ExCSS
{
    internal sealed class StylesheetComposer
    {
        private readonly Lexer _lexer;
        private readonly StylesheetParser _parser;
        private readonly Stack<StylesheetNode> _nodes;

        // The source index (raw, into _lexer.Source) immediately before the most recently read token.
        // Captured by NextToken so a CSS-Nesting classification look-ahead can rewind to a construct's
        // exact start without any position arithmetic (which is unreliable across \r\n normalization and
        // unicode escapes) and can slice the nested prelude's source text.
        private int _markBeforeLastToken;

        public StylesheetComposer(Lexer lexer, StylesheetParser parser)
        {
            _lexer = lexer;
            _parser = parser;
            _nodes = new Stack<StylesheetNode>();
        }

        public Rule CreateAtRule(Token token)
        {
            if (token.Data.Is(RuleNames.Media)) return CreateMedia(token);

            if (token.Data.Is(RuleNames.FontFace)) return CreateFontFace(token);

            if (token.Data.Is(RuleNames.Keyframes)) return CreateKeyframes(token);

            if (token.Data.Is(RuleNames.Import)) return CreateImport(token);

            if (token.Data.Is(RuleNames.Charset)) return CreateCharset(token);

            if (token.Data.Is(RuleNames.Namespace)) return CreateNamespace(token);

            if (token.Data.Is(RuleNames.Page)) return CreatePage(token);

            if (token.Data.Is(RuleNames.Supports)) return CreateSupports(token);

            if (token.Data.Is(RuleNames.ViewPort)) return CreateViewport(token);

            if (token.Data.Is(RuleNames.Container)) return CreateContainer(token);

            if (token.Data.Is(RuleNames.Property)) return CreateProperty(token);

            if (token.Data.Is(RuleNames.Layer)) return CreateLayer(token);

            if (token.Data.Is(RuleNames.FontPaletteValues)) return CreateFontPaletteValues(token);

            if (token.Data.Is(RuleNames.StartingStyle)) return CreateStartingStyle(token);

            if (token.Data.Is(RuleNames.Scope)) return CreateScope(token);

            return token.Data.Is(RuleNames.Document) ? CreateDocument(token) : CreateUnknown(token);
        }

        public Rule CreateRule(Token token)
        {
            switch (token.Type)
            {
                case TokenType.AtKeyword:
                    return CreateAtRule(token);

                case TokenType.CurlyBracketOpen:
                    RaiseErrorOccurred(ParseError.InvalidBlockStart, token.Position);
                    MoveToRuleEnd(ref token);
                    return null;

                case TokenType.String:
                case TokenType.Url:
                case TokenType.CurlyBracketClose:
                case TokenType.RoundBracketClose:
                case TokenType.SquareBracketClose:
                    RaiseErrorOccurred(ParseError.InvalidToken, token.Position);
                    MoveToRuleEnd(ref token);
                    return null;

                default:
                    return CreateStyle(token);
            }
        }

        public Rule CreateCharset(Token current)
        {
            var rule = new CharsetRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);

            if (token.Type == TokenType.String) rule.CharacterSet = token.Data;

            JumpToEnd(ref token);
            rule.StylesheetText = CreateView(start, token.Position);
            _nodes.Pop();
            return rule;
        }

        public Rule CreateDocument(Token current)
        {
            var rule = new DocumentRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            FillFunctions(function => rule.AppendChild(function), ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillRules(rule);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateViewport(Token current)
        {
            var rule = new ViewportRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillDeclarations(rule, PropertyFactory.Instance.CreateViewport);

                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateFontFace(Token current)
        {
            var rule = new FontFaceRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillDeclarations(rule, PropertyFactory.Instance.CreateFont);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateProperty(Token current)
        {
            var rule = new PropertyRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            rule.Name = GetRuleName(ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillDeclarations(rule, PropertyFactory.Instance.CreatePropertyDescriptor);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateLayer(Token current)
        {
            var start = current.Position;
            var token = NextToken();
            ParseComments(ref token);

            // Read the (optional) comma-separated layer name list, stopping at '{' (block form) or
            // ';'/EOF (statement form).
            var names = ReadLayerNames(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                // Block form: `@layer name { rules }` (at most one name; anonymous if none).
                var rule = new LayerRule(_parser)
                {
                    Name = names.Count > 0 ? names[0] : string.Empty
                };
                _nodes.Push(rule);
                var end = FillRules(rule);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            // Statement form: `@layer a, b, c;` — declares layer order only, no rules. token is at ';'/EOF.
            var statement = new LayerStatementRule(_parser);
            statement.Names.AddRange(names);
            statement.StylesheetText = CreateView(start, token.Position);
            return statement;
        }

        /// <summary>
        /// Reads a <c>@layer</c> prelude: zero or more comma-separated layer names, each a dotted
        /// ident sequence (e.g. <c>framework.utilities</c>). Advances <paramref name="token"/> up to
        /// (but not past) the terminating <c>{</c>, <c>;</c>, or end of file.
        /// </summary>
        private List<string> ReadLayerNames(ref Token token)
        {
            var names = new List<string>();
            var current = new System.Text.StringBuilder();

            void Flush()
            {
                if (current.Length <= 0) return;
                names.Add(current.ToString());
                current.Clear();
            }

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketOpen, TokenType.Semicolon))
            {
                switch (token.Type)
                {
                    case TokenType.Ident:
                        current.Append(token.Data);
                        break;
                    case TokenType.Delim when token.Data == ".":
                        current.Append('.');
                        break;
                    case TokenType.Comma:
                        Flush();
                        break;
                    // Whitespace/comments and anything else separating names are ignored.
                }

                token = NextToken();
                ParseComments(ref token);
            }

            Flush();
            return names;
        }

        public Rule CreateFontPaletteValues(Token current)
        {
            var rule = new FontPaletteValuesRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            rule.Name = GetRuleName(ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillDeclarations(rule, PropertyFactory.Instance.CreateFontPaletteDescriptor);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateImport(Token current)
        {
            var rule = new ImportRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);

            if (token.Is(TokenType.String, TokenType.Url))
            {
                rule.Href = token.Data;
                token = NextToken();
                ParseComments(ref token);
                FillMediaList(rule.Media, TokenType.Semicolon, ref token);
            }

            ParseComments(ref token);
            JumpToEnd(ref token);
            rule.StylesheetText = CreateView(start, token.Position);
            _nodes.Pop();
            return rule;
        }

        public Rule CreateKeyframes(Token current)
        {
            var rule = new KeyframesRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            rule.Name = GetRuleName(ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillKeyframeRules(rule);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateMedia(Token current)
        {
            var rule = new MediaRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            FillMediaList(rule.Media, TokenType.CurlyBracketOpen, ref token);
            ParseComments(ref token);

            if (token.Type != TokenType.CurlyBracketOpen)
                while (token.Type != TokenType.EndOfFile)
                {
                    if (token.Type == TokenType.Semicolon)
                    {
                        _nodes.Pop();
                        return null;
                    }

                    if (token.Type == TokenType.CurlyBracketOpen) break;

                    token = NextToken();
                }

            var end = FillRules(rule);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule;
        }

        public Rule CreateContainer(Token current)
        {
            var rule = new ContainerRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            FillContainerPrelude(rule, ref token);

            if (token.Type != TokenType.CurlyBracketOpen)
            {
                _nodes.Pop();
                return null;
            }

            var end = FillRules(rule);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule;
        }

        /// <summary>
        /// <c>@starting-style { rules }</c> (CSS Transitions 2). The rule takes no prelude; anything
        /// before its block invalidates it, and the block is skipped.
        /// </summary>
        public Rule CreateStartingStyle(Token current)
        {
            var start = current.Position;
            var token = NextToken();
            ParseComments(ref token);

            if (token.Type != TokenType.CurlyBracketOpen) return SkipDeclarations(token);

            var rule = new StartingStyleRule(_parser);
            _nodes.Push(rule);
            var end = FillRules(rule);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule;
        }

        /// <summary>
        /// <c>@scope (start) to (end) { rules }</c> (CSS Cascade 6). Both selector lists are kept as
        /// written and either may be left out; any other prelude invalidates the rule, and the
        /// block is skipped. The block's own rules are scoped, see <see cref="FillRules"/>.
        /// </summary>
        public Rule CreateScope(Token current)
        {
            var start = current.Position;
            var token = NextToken();
            ParseComments(ref token);

            var rule = new ScopeRule(_parser);
            var valid = rule.SetPrelude(ReadRawPrelude(ref token));

            if (token.Type != TokenType.CurlyBracketOpen || !valid) return SkipDeclarations(token);

            _nodes.Push(rule);
            var end = FillRules(rule);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule;
        }

        /// <summary>
        /// Reads a container query prelude: an optional name, then the condition, which is kept as
        /// written because a container query is not a media query (range syntax, <c>style()</c>).
        /// Leaves <paramref name="token"/> on the block's <c>{</c>, or on the <c>;</c> or end of file
        /// that means there is none.
        /// </summary>
        private void FillContainerPrelude(ContainerRule rule, ref Token token)
        {
            // `not` opens a negated condition rather than naming the container.
            rule.Name = token.Type == TokenType.Ident && !token.Data.Isi(Keywords.Not) ? GetRuleName(ref token) : string.Empty;
            ParseComments(ref token);
            rule.ConditionText = ReadRawPrelude(ref token);
        }

        /// <summary>
        /// Reads a supports prelude, leaving <paramref name="token"/> on the block's <c>{</c> or on
        /// what ends the rule instead. A prelude the condition grammar cannot read to the block is
        /// kept as written: <c>selector()</c>, and every other function, is valid CSS it has no node
        /// for. False when nothing a condition can start with does, a bare declaration say.
        /// </summary>
        private bool FillSupportsPrelude(SupportsRule rule, ref Token token)
        {
            var start = _markBeforeLastToken;
            var valid = token.Type == TokenType.RoundBracketOpen || token.Type == TokenType.Function || token.Data.Isi(Keywords.Not);
            var condition = AggregateCondition(ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                rule.Condition = condition;
                return true;
            }

            rule.ConditionText = ReadRawPrelude(ref token, start);
            return valid;
        }

        /// <summary>
        /// The source text from the current token up to, not including, the next top-level <c>{</c>,
        /// <c>;</c> or end of file, which <paramref name="token"/> is left on. Read from the raw source
        /// by the lexer's insertion marks, as the nesting code does, so escapes and spacing survive.
        /// </summary>
        private string ReadRawPrelude(ref Token token) => ReadRawPrelude(ref token, _markBeforeLastToken);

        private string ReadRawPrelude(ref Token token, int start)
        {
            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketOpen, TokenType.Semicolon))
                token = NextToken();

            var end = _markBeforeLastToken;
            return end > start ? _lexer.Source.Text.Substring(start, end - start).Trim() : string.Empty;
        }
        public Rule CreateNamespace(Token current)
        {
            var rule = new NamespaceRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            rule.Prefix = GetRuleName(ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.Url) rule.NamespaceUri = token.Data;

            JumpToEnd(ref token);
            rule.StylesheetText = CreateView(start, token.Position);
            _nodes.Pop();
            return rule;
        }

        public Rule CreatePage(Token current)
        {
            var rule = new PageRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);

            if (token.Type != TokenType.CurlyBracketOpen)
            {
                // A pseudo-selector exists.  Parse it prior
                // to declarations
                // e.g. @page :left{...}
                rule.Selector = CreatePageSelector(ref token);
                ParseComments(ref token);
            }

            if (token.Type == TokenType.CurlyBracketOpen)
            {
                var end = FillDeclarations(rule.Style);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateSupports(Token current)
        {
            var rule = new SupportsRule(_parser);
            var start = current.Position;
            var token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);
            var valid = FillSupportsPrelude(rule, ref token);
            ParseComments(ref token);

            if (token.Type == TokenType.CurlyBracketOpen && valid)
            {
                var end = FillRules(rule);
                rule.StylesheetText = CreateView(start, end);
                _nodes.Pop();
                return rule;
            }

            _nodes.Pop();
            return SkipDeclarations(token);
        }

        public Rule CreateStyle(Token current)
        {
            var rule = new StyleRule(_parser);
            var start = current.Position;
            _nodes.Push(rule);
            ParseComments(ref current);
            rule.Selector = CreateSelector(ref current);
            var end = FillDeclarations(rule.Style);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule.Selector != null ? rule : null;
        }

        public Rule CreateMarginStyle(ref Token current)
        {
            var rule = new MarginStyleRule(_parser);
            var start = current.Position;
            _nodes.Push(rule);
            ParseComments(ref current);
            rule.Selector = CreateMarginSelector(ref current);
            var end = FillDeclarations(rule.Style);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule.Selector != null ? rule : null;
        }

        public KeyframeRule CreateKeyframeRule(Token current)
        {
            var rule = new KeyframeRule(_parser);
            var start = current.Position;
            _nodes.Push(rule);
            ParseComments(ref current);
            rule.Key = CreateKeyframeSelector(ref current);
            var end = FillDeclarations(rule.Style);
            rule.StylesheetText = CreateView(start, end);
            _nodes.Pop();
            return rule.Key != null ? rule : null;
        }

        public Rule CreateUnknown(Token current)
        {
            var start = current.Position;

            if (_parser.Options.IncludeUnknownRules)
            {
                var token = NextToken();
                var rule = new UnknownRule(current.Data, _parser);
                _nodes.Push(rule);

                while (token.IsNot(TokenType.CurlyBracketOpen, TokenType.Semicolon, TokenType.EndOfFile))
                    token = NextToken();

                if (token.Type == TokenType.CurlyBracketOpen)
                {
                    var curly = 1;

                    do
                    {
                        token = NextToken();

                        switch (token.Type)
                        {
                            case TokenType.CurlyBracketOpen:
                                curly++;
                                break;
                            case TokenType.CurlyBracketClose:
                                curly--;
                                break;
                            case TokenType.EndOfFile:
                                curly = 0;
                                break;
                        }
                    } while (curly != 0);
                }

                rule.StylesheetText = CreateView(start, token.Position);
                _nodes.Pop();
                return rule;
            }

            RaiseErrorOccurred(ParseError.UnknownAtRule, start);
            MoveToRuleEnd(ref current);
            return default(UnknownRule);
        }

        public TokenValue CreateValue(ref Token token)
        {
            return CreateValue(TokenType.CurlyBracketClose, ref token, out _);
        }

        public List<Medium> CreateMedia(ref Token token)
        {
            var list = new List<Medium>();
            ParseComments(ref token);

            while (token.Type != TokenType.EndOfFile)
            {
                var medium = CreateMedium(ref token);

                if (medium == null || token.IsNot(TokenType.Comma, TokenType.EndOfFile))
                    throw new ParseException("Unable to create medium or end of file reached unexpectedly");

                token = NextToken();
                ParseComments(ref token);
                list.Add(medium);
            }

            return list;
        }

        public TextPosition CreateRules(Stylesheet sheet)
        {
            var token = NextToken();
            _nodes.Push(sheet);
            ParseComments(ref token);

            while (token.Type != TokenType.EndOfFile)
            {
                var rule = CreateRule(token);
                token = NextToken();
                ParseComments(ref token);
                sheet.Rules.Add(rule);
            }

            _nodes.Pop();
            return token.Position;
        }

        public IConditionFunction CreateCondition(ref Token token)
        {
            ParseComments(ref token);
            return AggregateCondition(ref token);
        }

        public KeyframeSelector CreateKeyframeSelector(ref Token token)
        {
            var keys = new List<Percent>();
            var valid = true;
            var start = token.Position;
            ParseComments(ref token);

            while (token.Type != TokenType.EndOfFile)
            {
                if (keys.Count > 0)
                {
                    if (token.Type == TokenType.CurlyBracketOpen) break;
                    if (token.Type != TokenType.Comma)
                        valid = false;
                    else
                        token = NextToken();

                    ParseComments(ref token);
                }

                switch (token.Type)
                {
                    case TokenType.Percentage:
                        keys.Add(new Percent(((UnitToken) token).Value));
                        break;
                    case TokenType.Ident when token.Data.Is(Keywords.From):
                        keys.Add(Percent.Zero);
                        break;
                    case TokenType.Ident when token.Data.Is(Keywords.To):
                        keys.Add(Percent.Hundred);
                        break;
                    default:
                        valid = false;
                        break;
                }

                token = NextToken();
                ParseComments(ref token);
            }

            if (!valid) RaiseErrorOccurred(ParseError.InvalidSelector, start);

            return new KeyframeSelector(keys);
        }

        private PageSelector CreatePageSelector(ref Token token)
        {
            // The CSS Paged Media 3 selector grammar, per comma-separated entry: an optional <ident> page
            // name followed by an optional :<ident> pseudo-class - "@page chapter1:left, chapter2:left"
            // (name+pseudo), "@page chapter" (name only), "@page :first" (pseudo only). Page names are
            // case-sensitive custom-idents; pseudo-class keywords (first/left/right) are matched
            // case-insensitively by the caller.
            var entries = new List<PageSelectorEntry>();

            while (true)
            {
                string name = null;
                string pseudo = null;

                if (token.Type == TokenType.Ident)
                {
                    name = token.Data;
                    token = NextToken();
                }

                if (token.Type == TokenType.Colon)
                {
                    token = NextToken();
                    if (token.Type == TokenType.Ident)
                    {
                        pseudo = token.Data;
                        token = NextToken();
                    }
                }

                if (name != null || pseudo != null)
                    entries.Add(new PageSelectorEntry(name, pseudo));

                ParseComments(ref token);

                if (token.Type != TokenType.Comma) break;

                token = NextToken();
                ParseComments(ref token);
            }

            var selector = new PageSelector(entries);

            //var start = token.Position;

            //while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketOpen, TokenType.CurlyBracketClose))
            //{
            //    var a = 1;
            //    token = NextToken();
            //}

            //var result = selector.ToPool();

            //if (result is StylesheetNode node)
            //{
            //    var end = token.Position.Shift(-1);
            //node.StylesheetText = CreateView(start, end);
            //}

            //if (!selectorIsValid && !_parser.Options.AllowInvalidValues)
            //{
            //    RaiseErrorOccurred(ParseError.InvalidSelector, start);
            //    result = null;
            //}

            //return result;

            return selector;
        }

        public List<DocumentFunction> CreateFunctions(ref Token token)
        {
            var functions = new List<DocumentFunction>();
            ParseComments(ref token);
            FillFunctions(function => functions.Add(function), ref token);
            return functions;
        }

        public TextPosition FillDeclarations(StyleDeclaration style)
        {
            var finalProperties = new Dictionary<string, IProperty>(StringComparer.OrdinalIgnoreCase);
            var token = NextToken();
            _nodes.Push(style);
            ParseComments(ref token);

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketClose))
            {
                // @page selectors support declaration blocks in the form of at rules.  This 
                // conditional accounts for the nested at with a page parent
                //
                // @page {
                //   @top-left { ... /* document name */ }
                //   @bottom-center { ... /* page number */}
                // }
                if (token.Is(TokenType.AtKeyword))
                {
                    var parentPageRule = _nodes.FirstOrDefault(parent => parent is PageRule);
                    if (parentPageRule != null)
                    {
                        //var genericAtRule = CreateMarginRule(ref token);
                        //parentPageRule.AppendChild(genericAtRule);
                        // Rewind to capture the margin's @ symbol

                        var marginToken = new Token(TokenType.Ident, token.Data, token.Position);
                        var marginStyle = CreateMarginStyle(ref marginToken);
                        parentPageRule.AppendChild(marginStyle);
                        // CreateMarginStyle's inner FillDeclarations consumed through the margin box's
                        // closing '}' but left marginToken at the box's own '{'. Reusing it here re-enters
                        // the loop on a stale token, so any declaration after the margin box (and any
                        // further margin box) was dropped. Advance to the next real token instead.
                        token = NextToken();
                    }
                    else if (!TryCreateNestedConditionalRule(ref token))
                    {
                        // Advance to the next token or this is an endless loop
                        token = NextToken();
                    }
                }
                else if (TryCreateNestedRule(ref token))
                {
                    // CSS Nesting: a nested style rule was parsed and attached to the enclosing rule.
                }
                else FillDeclaration(style, finalProperties, ref token);

                ParseComments(ref token);
            }

            _nodes.Pop();
            return token.Position;
        }

        /// <summary>
        /// One declaration, read from <paramref name="token"/> and set on <paramref name="style"/>
        /// unless an important declaration of the same property already is.
        /// </summary>
        private void FillDeclaration(StyleDeclaration style, Dictionary<string, IProperty> finalProperties, ref Token token)
        {
                // RawDeclarations keeps every declaration exactly as it was written: no typed
                // property, so no value normalisation, and no shorthand expansion below. For a
                // host whose property set is not the web's, that expansion is wrong rather than
                // merely unhelpful.
                var createDeclaration = _parser.Options.RawDeclarations
                    ? RawDeclaration
                    : new Func<string, Property>(PropertyFactory.Instance.Create);
                var sourceProperty = CreateDeclarationWith(createDeclaration, ref token);
                var resolvedProperties = new[] {sourceProperty};

                if (sourceProperty is {HasValue: true})
                {
                    // For shorthand properties we need to first find out what alternate set of properties they will
                    // end up resolving into so that we can compare them with their previously parsed counterparts (if any)
                    // and determine which one takes priority over the other.
                    // Example 1: "margin-left: 5px !important; text-align:center; margin: 3px;";
                    // Example 2: "margin: 5px !important; text-align:center; margin-left: 3px;";
                    if (sourceProperty is ShorthandProperty shorthandProperty)
                    {
                        if (shorthandProperty.DeclaredValue.Original.ContainsFunction(FunctionNames.Var))
                        {
                            // A var() reference can't be split into per-longhand slices at parse time -
                            // the referenced custom property's value is only known per-element, at cascade
                            // time. Keep the shorthand declaration whole so substitution and expansion can
                            // happen once it is resolved (CSS Variables 1 3.2).
                            resolvedProperties = new Property[] { shorthandProperty };
                        }
                        else
                        {
                            resolvedProperties = PropertyFactory.Instance.CreateLonghandsFor(shorthandProperty.Name);
                            shorthandProperty.Export(resolvedProperties);
                        }
                    }

                    foreach (var resolvedProperty in resolvedProperties)
                    {
                        // The following relies on the fact that the tokens are processed in 
                        // top-to-bottom order of how they are defined in the parsed style declaration.
                        // This handles exposing the correct value for a property when it appears multiple 
                        // times in the same style declaration.
                        // Example: "background-color:green !important; text-align:center; background-color:yellow;";
                        // In this example even though background-color yellow is defined last, the previous value
                        // of green should be the one exposed given it is tagged as important.
                        // ------------------------------------------------------------------------------------------
                        // Only set this property if one of the following conditions is true:
                        // a) It was not previously added or...
                        // b) The previously added property is not tagged as important or ...
                        // c) The previously added property is tagged as important but so is this new one.
                        var shouldSetProperty =
                            !finalProperties.TryGetValue(resolvedProperty.Name, out var previousProperty)
                            || !previousProperty.IsImportant
                            || resolvedProperty.IsImportant;

                        if (shouldSetProperty)
                        {
                            style.SetProperty(resolvedProperty);
                            finalProperties[resolvedProperty.Name] = resolvedProperty;
                        }
                    }
                }
        }

        /// <summary>
        /// The text a rule nested inside <paramref name="rule"/> resolves against. Its serialized
        /// selector will not do: the lexer resolves an escape while reading, so serializing it back
        /// yields a different selector -- <c>.a\:b</c> as <c>.a:b</c>, a class followed by an
        /// unknown pseudo-class -- which then fails to parse, and the nested rule is lost. The text
        /// a rule was read from keeps its escapes, and for a rule that was itself nested that is the
        /// resolved text <see cref="AttachSelectorText"/> gave it.
        /// </summary>
        private static string NestingBase(StyleRule rule)
        {
            var source = rule.Selector?.StylesheetText?.Text;
            return string.IsNullOrWhiteSpace(source) ? rule.SelectorText : source.Trim();
        }

        /// <summary>
        /// Records the text a nested rule's selector was resolved from. A rule read from the source
        /// carries one already; a nested rule has no source of its own, and without this a rule
        /// nested inside it would have nothing to resolve against but the lossy serialization.
        /// </summary>
        private static void AttachSelectorText(StyleRule rule, string text)
        {
            if (string.IsNullOrEmpty(text) || !(rule.Selector is StylesheetNode node)) return;

            var range = new TextRange(new TextPosition(1, 1, 1), new TextPosition(1, 1, text.Length));
            node.StylesheetText = new StylesheetText(range, new TextSource(text));
        }

        /// <summary>
        /// CSS Nesting: a group rule written inside a style rule's block -- <c>@media</c>,
        /// <c>@supports</c>, <c>@container</c> or <c>@starting-style</c>. Its declarations belong to
        /// an implicit style rule carrying the enclosing rule's own selector, so
        /// <c>.a { @media (...) { color: red } }</c> is <c>@media (...) { :is(.a) { color: red } }</c>
        /// (CSS Nesting 1 &#xA7;3). A nested <c>@scope</c> is the exception: only its start selector
        /// nests, and its block holds scoped rules. Returns true with <paramref name="token"/>
        /// advanced past the block, false for an at-rule that cannot appear here, leaving the caller to skip it.
        /// </summary>
        private bool TryCreateNestedConditionalRule(ref Token token)
        {
            var parent = _nodes.OfType<StyleRule>().FirstOrDefault();
            if (parent == null) return false;

            GroupingRule rule;

            if (token.Data.Isi(RuleNames.Media)) rule = new MediaRule(_parser);
            else if (token.Data.Isi(RuleNames.Supports)) rule = new SupportsRule(_parser);
            else if (token.Data.Isi(RuleNames.Container)) rule = new ContainerRule(_parser);
            else if (token.Data.Isi(RuleNames.StartingStyle)) rule = new StartingStyleRule(_parser);
            else if (token.Data.Isi(RuleNames.Scope)) rule = new ScopeRule(_parser);
            else return false;

            var start = token.Position;
            var parentSelector = NestingBase(parent);
            var valid = true;

            token = NextToken();
            _nodes.Push(rule);
            ParseComments(ref token);

            switch (rule)
            {
                case MediaRule mediaRule:
                    FillMediaList(mediaRule.Media, TokenType.CurlyBracketOpen, ref token);
                    break;
                case SupportsRule supportsRule:
                    valid = FillSupportsPrelude(supportsRule, ref token);
                    break;
                case ContainerRule containerRule:
                    FillContainerPrelude(containerRule, ref token);
                    break;
                case ScopeRule scopeRule:
                    // The start selector nests in the enclosing rule the way a nested selector does; the
                    // limit and the rules in the block are relative to the scoping root instead.
                    valid = scopeRule.SetPrelude(ReadRawPrelude(ref token));
                    if (valid && scopeRule.StartText != null) scopeRule.StartText = ResolveNestedSelector(scopeRule.StartText, parentSelector);
                    break;
            }

            ParseComments(ref token);

            if (token.Type != TokenType.CurlyBracketOpen || !valid)
            {
                _nodes.Pop();
                SkipDeclarations(token);
                token = NextToken();
                return true;
            }

            if (rule is ScopeRule)
            {
                var scopeEnd = FillRules(rule);
                _nodes.Pop();
                rule.StylesheetText = CreateView(start, scopeEnd);
                parent.AddNestedRule(rule);
                token = NextToken();
                return true;
            }

            var implicitText = ResolveNestedSelector("&", parentSelector);
            var implicitRule = new StyleRule(_parser) { Selector = _parser.ParseSelector(implicitText) };

            AttachSelectorText(implicitRule, implicitText);

            rule.AppendChild(implicitRule);

            // Pushed so that a style rule nested one level deeper resolves against the implicit
            // rule's selector, which is the enclosing rule's, rather than skipping a level.
            _nodes.Push(implicitRule);
            var end = FillDeclarations(implicitRule.Style);
            _nodes.Pop();
            _nodes.Pop();

            rule.StylesheetText = CreateView(start, end);
            parent.AddNestedRule(rule);
            token = NextToken();
            return true;
        }

        /// <summary>
        /// CSS Nesting ([CSS Nesting 1](https://www.w3.org/TR/css-nesting-1/)): if the current construct
        /// in a declaration block is a nested style rule (a selector prelude followed by a <c>{ }</c>
        /// block) rather than a declaration, parses it, resolves its selector against the enclosing rule,
        /// attaches it, and returns true (with <paramref name="token"/> advanced past the block). Returns
        /// false for an ordinary declaration, having rewound the lexer so the caller re-reads it.
        /// </summary>
        private bool TryCreateNestedRule(ref Token token)
        {
            // Raw source index just before the current (first) token — the construct start, captured by
            // NextToken. Faithful across \r\n normalization, unlike deriving it from token.Position.
            var preludeStart = _markBeforeLastToken;

            if (!IsNestedRuleAhead(ref token, out var braceStart))
                return false;

            CreateNestedStyleRule(preludeStart, braceStart);
            token = NextToken();
            return true;
        }

        /// <summary>
        /// Looks ahead (bracket-aware) to classify the construct starting at <paramref name="token"/> as a
        /// nested style rule vs a declaration: the first top-level <c>{</c> means a nested rule (the stream
        /// is left just after it and its source index returned via <paramref name="braceStart"/>); a
        /// top-level <c>;</c>/<c>}</c>/EOF means a declaration, in which case the lexer is rewound to the
        /// construct start and <paramref name="token"/> re-read so the unchanged declaration path re-lexes
        /// it in value mode (avoiding the <c>#</c> value-mode tokenization hazard a token buffer would hit).
        /// Custom properties (<c>--x</c>) are always declarations, even with a <c>{</c> in their value.
        /// Function tokens (e.g. <c>url(…)</c>) are opaque — the lexer already consumed their parentheses —
        /// so only bare round/square brackets contribute to depth.
        /// </summary>
        private bool IsNestedRuleAhead(ref Token token, out int braceStart)
        {
            braceStart = 0;

            if (token.Type == TokenType.Ident && token.Data is { } name &&
                name.StartsWith("--", StringComparison.Ordinal))
                return false;

            var rewindMark = _markBeforeLastToken;   // raw source index before `token` (the first token)
            var depth = 0;
            var scan = token;
            var scanStart = rewindMark;              // raw source index before `scan`

            while (scan.Type != TokenType.EndOfFile)
            {
                switch (scan.Type)
                {
                    case TokenType.RoundBracketOpen:
                    case TokenType.SquareBracketOpen:
                        depth++;
                        break;
                    case TokenType.RoundBracketClose:
                    case TokenType.SquareBracketClose:
                        if (depth > 0) depth--;
                        break;
                    case TokenType.CurlyBracketOpen when depth == 0:
                        braceStart = scanStart;
                        token = scan;
                        return true;
                    case TokenType.CurlyBracketClose when depth == 0:
                    case TokenType.Semicolon when depth == 0:
                        _lexer.RewindTo(rewindMark);
                        token = NextToken();
                        return false;
                }

                scanStart = _lexer.InsertionPoint;   // raw index just before the next token
                scan = _lexer.Get();
            }

            _lexer.RewindTo(rewindMark);
            token = NextToken();
            return false;
        }

        /// <summary>
        /// Builds a <see cref="StyleRule"/> from a nested prelude (source span
        /// <c>[preludeStart, braceStart)</c>) resolved against the enclosing rule's selector, consumes
        /// its declaration block from the live stream, and attaches it to the parent rule via
        /// <see cref="StyleRule.AddNestedRule"/>. The resolved selector is absolute (<c>&amp;</c> →
        /// <c>:is(parent)</c>) so the cascade/matcher need no nesting awareness.
        /// </summary>
        private void CreateNestedStyleRule(int preludeStart, int braceStart)
        {
            var parent = _nodes.OfType<StyleRule>().FirstOrDefault();
            var preludeText = _lexer.Source.Text.Substring(preludeStart, braceStart - preludeStart);

            var resolvedText = parent == null ? null : ResolveNestedSelector(preludeText, NestingBase(parent));
            var rule = CreateStyleRule(resolvedText);

            if (parent != null && rule != null) parent.AddNestedRule(rule);
        }

        /// <summary>
        /// A style rule written directly in a <c>@scope</c> block, or in a conditional rule inside
        /// one. Its selector is scoped rather than nested: <c>&amp;</c> is the scoping root at zero
        /// specificity, and a branch that starts with a combinator is relative to it. The rest is
        /// left as written, since which elements are in scope is decided when the rule is matched.
        /// </summary>
        private StyleRule CreateScopedStyleRule(int preludeStart, int braceStart)
        {
            var preludeText = _lexer.Source.Text.Substring(preludeStart, braceStart - preludeStart);
            return CreateStyleRule(ResolveScopedSelector(preludeText));
        }

        /// <summary>
        /// Builds a style rule from resolved selector text and consumes its declaration block from
        /// the live stream. The block is consumed either way, to keep the parser in sync; null comes
        /// back when the selector did not parse, and the rule is nobody's.
        /// </summary>
        private StyleRule CreateStyleRule(string resolvedText)
        {
            var selector = resolvedText == null ? null : _parser.ParseSelector(resolvedText);
            var rule = new StyleRule(_parser);

            if (selector != null)
            {
                rule.Selector = selector;

                // Before the block is read, since a rule nested inside it resolves against this text.
                AttachSelectorText(rule, resolvedText);
            }

            _nodes.Push(rule);
            FillDeclarations(rule.Style);
            _nodes.Pop();

            return selector == null ? null : rule;
        }

        /// <summary>
        /// Resolves a scoped selector's prelude per CSS Cascade 6: each <c>&amp;</c> becomes
        /// <c>:where(:scope)</c>, and a branch that is a relative selector (<c>&gt; img</c>) gets
        /// <c>:scope</c> in front. A branch with neither is left alone, as it is matched against
        /// elements in scope and needs no anchor.
        /// </summary>
        private static string ResolveScopedSelector(string prelude)
        {
            var branches = SplitSelectorList(prelude);

            for (var i = 0; i < branches.Count; i++)
            {
                var branch = SubstituteNestingSelector(branches[i].Trim(), ScopeRootSelector, out _);
                if (branch.Length > 0 && (branch[0] == '>' || branch[0] == '+' || branch[0] == '~')) branch = ":scope " + branch;
                branches[i] = branch;
            }

            return string.Join(", ", branches);
        }

        // Splits on the commas that are outside every bracket and string.
        private static List<string> SplitSelectorList(string text)
        {
            var parts = new List<string>();
            var start = 0;
            var depth = 0;
            var quote = '\0';

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') { if (depth > 0) depth--; }
                else if (c == ',' && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }

            parts.Add(text.Substring(start));
            return parts;
        }

        /// <summary>
        /// Resolves a nested selector's prelude text against its parent's selector text per CSS Nesting:
        /// each <c>&amp;</c> becomes <c>:is(parent)</c> (giving <c>&amp;</c> the parent's specificity); a
        /// prelude with no <c>&amp;</c> is made relative to the parent (<c>:is(parent) &lt;prelude&gt;</c>),
        /// which correctly yields both the implicit-descendant (<c>.b</c> → <c>:is(parent) .b</c>) and the
        /// leading-combinator (<c>&gt; .b</c> → <c>:is(parent) &gt; .b</c>) forms.
        /// </summary>
        private static string ResolveNestedSelector(string prelude, string parentText)
        {
            var parentIs = ":is(" + (string.IsNullOrEmpty(parentText) ? "*" : parentText) + ")";
            var resolved = SubstituteNestingSelector(prelude.Trim(), parentIs, out var hasNestingSelector);
            return hasNestingSelector ? resolved : parentIs + " " + resolved;
        }

        /// <summary>
        /// Replaces every <c>&amp;</c> that is a nesting selector with <paramref name="replacement"/>,
        /// token-aware: a <c>&amp;</c> inside a string or attribute value (<c>[data-x="a&amp;b"]</c>) is
        /// kept, and does not count as one. That is also what decides <paramref name="found"/>, so
        /// a prelude whose only <c>&amp;</c> is quoted still takes the implicit-descendant form.
        /// </summary>
        private static string SubstituteNestingSelector(string text, string replacement, out bool found)
        {
            var sb = Pool.NewStringBuilder();
            var hasNestingSelector = false;
            var quote = '\0';

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (quote != '\0')
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < text.Length)
                        sb.Append(text[++i]);   // escaped char inside a string — copy verbatim
                    else if (c == quote)
                        quote = '\0';
                    continue;
                }

                switch (c)
                {
                    case '"':
                    case '\'':
                        quote = c;
                        sb.Append(c);
                        break;
                    case '&':
                        hasNestingSelector = true;
                        sb.Append(replacement);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            found = hasNestingSelector;
            return sb.ToPool();
        }

        private static readonly Func<string, Property> RawDeclaration = name => new UnknownProperty(name);

        public Property CreateDeclarationWith(Func<string, Property> createProperty, ref Token token)
        {
            var property = default(Property);

            var sb = Pool.NewStringBuilder();
            var start = token.Position;

            while (token.IsDeclarationName())
            {
                sb.Append(token.ToValue());
                token = NextToken();
            }

            var propertyName = sb.ToPool();

            if (propertyName.Length > 0)
            {
                property = createProperty(propertyName);

                if (property == null && _parser.Options.IncludeUnknownDeclarations)
                {
                    property = new UnknownProperty(propertyName);
                }

                if (property == null)
                    RaiseErrorOccurred(ParseError.UnknownDeclarationName, start);
                else
                    _nodes.Push(property);

                ParseComments(ref token);

                if (token.Type == TokenType.Colon)
                {
                    var value = CreateValue(TokenType.CurlyBracketClose, ref token, out var important);

                    if (value == null)
                        RaiseErrorOccurred(ParseError.ValueMissing, token.Position);
                    else if (property != null)
                    {
                        if(property.TrySetValue(value))
                            property.IsImportant = important;
                        else if(_parser.Options.AllowInvalidValues)
                        {
                            _nodes.Pop();

                            property = new UnknownProperty(propertyName);
                            property.TrySetValue(value);
                            property.IsImportant = important;
                            _nodes.Push(property);
                        }
                    }
                        

                    ParseComments(ref token);
                }
                else
                {
                    RaiseErrorOccurred(ParseError.ColonMissing, token.Position);
                }

                JumpToDeclEnd(ref token);

                if (property != null) _nodes.Pop();
            }
            else if (token.Type != TokenType.EndOfFile)
            {
                RaiseErrorOccurred(ParseError.IdentExpected, start);
                JumpToDeclEnd(ref token);
            }

            if (token.Type == TokenType.Semicolon) token = NextToken();

            return property;
        }

        public Property CreateDeclaration(ref Token token)
        {
            ParseComments(ref token);
            return CreateDeclarationWith(PropertyFactory.Instance.Create, ref token);
        }

        public Medium CreateMedium(ref Token token)
        {
            var medium = new Medium();
            ParseComments(ref token);

            if (token.Type == TokenType.Ident)
            {
                var identifier = token.Data;

                if (identifier.Isi(Keywords.Not))
                {
                    medium.IsInverse = true;
                    token = NextToken();
                    ParseComments(ref token);
                }
                else if (identifier.Isi(Keywords.Only))
                {
                    medium.IsExclusive = true;
                    token = NextToken();
                    ParseComments(ref token);
                }
            }

            if (token.Type == TokenType.Ident)
            {
                medium.Type = token.Data;
                token = NextToken();
                ParseComments(ref token);

                if (token.Type != TokenType.Ident || !token.Data.Isi(Keywords.And)) return medium;

                token = NextToken();
                ParseComments(ref token);
            }

            do
            {
                if (token.Type != TokenType.RoundBracketOpen) return null;

                token = NextToken();
                ParseComments(ref token);
                var feature = CreateFeature(ref token);

                if (feature != null) medium.AppendChild(feature);

                if (token.Type != TokenType.RoundBracketClose) return null;

                token = NextToken();
                ParseComments(ref token);

                if (feature == null) return null;

                if (token.Type != TokenType.Ident || !token.Data.Isi(Keywords.And)) break;

                token = NextToken();
                ParseComments(ref token);
            } while (token.Type != TokenType.EndOfFile);

            return medium;
        }

        private void JumpToEnd(ref Token current)
        {
            while (current.IsNot(TokenType.EndOfFile, TokenType.Semicolon)) current = NextToken();
        }

        private void MoveToRuleEnd(ref Token current)
        {
            var scopes = 0;

            while (current.Type != TokenType.EndOfFile)
            {
                if (current.Type == TokenType.CurlyBracketOpen)
                    scopes++;
                else if (current.Type == TokenType.CurlyBracketClose) scopes--;

                if (scopes <= 0 && current.Is(TokenType.CurlyBracketClose, TokenType.Semicolon)) break;

                current = NextToken();
            }
        }

        private void JumpToArgEnd(ref Token current)
        {
            var arguments = 0;

            while (current.Type != TokenType.EndOfFile)
            {
                if (current.Type == TokenType.RoundBracketOpen)
                    arguments++;
                else if (arguments <= 0 && current.Type == TokenType.RoundBracketClose)
                    break;
                else if (current.Type == TokenType.RoundBracketClose) arguments--;

                current = NextToken();
            }
        }

        private void JumpToDeclEnd(ref Token current)
        {
            var scopes = 0;

            while (current.Type != TokenType.EndOfFile)
            {
                if (current.Type == TokenType.CurlyBracketOpen)
                    scopes++;
                else if (scopes <= 0 && current.Is(TokenType.CurlyBracketClose, TokenType.Semicolon))
                    break;
                else if (current.Type == TokenType.CurlyBracketClose) scopes--;

                current = NextToken();
            }
        }

        private Token NextToken()
        {
            _markBeforeLastToken = _lexer.InsertionPoint;
            return _lexer.Get();
        }

        private StylesheetText CreateView(TextPosition start, TextPosition end)
        {
            var range = new TextRange(start, end);
            return new StylesheetText(range, _lexer.Source);
        }

        private void ParseComments(ref Token token)
        {
            var preserveComments = _parser.Options.PreserveComments;

            while (token.Type == TokenType.Whitespace || token.Type == TokenType.Comment ||
                   token.Type == TokenType.Cdc || token.Type == TokenType.Cdo)
            {
                if (preserveComments && token.Type == TokenType.Comment)
                {
                    var current = _nodes.Peek();
                    var comment = new Comment(token.Data);
                    var start = token.Position;
                    var end = start.After(token.ToValue());
                    comment.StylesheetText = CreateView(start, end);
                    current.AppendChild(comment);
                }

                token = NextToken();
            }
        }

        private Rule SkipDeclarations(Token token)
        {
            RaiseErrorOccurred(ParseError.InvalidToken, token.Position);
            MoveToRuleEnd(ref token);
            return default;
        }

        private void RaiseErrorOccurred(ParseError code, TextPosition position)
        {
            _lexer.RaiseErrorOccurred(code, position);
        }

        private IConditionFunction AggregateCondition(ref Token token)
        {
            var condition = ExtractCondition(ref token);

            if (condition == null) return null;

            ParseComments(ref token);
            var conjunction = token.Data;
            var creator = conjunction.GetCreator();

            if (creator != null)
            {
                token = NextToken();
                ParseComments(ref token);
                var conditions = MultipleConditions(condition, conjunction, ref token);
                condition = creator.Invoke(conditions);
            }

            return condition;
        }

        private IConditionFunction ExtractCondition(ref Token token)
        {
            if (token.Type == TokenType.RoundBracketOpen)
            {
                token = NextToken();
                ParseComments(ref token);
                var condition = AggregateCondition(ref token);

                if (condition != null)
                {
                    var group = new GroupCondition
                    {
                        Content = condition
                    };
                    condition = group;
                }
                else if (token.Type == TokenType.Ident)
                {
                    condition = DeclarationCondition(ref token);
                }

                if (token.Type != TokenType.RoundBracketClose) return condition;
                token = NextToken();
                ParseComments(ref token);

                return condition;
            }

            if (token.Data.Isi(Keywords.Not))
            {
                var condition = new NotCondition();
                token = NextToken();
                ParseComments(ref token);
                condition.Content = ExtractCondition(ref token);
                return condition;
            }

            return null;
        }

        private IConditionFunction DeclarationCondition(ref Token token)
        {
            var property = PropertyFactory.Instance.Create(token.Data) ?? new UnknownProperty(token.Data);
            var declaration = default(DeclarationCondition);
            token = NextToken();
            ParseComments(ref token);

            if (token.Type != TokenType.Colon) return null;

            var result = CreateValue(TokenType.RoundBracketClose, ref token, out var important);
            property.IsImportant = important;

            if (result != null) declaration = new DeclarationCondition(property, result);

            return declaration;
        }

        private List<IConditionFunction> MultipleConditions(IConditionFunction condition, string connector, ref Token token)
        {
            var list = new List<IConditionFunction>();
            ParseComments(ref token);
            list.Add(condition);

            while (token.Type != TokenType.EndOfFile)
            {
                condition = ExtractCondition(ref token);

                if (condition == null) break;

                list.Add(condition);

                if (!token.Data.Isi(connector)) break;

                token = NextToken();
                ParseComments(ref token);
            }

            return list;
        }

        private void FillFunctions(Action<DocumentFunction> add, ref Token token)
        {
            do
            {
                var function = token.ToDocumentFunction();

                if (function == null) break;

                token = NextToken();
                ParseComments(ref token);
                add(function);

                if (token.Type != TokenType.Comma) break;

                token = NextToken();
                ParseComments(ref token);
            } while (token.Type != TokenType.EndOfFile);
        }

        private TextPosition FillKeyframeRules(KeyframesRule parentRule)
        {
            var token = NextToken();
            ParseComments(ref token);

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketClose))
            {
                var rule = CreateKeyframeRule(token);
                token = NextToken();
                ParseComments(ref token);
                parentRule.Rules.Add(rule);
            }

            return token.Position;
        }

        private TextPosition FillDeclarations(DeclarationRule rule, Func<string, Property> createProperty)
        {
            var token = NextToken();
            ParseComments(ref token);

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketClose))
            {
                var property = CreateDeclarationWith(createProperty, ref token);

                if (property is {HasValue: true})
                {
                    rule.SetProperty(property);
                }

                ParseComments(ref token);
            }

            return token.Position;
        }

        private TextPosition FillRules(GroupingRule group)
        {
            var token = NextToken();
            ParseComments(ref token);

            StyleRule implicitRule = null;
            Dictionary<string, IProperty> implicitProperties = null;

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketClose))
            {
                // "Consume a block's contents" discards a <semicolon-token> outright, exactly as it does a
                // <whitespace-token> (CSS Syntax 3 5.5.5). Passing it on to CreateStyle instead feeds it to
                // SelectorConstructor, which has no recovery for an unexpected semicolon: it folds the token
                // into the next rule's selector and invalidates it, and because the run-on rule then keeps
                // consuming, the block's own closing brace is swallowed too and the following sibling rule
                // is lost with it.
                //
                // Note this is deliberately NOT done in CreateRules: "consume a stylesheet's contents"
                // (5.5.1) has no <semicolon-token> case, so a stray semicolon there falls to "anything else"
                // and is consumed into the next qualified rule's prelude, invalidating it. Only a block
                // passes <semicolon-token> as the stop token to "consume a qualified rule" (5.5.3).
                if (token.Type == TokenType.Semicolon)
                {
                    token = NextToken();
                    ParseComments(ref token);
                    continue;
                }

                // In a @scope block a style rule is scoped, not nested, and a declaration on its own
                // styles the scoping root (CSS Cascade 6). A run of declarations shares one implicit rule.
                if (token.Type != TokenType.AtKeyword && InScopeBlock)
                {
                    var preludeStart = _markBeforeLastToken;

                    if (IsNestedRuleAhead(ref token, out var braceStart))
                    {
                        implicitRule = null;
                        group.Rules.Add(CreateScopedStyleRule(preludeStart, braceStart));
                    }
                    else
                    {
                        if (implicitRule == null)
                        {
                            implicitRule = new StyleRule(_parser) { Selector = _parser.ParseSelector(ScopeRootSelector) };
                            AttachSelectorText(implicitRule, ScopeRootSelector);
                            implicitProperties = new Dictionary<string, IProperty>(StringComparer.OrdinalIgnoreCase);
                            group.Rules.Add(implicitRule);
                        }

                        FillDeclaration(implicitRule.Style, implicitProperties, ref token);
                        ParseComments(ref token);
                        continue;
                    }
                }
                else
                {
                    implicitRule = null;
                    group.Rules.Add(CreateRule(token));
                }

                token = NextToken();
                ParseComments(ref token);
            }

            return token.Position;
        }

        /// <summary>
        /// Whether the rules being read belong to a <c>@scope</c> block rather than to a style rule:
        /// the nearest of the two on the stack is the scope, however many conditional rules are between.
        /// </summary>
        private bool InScopeBlock => _nodes.FirstOrDefault(node => node is StyleRule || node is ScopeRule) is ScopeRule;

        /// <summary>The scoping root at zero specificity, which is what <c>&amp;</c> and a bare declaration mean in a <c>@scope</c> block.</summary>
        private const string ScopeRootSelector = ":where(:scope)";

        private void FillMediaList(MediaList list, TokenType end, ref Token token)
        {
            _nodes.Push(list);

            if (token.Type != end)
            {
                while (token.Type != TokenType.EndOfFile)
                {
                    var medium = CreateMedium(ref token);

                    if (medium != null) list.AppendChild(medium);

                    if (token.Type != TokenType.Comma) break;

                    token = NextToken();
                    ParseComments(ref token);
                }

                if (token.Type != end || list.Length == 0)
                {
                    list.Clear();
                    list.AppendChild(new Medium
                    {
                        IsInverse = true,
                        Type = Keywords.All
                    });
                }
            }

            _nodes.Pop();
        }

        private ISelector CreateSelector(ref Token token)
        {
            var selector = _parser.GetSelectorCreator();
            var start = token.Position;

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketOpen, TokenType.CurlyBracketClose))
            {
                selector.Apply(token);
                token = NextToken();
            }

            var selectorIsValid = selector.IsValid;
            var result = selector.ToPool();

            if (result is StylesheetNode node)
            {
                var end = token.Position.Shift(-1);
                node.StylesheetText = CreateView(start, end);
            }

            if (!selectorIsValid && !_parser.Options.AllowInvalidValues)
            {
                RaiseErrorOccurred(ParseError.InvalidSelector, start);
                result = null;
            }

            return result;
        }

        private ISelector CreateMarginSelector(ref Token token)
        {
            var selector = _parser.GetSelectorCreator();
            var start = token.Position;

            while (token.IsNot(TokenType.EndOfFile, TokenType.CurlyBracketOpen, TokenType.CurlyBracketClose))
            {
                selector.Apply(token);
                token = NextToken();
            }

            var selectorIsValid = selector.IsValid;
            var result = selector.ToPool();

            if (result is StylesheetNode node)
            {
                var end = token.Position.Shift(-1);
                node.StylesheetText = CreateView(start, end);
            }

            if (!selectorIsValid && !_parser.Options.AllowInvalidValues)
            {
                RaiseErrorOccurred(ParseError.InvalidSelector, start);
                result = null;
            }

            return result;
        }

        private TokenValue CreateValue(TokenType closing, ref Token token, out bool important)
        {
            var value = Pool.NewValueBuilder();
            _lexer.IsInValue = true;
            token = NextToken();
            var start = token.Position;

            while (token.IsNot(TokenType.EndOfFile, TokenType.Semicolon, closing))
            {
                value.Apply(token);
                token = NextToken();
            }

            important = value.IsImportant;
            _lexer.IsInValue = false;
            var valueIsValid = value.IsValid;
            var result = value.ToPool();

            //var node = result as StylesheetNode;
            var node = (StylesheetNode) result;

            if (node != null)
            {
                var end = token.Position.Shift(-1);
                node.StylesheetText = CreateView(start, end);
            }

            if (!valueIsValid && !_parser.Options.AllowInvalidValues)
            {
                RaiseErrorOccurred(ParseError.InvalidValue, start);
                result = null;
            }

            return result;
        }

        private string GetRuleName(ref Token token)
        {
            var name = string.Empty;

            if (token.Type == TokenType.Ident)
            {
                name = token.Data;
                token = NextToken();
            }

            return name;
        }

        private MediaFeature CreateFeature(ref Token token)
        {
            if (token.Type == TokenType.Ident)
            {
                var start = token.Position;
                var val = TokenValue.Empty;
                var feature = _parser.Options.AllowInvalidConstraints
                    ? new UnknownMediaFeature(token.Data)
                    : MediaFeatureFactory.Instance.Create(token.Data);

                token = NextToken();
                ParseComments(ref token);
                var tokenDelimiter = TokenType.Colon;
                if (token.Type == TokenType.Colon ||
                    token.Type == TokenType.GreaterThan || token.Type == TokenType.LessThan ||
                    token.Type == TokenType.GreaterThanOrEqual || token.Type == TokenType.LessThanOrEqual)
                {
                    tokenDelimiter = token.Type;
                    var value = Pool.NewValueBuilder();
                    token = NextToken();

                    while (token.IsNot(TokenType.RoundBracketClose, TokenType.EndOfFile) || !value.IsReady)
                    {
                        value.Apply(token);
                        token = NextToken();
                    }

                    val = value.ToPool();
                }
                else if (token.Type == TokenType.EndOfFile)
                {
                    return null;
                }

                if (feature != null && feature.TrySetValue(val, tokenDelimiter))
                {
                    if (feature is StylesheetNode node)
                    {
                        var end = token.Position.Shift(-1);
                        node.StylesheetText = CreateView(start, end);
                    }

                    return feature;
                }
            }
            else
            {
                JumpToArgEnd(ref token);
            }

            return null;
        }
    }
}