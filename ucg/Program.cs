using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ucg
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

                XDocument model = XDocument.Load(modelPath);
                Scripts.Run(scriptPath, model.Root, ctx);
                return 0;
            }
            catch (Exception ex)
            {
                Std.Error(ex.ToString());
                return 9;
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
