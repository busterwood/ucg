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
            bool Remove(char toRemove, ref string text)
            {
                int idx = text.IndexOf(toRemove);
                if (idx >= 0)
                {
                    text = text.Remove(idx, 1);
                    return true;
                }
                return false;
            }

            // tilde means return empty string when last
            if (Remove('~', ref modifier) && last)
                return ""; 

            // comma means prefix a comma when not empty
            if (Remove(',', ref modifier) && !string.IsNullOrEmpty(value))
                value = "," + value;

            // b means add brackets when not empty
            if (Remove('b', ref modifier) && !string.IsNullOrEmpty(value))
                value = "(" + value + ")";

            switch (modifier.ToLower())
            {
                case "u": // UPPER CASE
                    return value.ToUpper();
                case "l": // lower case
                    return value.ToLower();
                case "t": // Title Case
                    return TitleCase(value);
                case "p": // PascalCase
                    return PascalCase(value);
                case "c": // camelCase
                    return CamelCase(value);
                case "sql": // SQL_CASE
                    return SqlCase(value);
                default:
                    return value;
            }
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
