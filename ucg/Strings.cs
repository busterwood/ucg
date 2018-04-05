using System;
using System.Linq;

namespace BusterWood.UniCodeGen
{
    static class Strings
    {
        public static string PascalCase(string value)
        {
            return string.Join("", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(PascalCaseWord));
        }

        static string PascalCaseWord(string word) => word.Substring(0, 1).ToUpper() + word.Substring(1).ToLower();

        public static string CamelCase(string value)
        {
            var temp = PascalCase(value);
            return temp.Substring(0, 1).ToLower() + temp.Substring(1);
        }

        public static string SqlCase(string value)
        {
            return string.Join("_", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(wd => wd.ToUpper()));
        }
    }

}
