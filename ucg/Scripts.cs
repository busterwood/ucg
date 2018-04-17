using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace BusterWood.UniCodeGen
{
    static class Scripts
    {
        public static void Run(string scriptPath, XElement model, Context ctx)
        {
            string script = Load(scriptPath);
            RunScript(scriptPath, script, model, ctx);
            ctx.Output.Flush();
        }

        private static string Load(string scriptPath)
        {
            Std.Info($"Running script '{scriptPath}'");
            if (!File.Exists(scriptPath))
                throw new ScriptException($"Cannot file script '{scriptPath}'");

            return File.ReadAllText(scriptPath);
        }

        private static void RunScript(string scriptPath, string script, XElement model, Context ctx)
        {
            var parsed = Parse(scriptPath, script, ctx);
            Execute(model, ctx, parsed);
        }

        private static void Execute(XElement model, Context ctx, List<Line> parsed)
        {
            foreach (var l in parsed)
            {
                l.Execute(model, ctx);
            }
        }

        private static List<Line> Parse(string scriptPath, string script, Context ctx)
        {
            var lines = new LineReader(scriptPath, new StringReader(script), ctx).GetEnumerator();
            return ParseRecursive(lines, line => false);
        }

        private static List<Line> ParseRecursive(IEnumerator<Line> lines, Func<Line, bool> end)
        {
            var body = new List<Line>();
            while (lines.MoveNext() && !end(lines.Current))
            {
                body.Add(lines.Current);
                if (lines.Current is ForEachLine fe)
                    ParseForEachBody(fe, lines);
                else if (lines.Current is IfLine il)
                    ParseIfBody(il, lines);
            }
            return body;
        }

        private static void ParseForEachBody(ForEachLine fe, IEnumerator<Line> lines)
        {
            Func<Line, bool> end = line => line is EndForLine;
            var body = ParseRecursive(lines, end);
            if (end(lines.Current))
                fe.Body = body;
            else
                throw new ScriptException($"{ForEachLine.Keyword} on line {fe.Number} without matching {EndForLine.Keyword}");
        }

        private static void ParseIfBody(IfLine il, IEnumerator<Line> lines)
        {
            il.True = ParseRecursive(lines, line => line is EndIfLine || line is ElseLine);

            if (lines.Current is EndIfLine)
            {
                // if endif
                il.False = new List<Line>();
            }
            else if (lines.Current is ElseLine)
            {
                // if else endif
                il.False = ParseRecursive(lines, line => line is EndIfLine);
                if (!(lines.Current is EndIfLine))
                    throw new ScriptException($"{IfLine.Keyword} on line {il.Number} without matching {EndIfLine.Keyword}");
            }
        }

    }
}
