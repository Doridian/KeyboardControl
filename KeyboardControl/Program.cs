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
            var arr = new Keyboard.HSVColor[16];
            for (byte i = 0; i < 16; i++)
            {
                var x = new Keyboard.HSVColor(22 * i, 1, 0.5);
                arr[i] = x;
                dict.Add(i, x);
            }
            //await keyboard.SetRGBLightMulti(dict);
            await keyboard.SetRGBLightOffset(arr, 0);

            Console.In.ReadLine();
        }
    }
}
