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

            var body = new List<Line>();
            while (lines.MoveNext())
            {
                body.Add(lines.Current);
                if (lines.Current is ForEachLine fe)
                    fe.Body = ParseForEachBody(lines);
            }
            return body;
        }

        private static List<Line> ParseForEachBody(IEnumerator<Line> lines)
        {
            var body = new List<Line>();
            while (lines.MoveNext())
            {
                if (lines.Current is EndForLine)
                    return body;
                body.Add(lines.Current);
                if (lines.Current is ForEachLine fe)
                    fe.Body = ParseForEachBody(lines);
            }
            throw new ScriptException(ForEachLine.Keyword + " without matching " + EndForLine.Keyword);
        }
    }
}
