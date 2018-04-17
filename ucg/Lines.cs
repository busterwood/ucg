using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using static System.StringComparison;

namespace BusterWood.UniCodeGen
{
    class Context
    {
        public TextWriter Output { get; set; }
        public bool TemplateMode { get; set; } = true;
        public bool IsLast { get; set; }
    }

    class LineReader : IEnumerable<Line>
    {
        const string Tab = "    ";
        readonly TextReader _reader;
        readonly Context _context;
        private readonly string _scriptName;

        public LineReader(string scriptName, TextReader reader, Context context)
        {
            _reader = reader;
            _context = context;
            _scriptName = scriptName;
        }

        public IEnumerator<Line> GetEnumerator()
        {
            int lineNumber = 0;
            for(;;)
            {
                var line = _reader.ReadLine();
                if (line == null)
                    break;
                lineNumber++;
                yield return Create(line.Replace("\t", Tab), lineNumber);
            }
        }

        private Line Create(string line, int number)
        {
            if (!_context.TemplateMode || IsScript(line))
                return CreateScriptLine(line, number);
            else
                return new TemplateLine(line, number);
        }

        private bool IsScript(string line) => line.Length > 0 && line[0] == '.';

        private Line CreateScriptLine(string line, int number)
        {
            var firstWord = "." + FirstWord(line);

            if (OutputLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new OutputLine(line, number);

            if (IncludeLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new IncludeLine(line, number);

            if (ForEachLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new ForEachLine(line, number);

            if (EndForLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new EndForLine(line, number);

            if (CommentLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new CommentLine(line, number);

            if (TemplateModeLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
            {
                var tm = new TemplateModeLine(line, number);
                _context.TemplateMode = tm.On; // changes how lines parse
                return tm;
            }

            throw new ScriptException($"{firstWord} is not a known script command on line {number} of {_scriptName}: '{line}'");
        }

        private static string FirstWord(string line)
        {
            var start = 0;
            while (start < line.Length && line[start] == '.')
                start++;
            while (start < line.Length && char.IsWhiteSpace(line[start]))
                start++;

            var end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
                end++;

            return line.Substring(start, end - start);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A line of a a script</summary>
    abstract class Line
    {
        public string Text { get; set; }
        public int Number { get; set; }

        protected Line(string line, int number)
        {
            Text = line;
            Number = number;
        }

        public override string ToString() => Text;

        public abstract void Execute(XElement model, Context ctx);

        // because we support embedded variable expansion we need to recursively expand
        protected static string ExpandVars(string text, XElement model, Context ctx)
        {
            var sb = new StringBuilder();
            var r = new StringReader(text);
            for(;;)
            {
                var cur = r.Read();
                if (cur < 0)
                    break;
                var ch = (char)cur;
                if (ch == '$' && r.Peek() == '(')
                    sb.Append(RecursivelyExpand(r, model, ctx));
                else
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>We just read $( so the next part if the expression with a possible embedded expression</summary>
        private static string RecursivelyExpand(StringReader r, XElement model, Context ctx)
        {
            var expression = new StringBuilder();
            r.Read(); // consume the opening bracket that we peeked
            int openBrackets = 1;
            while (openBrackets > 0)
            {
                var cur = r.Read();
                if (cur < 0)
                    break;
                var ch = (char)cur;
                if (ch == '$' && r.Peek() == '(')
                {
                    expression.Append(RecursivelyExpand(r, model, ctx));
                }
                else if (ch == '(')
                {
                    openBrackets++;
                    expression.Append(ch);
                }
                else if (ch == ')')
                {
                    openBrackets--;
                    if (openBrackets > 0)
                        expression.Append(ch);
                }
                else
                    expression.Append(ch);
            }
            return Evaluate(expression.ToString(), model, ctx);
        }

        private static string Evaluate(string variable, XElement model, Context ctx)
        {
            string changeCase = CaseSuffix(ref variable);
            string value = FindValue(variable, model, ctx);
            return ChangeCase(changeCase, value);
        }

        private static string CaseSuffix(ref string variable)
        {
            int colon = variable.LastIndexOf(':');
            if (colon < 0)
                return "";
            var result = variable.Substring(colon + 1);
            variable = variable.Substring(0, colon);
            return result;
        }

        private static string FindValue(string variable, XElement model, Context ctx)
        {
            // allow for lists that need delimiters between, e.g. 1,2,3
            if (variable.StartsWith('"') && variable.EndsWith('"'))
                return ctx.IsLast ? "" : variable.Trim('"');

            int idx = variable.IndexOf("??");
            string found;
            if (idx > 0)
            {
                var left = variable.Substring(0, idx);
                var right = variable.Substring(idx + "??".Length);
                found = XPathAttrValue(model, left);
                if (string.IsNullOrEmpty(found))
                    found = XPathAttrValue(model, right);
            }
            else
            {
                found = XPathAttrValue(model, variable);
            }
            if (found != null)
                return found.ToString();

            throw new ScriptException($"Cannot find model attribute called '{variable}'");
        }

        private static string XPathAttrValue(XElement model, string xpath) => (string)model.XPathEvaluate("string(" + xpath + ")");

        private static string ChangeCase(string changeCase, string value)
        {
            switch (changeCase.ToLower())
            {
                case "u": // UPPER CASE
                    return value.ToUpper();
                case "l": // lower case
                    return value.ToLower();
                case "p": // PascalCase
                    return Strings.PascalCase(value);
                case "c": // camelCase
                    return Strings.CamelCase(value);
                case "sql": // SQL_CASE
                    return Strings.SqlCase(value);
                default:
                    return value;
            }
        }
    }

    /// <summary>Line does not start with "." but may contain text substitutions</summary>
    class TemplateLine : Line
    {
        public TemplateLine(string line, int number) : base(line, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
            var txt = ExpandVars(Text, model, ctx);
            ctx.Output.WriteLine(txt);
        }
    }

    /// <summary>A line of script starting with "."</summary>
    abstract class ScriptLine : Line
    {
        public ScriptLine(string line, int number) : base(line, number)
        {
        }

        protected static string Quoted(string text)
        {
            var start = text.IndexOf('"');
            if (start < 0) return null;
            var end = text.IndexOf('"', start + 1);
            if (end < 0) return null;
            return text.Substring(start + 1, end - start - 1);
        }
    }

    class CommentLine : ScriptLine
    {
        public const string Keyword = ".//";

        public CommentLine(string text, int number) : base(text, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>Set the output file</summary>
    class OutputLine : ScriptLine
    {
        public const string Keyword = ".output";
        readonly string _quoted;

        public OutputLine(string line, int number) : base(line, number)
        {
            _quoted = Quoted(line);
            if (_quoted == null)
                throw new ScriptException($"{Keyword} must be followed by a double quoted string: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            var newFilename = ExpandVars(_quoted, model, ctx);
            Std.Info($"Output is '{newFilename}'");
            var old = ctx.Output;
            ctx.Output = new StreamWriter(newFilename);
            old.Flush();
            old.Close();
        }
    }

    /// <summary>Include another script for this model</summary>
    class IncludeLine : ScriptLine
    {
        public const string Keyword = ".include";
        readonly string _quoted;

        public IncludeLine(string line, int number) : base(line, number)
        {
            _quoted = Quoted(line);
            if (_quoted == null)
                throw new ScriptException($"{Keyword} must be followed by a double quoted string: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            var scriptPath = ExpandVars(_quoted, model, ctx);
            Scripts.Run(scriptPath, model, ctx);
        }
    }
    
    /// <summary>Repeat part of the script for each child element of the model</summary>
    class ForEachLine : ScriptLine
    {
        public const string Keyword = ".foreach";

        readonly string _path;

        public List<Line> Body;

        public ForEachLine(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf("foreach", 0);
            _path = line.Substring(idx + "foreach".Length).Trim();
            //support multiple levels?
            if (string.IsNullOrEmpty(_path))
                throw new ScriptException($"{Keyword} must be followed by child element name: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (Body == null)
                throw new ScriptException("Empty body of " + Keyword);
            var expanded = ExpandVars(_path, model, ctx);
            // using XPATH 1.0 but we want to support "distinct-values()" XPATH 2.0 function
            bool distinct = false;
            if (expanded.StartsWith("distinct-values("))
            {
                expanded = expanded.Substring("distinct-values(".Length);
                expanded = expanded.Substring(0, expanded.Length - 1); // last closing bracket
                distinct = true;
            }
            var childern = ((IEnumerable)model.XPathEvaluate(expanded)).OfType<XElement>().ToList();
            if (distinct)
                childern = childern.Distinct(new SameAttrbutesComparer()).ToList();

            var last = childern.LastOrDefault();
            foreach (var child in childern)
            {
                foreach (var l in Body)
                {
                    ctx.IsLast = child == last;
                    l.Execute(child, ctx);
                }
            }
        }
    }

    /// <summary>end of <see cref="ForEachLine"/></summary>
    class EndForLine : ScriptLine
    {
        public const string Keyword = ".endfor";

        public EndForLine(string line, int number) : base(line, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }
    
    /// <summary>Turns on or off <see cref="Context.TemplateMode"/></summary>
    class TemplateModeLine : ScriptLine
    {
        public const string Keyword = ".template";
        public readonly bool On;

        public TemplateModeLine(string line, int number) : base(line, number)
        {
            var bits = line.TrimStart('.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 2)
                throw new ScriptException($"{Keyword} must be followed by child element name: '{line}'");
            On = "on".Equals(bits[1], OrdinalIgnoreCase) || "true".Equals(bits[1], OrdinalIgnoreCase);
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    class SameAttrbutesComparer : IEqualityComparer<XElement>
    {
        public bool Equals(XElement x, XElement y)
        {
            // check the inner text
            string xtxt = ((string)x.XPathEvaluate("string(text())")).Trim();
            string ytxt = ((string)y.XPathEvaluate("string(text())")).Trim();
            if (!xtxt.Equals(ytxt))
                return false;

            // if first attribute differs then not the same - IGNORES other attributes
            var xa = x.Attributes().FirstOrDefault();
            var ya = y.Attributes().FirstOrDefault();
            return string.Equals(xa?.Name, ya?.Name) && string.Equals(xa?.Value, ya?.Value);
        }

        public int GetHashCode(XElement ele)
        {
            string txt = ((string)ele.XPathEvaluate("string(text())")).Trim();
            int hc = txt.GetHashCode();
            var a = ele.Attributes().FirstOrDefault();
            if (a != null)
            {
                hc ^= a.Name.GetHashCode();
                hc ^= a.Value.GetHashCode();
            }
            return hc;
        }
    }
}
