using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KeyboardControl
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Keyboard keyboard = new Keyboard();
            await keyboard.Initialize();

            var dict = new Dictionary<byte, Keyboard.HSVColor>();
            for (byte i = 0; i < 16; i++)
            {
                dict.Add(i, new Keyboard.HSVColor(22 * i, 1, 0.5));
            }
            await keyboard.SetRGBLightMulti(dict);

            Console.In.ReadLine();
        }
    }
}
