using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeyboardControl
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Keyboard keyboard = new Keyboard();
            await keyboard.Initialize();



            Console.In.ReadLine();
        }
    }
}
