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
        readonly TextReader _reader;
        readonly Context _context;

        public LineReader(TextReader reader, Context context)
        {
            _reader = reader;
            _context = context;
        }

        public IEnumerator<Line> GetEnumerator()
        {
            for(;;)
            {
                var line = _reader.ReadLine();
                if (line == null)
                    break;
                yield return Create(line);
            }
        }

        private Line Create(string line)
        {
            if (!_context.TemplateMode || IsScript(line))
                return CreateScriptLine(line);
            else
                return new TemplateLine { Text = line };
        }

        private bool IsScript(string line) => line.Length > 0 && line[0] == '.';

        private Line CreateScriptLine(string line)
        {
            var firstWord = "." + FirstWord(line);

            if (OutputLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new OutputLine(line);

            if (IncludeLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new IncludeLine(line);

            if (ForEachLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new ForEachLine(line);

            if (EndForLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new EndForLine(line);

            if (CommentLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new CommentLine { Text=line };

            if (TemplateModeLine.Keyword.Equals(firstWord, OrdinalIgnoreCase))
            {
                var tm = new TemplateModeLine(line);
                _context.TemplateMode = tm.On; // changes how lines parse
                return tm;
            }

            throw new ScriptException($"{firstWord} is not a known script command on line '{line}'");
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

        public override string ToString() => Text;

        public abstract void Execute(XElement model, Context ctx);

        protected static string ExpandVars(string text, XElement model, Context ctx)
        {
            var sb = new StringBuilder();
            var startOfText = 0;
            while (startOfText < text.Length)
            {
                var startIdx = text.IndexOf("$(", startOfText);
                if (startIdx < 0)
                {
                    sb.Append(text.Substring(startOfText)); // no more variable to substitute
                    break;
                }
                var endIdx = text.IndexOf(")", startIdx);
                sb.Append(text.Substring(startOfText, startIdx - startOfText));
                var variable = text.Substring(startIdx + 2, endIdx - (startIdx + 2));
                sb.Append(Evaluate(variable, model, ctx));
                startOfText = endIdx + 1;
            }
            return sb.ToString();
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
                found = (string)model.XPathEvaluate("string(" + left + ")");
                if (string.IsNullOrEmpty(found))
                    found = (string)model.XPathEvaluate("string(" + right + ")");
            }
            else
            {
                found = (string)model.XPathEvaluate("string(" + variable + ")");
            }
            if (found != null)
                return found.ToString();

            throw new ScriptException($"Cannot find model attribute called '{variable}'");
        }

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
        public override void Execute(XElement model, Context ctx)
        {
            var txt = ExpandVars(Text, model, ctx);
            ctx.Output.WriteLine(txt);
        }
    }

    /// <summary>A line of script starting with "."</summary>
    abstract class ScriptLine : Line
    {
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
        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>Set the output file</summary>
    class OutputLine : ScriptLine
    {
        public const string Keyword = ".output";
        readonly string _quoted;

        public OutputLine(string line)
        {
            Text = line;
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

        public IncludeLine(string line)
        {
            Text = line;
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
    
    /// <summary>Repeat part of the script for eacg child element of the model</summary>
    class ForEachLine : ScriptLine
    {
        public const string Keyword = ".foreach";

        readonly string _path;

        public List<Line> Body;

        public ForEachLine(string line)
        {
            Text = line;
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

            var childern = ((IEnumerable)model.XPathEvaluate(_path)).OfType<XElement>().ToList();
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
        public const string Keyword = ".end";

        public EndForLine(string line)
        {
            Text = line;
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

        public TemplateModeLine(string line)
        {
            Text = line;
            var bits = line.TrimStart('.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 2)
                throw new ScriptException($"{Keyword} must be followed by child element name: '{line}'");
            On = "on".Equals(bits[1], OrdinalIgnoreCase) || "true".Equals(bits[1], OrdinalIgnoreCase);
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

}
