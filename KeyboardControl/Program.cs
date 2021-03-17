using System;
using System.Threading.Tasks;

namespace KeyboardControl
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Keyboard keyboard = new Keyboard();
            await keyboard.Initialize();

            Console.Out.WriteLine(await keyboard.GetRGBLightHSV());

            Console.In.ReadLine();
        }
    }
}
