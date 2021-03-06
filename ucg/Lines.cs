﻿using System;
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

            if (firstWord.StartsWith(Comment.Keyword, OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line))
                return new Comment(line, number);
            if (Output.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Output(line, number);
            if (Include.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Include(line, number);
            if (Echo.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Echo(line, number);
            if (WriteModel.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new WriteModel(line, number);
            if (Load.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Load(line, number);
            if (Inherit.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Inherit(line, number);
            if (Merge.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Merge(line, number);
            if (Transform.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Transform(line, number);

            if (ForEach.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new ForEach(line, number);
            if (EndFor.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new EndFor(line, number);

            if (ForFiles.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new ForFiles(line, number);
            if (EndFiles.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new EndFiles(line, number);

            if (If.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new If(line, number);
            if (Else.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new Else(line, number);
            if (EndIf.Keyword.Equals(firstWord, OrdinalIgnoreCase))
                return new EndIf(line, number);

            if (Template.Keyword.Equals(firstWord, OrdinalIgnoreCase))
            {
                var tm = new Template(line, number);
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
                {
                    r.Read(); // consume the opening bracket that we peeked
                    sb.Append(RecursivelyExpand(r, model, ctx));
                }
                else
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>We just read $( so the next part if the expression with a possible embedded expression</summary>
        private static string RecursivelyExpand(StringReader r, XElement model, Context ctx)
        {
            var expression = new StringBuilder();
            int openBrackets = 1;
            while (openBrackets > 0)
            {
                var cur = r.Read();
                if (cur < 0)
                    break;
                var ch = (char)cur;
                if (ch == '$' && r.Peek() == '(')
                {
                    r.Read(); // consume the opening bracket that we peeked
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
            var bits = variable.SplitOnLast(':');
            string modifier = bits[1];
            string value = FindValue(bits[0], model, ctx);
            return Strings.ApplyFormatModifier(value, modifier, ctx.IsLast);
        }

        private static string FindValue(string variable, XElement model, Context ctx)
        {
            // return double quoted strings with quotes removed
            if (variable.StartsWith('"') && variable.EndsWith('"'))
                return variable.Trim('"');

            int idx = variable.IndexOf("??");
            string found;
            if (idx > 0)
            {
                // evaluate: left ?? right
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

    /// <summary>
    /// .//
    /// 
    /// ignore the rest of this line
    /// </summary>
    class Comment : ScriptLine
    {
        public const string Keyword = ".//";

        public Comment(string text, int number) : base(text, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>
    /// .output "file path"
    /// 
    /// Set the output file
    /// </summary>
    class Output : ScriptLine
    {
        public const string Keyword = ".output";
        readonly string _quoted;

        public Output(string line, int number) : base(line, number)
        {
            _quoted = Quoted(line);
            if (_quoted == null)
                throw new ScriptException($"{Keyword} must be followed by a double quoted string: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            var newFilename = ExpandVars(_quoted, model, ctx);
            EnsureFolderExists(newFilename);
            Std.Info($"Output is '{newFilename}'");
            var old = ctx.Output;
            ctx.Output = new StreamWriter(newFilename, append: false);
            old.Flush();
            old.Close();
        }

        private static void EnsureFolderExists(string newFilename)
        {
            var folder = Path.GetDirectoryName(newFilename);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }
    }

    /// <summary>
    /// .include "file path"
    /// 
    /// Include another script for this model
    /// </summary>
    class Include : ScriptLine
    {
        public const string Keyword = ".include";
        readonly string _quoted;

        public Include(string line, int number) : base(line, number)
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
    
    /// <summary>
    /// .foreach xpath-expression
    /// 
    /// Repeat part of the script for each child element of the model
    /// </summary>
    class ForEach : ScriptLine
    {
        public const string Keyword = ".foreach";

        readonly string _path;

        public List<Line> Body;

        public ForEach(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf("foreach", 0);
            _path = line.Substring(idx + "foreach".Length).Trim();
            if (string.IsNullOrEmpty(_path))
                throw new ScriptException($"{Keyword} must be followed by child element name: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (Body == null)
                throw new ScriptException("Empty body of " + Keyword);
            var expanded = ExpandVars(_path, model, ctx);

            // using XPATH 1.0 but we want to support "distinct-values()" XPATH 2.0 function
            bool distinct = DistinctValues(ref expanded);
            var childern = ((IEnumerable)model.XPathEvaluate(expanded)).OfType<XElement>().ToList();
            if (distinct)
                childern = childern.Distinct(new TextAndFirstAttributeEquality()).ToList();

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

        private static bool DistinctValues(ref string expanded)
        {
            if (expanded.StartsWith("distinct-values("))
            {
                expanded = expanded.Substring("distinct-values(".Length);
                expanded = expanded.Substring(0, expanded.Length - 1); // last closing bracket
                return true;
            }
            return false;
        }
    }

    /// <summary>end of <see cref="ForEach"/></summary>
    class EndFor : ScriptLine
    {
        public const string Keyword = ".endfor";

        public EndFor(string line, int number) : base(line, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>
    /// .forfiles "dir/*.ext"
    /// 
    /// Repeat part of the script for each file found.  Each file has 4 attributes:  path, name (excluding extension), extension, folder
    /// </summary>
    class ForFiles : ScriptLine
    {
        public const string Keyword = "." + keyword;
        const string keyword = "forfiles";

        readonly string _path;

        public List<Line> Body;

        public ForFiles(string line, int number) : base(line, number)
        {
            _path = Quoted(line);
            if (_path == null)
                throw new ScriptException($"{Keyword} must be followed by a double quoted string: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (Body == null)
                throw new ScriptException("Empty body of " + Keyword);

            var expanded = ExpandVars(_path, model, ctx);
            var files = Directory.GetFiles(_path);
            var last = files.Last();
            foreach (var fp in files)
            {
                XElement fileEle = new XElement("file", 
                    new XAttribute("name", Path.GetFileNameWithoutExtension(fp)),
                    new XAttribute("path", fp),
                    new XAttribute("extension", Path.GetExtension(fp)),
                    new XAttribute("folder", Path.GetDirectoryName(fp))
                );
                model.Add(fileEle);
                foreach (var l in Body)
                {
                    ctx.IsLast = fp == last;
                    l.Execute(model, ctx);
                }
                fileEle.Remove();
            }
        }
    }

    /// <summary>end of <see cref="ForFiles"/></summary>
    class EndFiles : ScriptLine
    {
        public const string Keyword = ".endfiles";

        public EndFiles(string line, int number) : base(line, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>
    /// .if xpath-expression
    /// 
    /// evaluates the xpath expression to see if it return either a non-empty string or a single element
    /// </summary>
    class If : ScriptLine
    {
        public const string Keyword = ".if";

        readonly string _path;

        public List<Line> True;
        public List<Line> False;

        public If(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf("if", 0);
            _path = line.Substring(idx + "if".Length).Trim();
            if (string.IsNullOrEmpty(_path))
                throw new ScriptException($"{Keyword} must be followed by child element name: '{line}'");
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (True == null)
                throw new ScriptException("Empty body of " + Keyword);
            bool result = Evaluate(model, ctx);
            foreach (var l in result ? True : False)
            {
                l.Execute(model, ctx);
            }
        }

        private bool Evaluate(XElement model, Context ctx)
        {
            var expanded = ExpandVars(_path, model, ctx);
            var selected = model.XPathEvaluate(expanded);
            if (selected is bool b)
                return b;
            else if (selected is string s)
                return !string.IsNullOrEmpty(s);
            else if (selected is IEnumerable e)
                return e.OfType<XElement>().FirstOrDefault() != null;
            else
                throw new ScriptException($"{Keyword} expression value is not a string or IEnumerable on line {Number}: '{Text}'");
        }
    }

    /// <summary>Else part of a <see cref="If"/></summary>
    class Else : ScriptLine
    {
        public const string Keyword = ".else";

        public Else(string line, int number) : base(line, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>end of <see cref="If"/></summary>
    class EndIf : ScriptLine
    {
        public const string Keyword = ".endif";

        public EndIf(string line, int number) : base(line, number)
        {
        }

        public override void Execute(XElement model, Context ctx)
        {
        }
    }

    /// <summary>
    /// .echo message
    /// 
    /// Writes to a message to StdErr
    /// </summary>
    class Echo : ScriptLine
    {
        public const string Keyword = "." + keywordNoDot;
        const string keywordNoDot = "echo";
        string rest;

        public Echo(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf(keywordNoDot, 0);
            rest = line.Substring(idx + keywordNoDot.Length).Trim();
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (string.IsNullOrEmpty(rest))
                Std.Info("");
            else
            {
                var expanded = ExpandVars(rest, model, ctx);
                Std.Info(expanded);
            }
        }
    }

    /// <summary>
    /// .writemodel [xpath]
    /// 
    /// outputs the model source XML, or the XML returned by the optional xpath expression
    /// </summary>
    class WriteModel : ScriptLine
    {
        public const string Keyword = "." + keywordNoDot;
        const string keywordNoDot = "writemodel";
        string xpath;

        public WriteModel(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf(keywordNoDot, 0);
            xpath = line.Substring(idx + keywordNoDot.Length).Trim();
        }

        public override void Execute(XElement model, Context ctx)
        {
            if (string.IsNullOrEmpty(xpath))
                ctx.Output.WriteLine(model);
            else
            {
                var expanded = ExpandVars(xpath, model, ctx);
                foreach (var ele in ((IEnumerable)model.XPathEvaluate(expanded)).OfType<XElement>())
                {
                    ctx.Output.WriteLine(ele);
                }
            }
        }
    }
    
    /// <summary>
    /// .load "filepath" xpath-expression
    /// 
    /// Adds the selected elements from the file to the current model element
    /// </summary>
    class Load : ScriptLine
    {
        public const string Keyword = "." + keywordNoDot;
        const string keywordNoDot = "load";
        string filePath;
        string xpath;

        public Load(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf(keywordNoDot, 0);
            var rest = line.Substring(idx + keywordNoDot.Length).Trim();
            filePath = Quoted(rest);
            int xpathIdx = rest.IndexOf(filePath) + filePath.Length + 1;
            xpath = rest.Substring(xpathIdx).Trim();
        }

        public override void Execute(XElement model, Context ctx)
        {
            var doc = XDocument.Load(filePath);
            var expanded = ExpandVars(xpath, doc.Root, ctx);
            foreach (var ele in ((IEnumerable)model.XPathEvaluate(expanded)).OfType<XElement>())
            {
                model.Add(ele); // automatic deep clone
            }
        }
    }

    abstract class MergeInheritBase : ScriptLine
    {
        protected string xpath;
        protected bool attributesOnly;

        public MergeInheritBase(string line, int number) : base(line, number)
        {
        }

        protected void Initialise(string keywordNoDot, string line)
        {
            var idx = line.IndexOf(keywordNoDot, 0);
            var rest = line.Substring(idx + keywordNoDot.Length).Trim();
            attributesOnly = rest.StartsWith("attributes ", OrdinalIgnoreCase);
            xpath = attributesOnly ? rest.Substring("attributes ".Length).TrimStart() : rest;
        }

        public override void Execute(XElement model, Context ctx)
        {
            var expanded = ExpandVars(xpath, model, ctx);
            var found = ((IEnumerable)model.XPathEvaluate(expanded)).OfType<XElement>().ToList();
            if (found.Count == 0)
                return;
            if (found.Count > 1)
                throw new ScriptException($"Expression returned more than one element on line {Number}");

            AddAttributes(model, ctx, found[0]);

            if (!attributesOnly)
                AddElements(model, ctx, found[0]);
        }

        protected abstract void AddAttributes(XElement model, Context ctx, XElement xElement);

        protected abstract void AddElements(XElement model, Context ctx, XElement xElement);
    }

    /// <summary>
    /// .merge xpath-expression
    /// 
    /// Updates or adds the attributes and child elements of the element matching the supplied XPATH expression
    /// </summary>
    /// <remarks>Different from <see cref="Inherit"/> in that existing attribute values are overwritten and all child and text elements are added</remarks>
    class Merge : MergeInheritBase
    {
        public const string Keyword = "." + keywordNoDot;
        const string keywordNoDot = "merge";

        public Merge(string line, int number) : base(line, number)
        {
            Initialise(keywordNoDot, line);
        }

        protected override void AddAttributes(XElement model, Context ctx, XElement ele)
        {
            foreach (var a in ele.Attributes())
            {
                var e = model.Attribute(a.Name);
                if (e != null)
                    e.Value = a.Value;
                else
                    model.Add(a);
            }
        }

        protected override void AddElements(XElement model, Context ctx, XElement ele)
        {
            foreach (var n in ele.Nodes())
            {
                model.Add(n); // elements and text nodes
            }
        }
    }

    /// <summary>
    /// .inherit xpath-expression
    /// 
    /// Adds missing attributes and child elements from the element matching the supplied XPATH expression.
    /// </summary>
    /// <remarks>Different from <see cref="Merge"/> in that only attributes and elements that don't already exist on the model are added</remarks>
    class Inherit : MergeInheritBase
    {
        public const string Keyword = "." + keywordNoDot;
        const string keywordNoDot = "inherit";

        public Inherit(string line, int number) : base(line, number)
        {
            Initialise(keywordNoDot, line);
        }

        protected override void AddAttributes(XElement model, Context ctx, XElement ele)
        {
            foreach (var a in ele.Attributes())
            {
                var e = model.Attribute(a.Name);
                if (e == null)
                    model.Add(a);
            }
        }

        protected override void AddElements(XElement model, Context ctx, XElement ele)
        {
            var existing = new HashSet<XElement>(new TextAndFirstAttributeEquality());
            existing.UnionWith(model.Elements());
            foreach (var e in ele.Elements().Where(e => !existing.Contains(e)))
            {
                model.Add(e);
                existing.Add(e);
            }
        }
    }

    /// <summary>
    /// .transform xpath-expression
    /// 
    /// Transforms attributes of the current model element into child elements.  The attributes transformed are determined by matching attribute names to the supplied XPATH expression.
    /// </summary>
    /// <remarks>The XPATH expression must return strings</remarks>
    class Transform : ScriptLine
    {
        public const string Keyword = "." + keywordNoDot;
        const string keywordNoDot = "transform";
        string xpath;

        public Transform(string line, int number) : base(line, number)
        {
            var idx = line.IndexOf(keywordNoDot, 0);
            xpath = line.Substring(idx + keywordNoDot.Length).Trim();
        }

        public override void Execute(XElement model, Context ctx)
        {
            var expanded = ExpandVars(xpath, model, ctx);
            var found = (IEnumerable)model.XPathEvaluate(expanded);
            foreach (var an in found.OfType<XAttribute>())
            {
                var nameNoSpaces = an.Value.Replace(" ", "");
                var attr = model.Attribute(nameNoSpaces);
                if (attr == null)
                    throw new ScriptException($"Cannot find an attribute called '{nameNoSpaces}' in {model} on line {Number}");
                var e = new XElement(attr.Name, attr.Value, new XAttribute("name", an.Value));
                model.Add(e);
                attr.Remove(); // remove attribute from model
            }
        }

    }

    /// <summary>
    /// .template [on|true]
    /// 
    /// Turns on or off <see cref="Context.TemplateMode"/>
    /// </summary>
    class Template : ScriptLine
    {
        public const string Keyword = ".template";
        public readonly bool On;

        public Template(string line, int number) : base(line, number)
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

    class TextAndFirstAttributeEquality : IEqualityComparer<XElement>
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
            XAttribute ya = y.Attributes().FirstOrDefault();
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
