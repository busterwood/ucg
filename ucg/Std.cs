using System;

namespace BusterWood.UniCodeGen
{
    static class Std
    {
        public static void Info(string txt)
        {
            using (new Colour(ConsoleColor.Cyan))
               Console.Error.WriteLine(txt);
        }

        public static void Error(string txt)
        {
            using (new Colour(ConsoleColor.Red))
               Console.Error.WriteLine(txt);
        }

        struct Colour : IDisposable
        {
            readonly ConsoleColor old;

            public Colour(ConsoleColor newColour)
            {
                this.old = Console.ForegroundColor;
                Console.ForegroundColor = newColour;
            }

            public void Dispose()
            {
                Console.ForegroundColor = old;
            }
        }
    }
}
