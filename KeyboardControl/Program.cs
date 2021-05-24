using CSCore.SoundIn;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardControl
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var keyboard = new Keyboard();
            await keyboard.Initialize();

            var audioHandler = new AudioHandler(new WasapiLoopbackCapture(), keyboard);
            audioHandler.Start();

            var t = new Thread(delegate ()
            {
                while (true)
                {
                    Thread.Sleep(1000 / 60);
                    audioHandler.RefreshKeyboard();
                }
            });
            t.Start();

            Console.In.ReadLine();
        }
    }
}
