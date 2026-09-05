using System.IO;
using System.Linq;

namespace ExCSS
{
    internal sealed class ContainerRule : ConditionRule, IContainerRule
    {
        private string _condition;

        internal ContainerRule(StylesheetParser parser) : base(RuleType.Container, parser)
        {
            AppendChild(new MediaList(parser));
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            var name = "@container";
            if (!string.IsNullOrEmpty(Name))
                name = $"{name} {Name}";
            writer.Write(formatter.Rule(name, ConditionText, rules));
        }

        /// <summary>
        /// The condition read as a media list, for the size features the two grammars share. Empty
        /// for a range (<c>(width > 400px)</c>) or <c>style()</c> query the media grammar cannot hold;
        /// <see cref="ConditionText"/> has it either way.
        /// </summary>
        public MediaList Media => Children.OfType<MediaList>().FirstOrDefault();

        public string Name { get; set; }

        /// <summary>The condition as written, since a container query is not a media query.</summary>
        public string ConditionText
        {
            get => _condition ?? Media.MediaText;
            set
            {
                _condition = value;
                Media.Clear();

                try
                {
                    Media.MediaText = value ?? string.Empty;
                }
                catch (ParseException)
                {
                    Media.Clear();
                }
            }
        }
    }
}
