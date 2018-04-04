using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BusterWood.UniCodeGen
{
    class Context
    {
        public TextWriter Output { get; set; }
    }

    class LineReader : IEnumerable<Line>
    {
        readonly TextReader reader;

        public LineReader(TextReader reader)
        {
            this.reader = reader;
        }

        public IEnumerator<Line> GetEnumerator()
        {
            for(;;)
            {
                var line = reader.ReadLine();
                if (line == null)
                    break;
                yield return Create(line);
            }
        }

        private static Line Create(string line)
        {
            if (IsScript(line))
                return CreateScriptLine(line);
            else
                return new TemplateLine { Text = line };
        }

        private static bool IsScript(string line) => line.Length > 0 && line[0] == '.';

        private static Line CreateScriptLine(string line)
        {
            var firstWord = "." + FirstWord(line, 1);
            if (OutputLine.Keyword.Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                return new OutputLine(line);
            if (IncludeLine.Keyword.Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                return new IncludeLine(line);
            if (ForEachLine.Keyword.Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                return new ForEachLine(line);
            if (EndForLine.Keyword.Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                return new EndForLine(line);

            throw new NotImplementedException();
        }

        private static string FirstWord(string line, int index)
        {
            var start = index;
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

        protected static string ExpandVars(string text, XElement model)
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
                sb.Append(Evaluate(variable, model));
                startOfText = endIdx + 1;
            }
            return sb.ToString();
        }

        private static string Evaluate(string variable, XElement model)
        {
            //TODO: evaluate dotted paths, e.g. page.title
            //TODO: change case of output via a format suffix?
            bool changeCase = true;
            if (variable.EndsWith(':'))
            {
                variable = variable.TrimEnd(':');
                changeCase = false;
            }

            var found = model.Attributes().FirstOrDefault(a => string.Equals(variable, a.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            if (found == null)
                throw new ScriptException($"Cannot find model attribute called '{variable}'");
            string value = found.Value;
            if (changeCase)
            {
                if (variable.All(char.IsUpper))
                    return value.ToUpper();
                if (variable.All(char.IsLower))
                    return value.ToLower();
                if (PascalCase(variable))
                    return char.ToUpper(value[0]) + value.Substring(1).ToLower();
            }
            return value;
        }

        private static bool PascalCase(string variable) => variable.Length > 2 && char.IsUpper(variable[0]) && variable.Skip(1).All(char.IsLower);
    }

    /// <summary>Line does not start with "." but may contain text substitutions</summary>
    class TemplateLine : Line
    {
        public override void Execute(XElement model, Context ctx)
        {
            var txt = ExpandVars(Text, model);
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
            var newFilename = ExpandVars(_quoted, model);
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
            var scriptPath = ExpandVars(_quoted, model);
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
            var bits = line.Substring(1).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            //support multiple levels?
            if (bits.Length != 2)
                throw new ScriptException($"{Keyword} must be followed by child element name: '{line}'");

            _path = bits[1]; // skip the keyword at index 0
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (Body == null)
                throw new ScriptException("Empty body of " + Keyword);

            foreach (var child in model.Elements().Where(e => string.Equals(_path, e.Name.LocalName, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var l in Body)
                {
                    l.Execute(child, ctx);
                }
            }
        }
    }

    /// <summary>end of <see cref="ForEachLine"/></summary>
    class EndForLine : ScriptLine
    {
        public const string Keyword = ".endfor";

        public EndForLine(string line)
        {
            Text = line;
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

}
