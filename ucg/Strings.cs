using System;
using System.Linq;

namespace BusterWood.UniCodeGen
{
    static class Strings
    {
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
