using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BusterWood.UniCodeGen
{
    class Program
    {
        static int Main(string[] argv)
        {
            var args = argv.ToList();
            var ctx = new Context { Output = Console.Out };

            var scriptPath = StringArg(args, "--script");
            if (string.IsNullOrEmpty(scriptPath))
            {
                Std.Error("script name/path must be supplied via the --script argument");
                return 1;
            }

            try
            {
                string modelPath = args.FirstOrDefault();
                if (string.IsNullOrEmpty(modelPath))
                {
                    Std.Error("Expected model file name/path to be supplied");
                    return 2;
                }
                args.RemoveAt(0);

                XDocument model = XDocument.Load(modelPath);
                var root = model.Root;
                root.Add(new XAttribute("model-path", modelPath));
                root.Add(new XAttribute("script-path", scriptPath));
                root.Add(new XAttribute("datetime-utc", DateTime.UtcNow.ToString("u")));
                MergeArgsAsAttributes(args, root); // extra arguments AFTER the model file are added as attributes to the root element
                Scripts.Run(scriptPath, root, ctx);
                return 0;
            }
            catch (Exception ex)
            {
                Std.Error(ex.ToString());
                return 9;
            }
        }

        private static void MergeArgsAsAttributes(List<string> args, XElement root)
        {
            while (args.Count > 0)
            {
                if (args[0].StartsWith("--"))
                {
                    if (args.Count == 1)
                        throw new ArgumentException($"Expected a value to follow command line argument '{args[0]}'");
                    string name = args[0].TrimStart('-');
                    string value = args[1];
                    var existing = root.Attribute(name);
                    if (existing != null)
                        existing.Value = value;
                    else
                        root.Add(new XAttribute(name, value));
                    args.RemoveAt(1);
                }
                args.RemoveAt(0);
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
}
