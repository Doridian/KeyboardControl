using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeyboardControl
{
    public class AudioHandler
    {
        private readonly IWaveIn waveIn;
        private readonly Keyboard keyboard;

        private static readonly int FFT_LENGTH = 512;
        private static readonly int FFT_LENGTH_HALF = FFT_LENGTH / 2;
        private static readonly int FFT_M = (int)Math.Log(FFT_LENGTH, 2);

        private static readonly int MAX_BUCKET_WINDOW = 5;

        private static readonly double SOUND_FLOOR = 120;
        private static readonly double SOUND_CEILING = 60;
        private static readonly double SOUND_DYNAMIC_RANGE = SOUND_FLOOR - SOUND_CEILING;

        private Complex[] fftBuffer = new Complex[FFT_LENGTH];
        private int fftPos = 0;

        private LinkedList<double[]> bucketPowers = new LinkedList<double[]>();

        public AudioHandler(IWaveIn waveIn, Keyboard keyboard)
        {
            this.waveIn = waveIn;
            this.keyboard = keyboard;
            waveIn.DataAvailable += WaveIn_DataAvailable;
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            for (int i = 0; i < e.BytesRecorded; i += waveIn.WaveFormat.BlockAlign)
            {
                fftBuffer[fftPos].X = (float)(BitConverter.ToSingle(e.Buffer, i) * FastFourierTransform.HannWindow(fftPos, FFT_LENGTH));
                fftBuffer[fftPos].Y = 0;
                fftPos++;
                if (fftPos >= FFT_LENGTH)
                {
                    FastFourierTransform.FFT(true, FFT_M, fftBuffer);
                    FFTDone();
                    fftPos = 0;
                }
            }
        }

        // TODO: Use bands: 25 38 59 91 140 215 331 510 784 1.2k 1.9k 2.9k 4.4k 6.8k 10.4k 16k
        private static readonly double[] BUCKET_FREQS = { 25, 38, 59, 91, 140, 215, 331, 510, 784, 1200, 1900, 2900, 4400, 6800, 10400, 16000 };
        private static readonly int[] BUCKET_LEDS = { 0, 15, 1, 14, 2, 13, 3, 12, 4, 11, 5, 10, 6, 9, 8, 7 };

        private double DBWeighting(double freq)
        {
            var freq2 = freq * freq;
            // A weighting
            double upper = (12194 * 12194) * (freq2 * freq2);
            double lower = (freq2 + 20.6 * 20.6) * Math.Sqrt((freq2 + 107.7 * 107.7) * (freq2 + 737.9 * 737.9)) * (freq2 + 12194 * 12194);
            return Math.Log10(upper / lower) * 20 + 2.00;
            // C weighting
            /*double upper = (12194 * 12194) * freq2;
            double lower = (freq2 + 20.6 * 20.6) * (freq2 + 12194 * 12194);
            return Math.Log10(upper / lower) * 20 + 0.06;*/
        }

        private void FFTDone()
        {
            var bandWidth = waveIn.WaveFormat.SampleRate / FFT_LENGTH;
            var currentBucketPowers = new double[BUCKET_FREQS.Length];
            
            /*
            for (var i = 0; i < currentBucketPowers.Length; i++)
            {
                currentBucketPowers[i] = -9999;
            }


            int w = Console.WindowWidth - 2;
            Console.SetCursorPosition(0, 0);
            */

            var currentBucket = 0;
            for (int band = 0; band < FFT_LENGTH_HALF; band++)
            {
                var freq = bandWidth * (band + 0.5);
                var complex = fftBuffer[band + FFT_LENGTH_HALF];
                var magnitudeSqr = (complex.X * complex.X) + (complex.Y * complex.Y);
                var value = Math.Log10(magnitudeSqr) * 10 - DBWeighting(freq);

                // Map value onto 0 (-SOUND_FLLOR dB) - 1 (-SOUND_CEILING dB) range
                if (double.IsNaN(value) || double.IsInfinity(value) || value < -SOUND_FLOOR)
                {
                    value = -SOUND_FLOOR;
                }

                value += SOUND_FLOOR;

                value /= SOUND_DYNAMIC_RANGE;
                if (value > 1)
                {
                    value = 1;
                }
                
                if (currentBucket < (BUCKET_FREQS.Length - 1) && Math.Abs(freq - BUCKET_FREQS[currentBucket]) > Math.Abs(freq - BUCKET_FREQS[currentBucket + 1]))
                {
                    currentBucket++;
                }

                if (value > currentBucketPowers[currentBucket])
                {
                    currentBucketPowers[currentBucket] = value;
                }
            }

            /*
            foreach (var bucket in currentBucketPowers)
            {
                var power = bucket.ToString(); //"".PadLeft((int)(w * bucket), '#');
                Console.Out.WriteLine(power.PadRight(w, ' '));
            }*/

            bucketPowers.AddFirst(currentBucketPowers);
            if (bucketPowers.Count > MAX_BUCKET_WINDOW)
            {
                bucketPowers.RemoveLast();
            }

            SendToKeyboard();
        }

        private async void SendToKeyboard()
        {
            var averageBucketPowers = new double[BUCKET_FREQS.Length];
            var p = 0;
            var pmax = 0.0;
            foreach (var currentBucketPowers in bucketPowers)
            {
                var relPowerFactor = 1.0 - (p / MAX_BUCKET_WINDOW);
                relPowerFactor *= relPowerFactor;

                pmax += relPowerFactor;
                for (var i = 0; i < BUCKET_FREQS.Length; i++)
                {
                    var relPower = currentBucketPowers[i] * relPowerFactor;
                    /*if (averageBucketPowers[i] < relPower)
                    {
                        averageBucketPowers[i] = relPower;
                    }*/
                    averageBucketPowers[i] += relPower;
                }
                p++;
            }

            var colors = new Keyboard.HSVColor[BUCKET_FREQS.Length];
            for (int i = 0; i < BUCKET_FREQS.Length; i++)
            {
                // 240 - 300
                var ratio = averageBucketPowers[i] / pmax;
                var l = BUCKET_LEDS[i];
                if (l < 0)
                {
                    continue;
                }

                if (ratio > 0.5)
                {
                    colors[l] = new Keyboard.HSVColor(300.0 - (60.0 * Math.Sqrt((ratio - 0.5) * 2.0)), 1.0, 1.0);
                }
                else
                {
                    colors[l] = new Keyboard.HSVColor(300.0, 1.0, ratio * 2.0);
                }
            }

            await keyboard.SetRGBLightOffset(colors, 0);
        }

        public void Start()
        {
            waveIn.StartRecording();
        }

        public void Stop()
        {
            waveIn.StopRecording();
        }
    }
}
