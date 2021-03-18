using NAudio.Wave;
using System;
using System.Collections.Generic;
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

            Console.In.ReadLine();
        }
    }
}
