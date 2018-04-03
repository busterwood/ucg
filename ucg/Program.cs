using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ucg
{
    class Program
    {
        static int Main(string[] argv)
        {
            var args = argv.ToList();
            var ctx = new Context { Output = Console.Out };

            try
            {
                string modelPath = args[0];
                if (args.Count == 0)
                {
                    Std.Error("Expected model file name/path to be supplied");
                    return 1;
                }

                XDocument model = XDocument.Load(modelPath);
                var scriptPath = model.Root.Attribute("script")?.Value;
                Scripts.RunDocument(scriptPath, model, ctx);
            }
            catch (Exception ex)
            {
                Std.Error(ex.ToString());
                return 2;
            }

            return 0;
        }

        private static void RunScript(string script, XElement model, Context ctx)
        {
            var sr = new StringReader(script);
            foreach (Line l in new LineReader(sr))
            {
                if (l is TemplateLine tl)
                {
                    ctx.Output.WriteLine(tl.Output(model));
                }
                else if (l is ScriptLine sl)
                {
                    sl.Execute(model, ctx);
                }
            }
        }

        static string StringArg(List<string> args, string argName, string @default = null)
        {
            int idx = args.IndexOf(argName);
            if (idx < 0)
                return @default;
            args.RemoveAt(idx);
            if (idx == args.Count)
                return null;
            var val = args[idx];
            args.RemoveAt(idx);
            return val;
        }
    }

    static class Scripts
    {
        public static void RunDocument(string scriptPath, XDocument model, Context ctx)
        {
            string script = Load(scriptPath);
            foreach (var child in model.Root.Descendants())
            {
                RunScript(script, child, ctx);
            }
            ctx.Output.Flush();
        }

        private static string Load(string scriptPath)
        {
            Std.Info($"Running script '{scriptPath}'");
            if (!File.Exists(scriptPath))
            {
                throw new ScriptException($"Cannot file script '{scriptPath}'");
            }
            return File.ReadAllText(scriptPath);
        }

        public static void Run(string scriptPath, XElement model, Context ctx)
        {
            string script = Load(scriptPath);
            RunScript(script, model, ctx);
            ctx.Output.Flush();
        }

        private static void RunScript(string script, XElement model, Context ctx)
        {
            var sr = new StringReader(script);
            foreach (Line l in new LineReader(sr))
            {
                if (l is TemplateLine tl)
                    ctx.Output.WriteLine(tl.Output(model));
                else if (l is ScriptLine sl)
                    sl.Execute(model, ctx);
                else
                    throw new NotImplementedException(l?.ToString());
            }
        }

    }

    static class Std
    {
        public static void Info(string txt)
        {
            var cc = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Error.WriteLine(txt);
            Console.ForegroundColor = cc;
        }

        public static void Error(string txt)
        {
            var cc = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(txt);
            Console.ForegroundColor = cc;
        }
    }

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
            if (line.StartsWith(".output"))
            {
                return new OutputLine(line);
            }
            if (line.StartsWith(".include"))
            {
                return new IncludeLine(line);
            }
            throw new NotImplementedException();
        }
        

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    abstract class Line
    {
        public string Text { get; set; }

        public override string ToString() => Text;

        protected static string Interpret(string text, XElement model)
        {
            var sb = new StringBuilder();
            var startOfText = 0;
            while (startOfText < text.Length)
            {
                var startIdx = text.IndexOf("$(", startOfText);
                if (startIdx < 0)
                {
                    sb.Append(text.Substring(startOfText));
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

            if (changeCase)
            {
                if (variable.All(char.IsUpper))
                    return found.Value.ToUpper();
                if (variable.All(char.IsLower))
                    return found.Value.ToLower();
                if (variable.Length > 2 && char.IsUpper(variable[0]) && variable.Skip(1).All(char.IsLower))
                    return char.ToUpper(found.Value[0]) + found.Value.Substring(1).ToLower();
            }
            return found.Value;
        }
    }

    /// <summary>Line does not start with "." but may contain text substitutions</summary>
    class TemplateLine : Line
    {
        public string Output(XElement model) => Interpret(Text, model);
    }

    /// <summary>A line of script starting with "."</summary>
    abstract class ScriptLine : Line
    {
        public abstract void Execute(XElement model, Context ctx);

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
        readonly string _quoted;

        public OutputLine(string line)
        {
            Text = line;
            _quoted = Quoted(line);
            if (_quoted == null)
                throw new ScriptException($".output must be followed by a double quoted string: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            var newFilename = Interpret(_quoted, model);
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
        readonly string _quoted;

        public IncludeLine(string line)
        {
            Text = line;
            _quoted = Quoted(line);
            if (_quoted == null)
                throw new ScriptException($".include must be followed by a double quoted string: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            var scriptPath = Interpret(_quoted, model);
            Scripts.Run(scriptPath, model, ctx);
        }
    }
}
