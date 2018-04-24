using System;
using System.Linq;

namespace BusterWood.UniCodeGen
{
    static class Strings
    {
        public static string[] SplitOnLast(this string text, char splitOn)
        {
            int idx = text.LastIndexOf(':');
            if (idx < 0)
                return new string[] { text, "" };
            var last = text.Substring(idx + 1);
            var first = text.Substring(0, idx);
            return new string[] { first, last };
        }

        /// <summary>
        /// Explain modifiers, e.g. "u,b"
        /// </summary>
        public static string ApplyFormatModifier(string value, string modifier, bool last=false)
        {
            var result = value;
            foreach (var ch in modifier)
            {
                switch (char.ToLower(ch))
                {
                    case 'u': // UPPER CASE
                        result = result.ToUpper();
                        break;
                    case 'l': // lower case
                        result = result.ToLower();
                        break;
                    case 't': // Title Case
                        result = TitleCase(result);
                        break;
                    case 'p': // PascalCase
                        result = PascalCase(result);
                        break;
                    case 'c': // camelCase
                        result = CamelCase(result);
                        break;
                    case '_': // underscore_separated
                        result = string.Join('_', result.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                        break;
                    case 'b':
                        if (!string.IsNullOrEmpty(result))
                            result = "(" + result + ")";
                        break;
                    case ',':
                        if (!string.IsNullOrEmpty(result))
                            result = "," + result;
                        break;
                    case '~':
                        if (last)
                            result = "";
                        break;
                    default:
                        break;// ignore others
                }
            }
            return result;
        }

        public static string TitleCase(string value)
        {
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(TitleCaseWord));
        }

        public static string PascalCase(string value)
        {
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("", words.Select(TitleCaseWord));
        }

        public static string CamelCase(string value)
        {
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.FirstOrDefault()?.ToLower() + string.Join("", words.Skip(1).Select(TitleCaseWord));
        }

        public static string SqlCase(string value)
        {
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("_", words.Select(wd => wd.ToUpper()));
        }

        static string TitleCaseWord(string word) => char.ToUpper(word[0]) + word.Substring(1).ToLower();
    }
}
