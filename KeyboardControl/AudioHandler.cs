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

        private static readonly int FFT_LENGTH = 513;
        private static readonly int FFT_LENGTH_HALF = FFT_LENGTH / 2;
        private static readonly int FFT_M = (int)Math.Log(FFT_LENGTH, 2);

        private static readonly int MAX_BUCKET_WINDOW = 2;

        private static readonly double SOUND_FLOOR = 30;
        private static readonly double SOUND_CEILING = 10;
        private static readonly double SOUND_DYNAMIC_RANGE = SOUND_FLOOR - SOUND_CEILING;

        private Complex[] fftBuffer = new Complex[FFT_LENGTH];
        private int fftPos = 0;

        private readonly int bucketCount;

        private LinkedList<double[]> bucketPowers = new LinkedList<double[]>();

        public AudioHandler(IWaveIn waveIn, Keyboard keyboard)
        {
            this.waveIn = waveIn;
            this.keyboard = keyboard;
            bucketCount = 16;
            waveIn.DataAvailable += WaveIn_DataAvailable;
        }

        // TODO: Use bands: 25 38 59 91 140 215 331 510 784 1.2k 1.9k 2.9k 4.4k 6.8k 10.4k 16k
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            for (int i = 0; i < e.BytesRecorded; i += waveIn.WaveFormat.BlockAlign)
            {
                fftBuffer[fftPos].X = (float)(BitConverter.ToSingle(e.Buffer, i) * FastFourierTransform.HammingWindow(fftPos, FFT_LENGTH));
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

        private void FFTDone()
        {
            var currentBucketPowers = new double[bucketCount];
            for (int band = 0; band < FFT_LENGTH_HALF; band++)
            {
                //var freq = (waveIn.WaveFormat.SampleRate / FFT_LENGTH) * band;
                var value = Math.Log10(fftBuffer[band + FFT_LENGTH_HALF].X) * 10;
                
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

                var bucket = (band * bucketCount) / FFT_LENGTH_HALF;
                if (value > currentBucketPowers[bucket])
                {
                    currentBucketPowers[bucket] = value;
                }
            }

            bucketPowers.AddLast(currentBucketPowers);
            if (bucketPowers.Count > MAX_BUCKET_WINDOW)
            {
                bucketPowers.RemoveFirst();
            }

            SendToKeyboard();
        }

        private async void SendToKeyboard()
        {
            var averageBucketPowers = new double[bucketCount];
            foreach (var currentBucketPowers in bucketPowers)
            {
                for (var i = 0; i < bucketCount; i++)
                {
                    averageBucketPowers[i] += currentBucketPowers[i];
                }
            }

            var colors = new Keyboard.HSVColor[bucketCount];
            for (int i = 0; i < bucketCount; i++)
            {
                // 240 - 300
                var ratio = averageBucketPowers[i] / MAX_BUCKET_WINDOW;

                if (ratio > 0.5)
                {
                    colors[i] = new Keyboard.HSVColor(300.0 - (60.0 * Math.Sqrt((ratio - 0.5) * 2.0)), 1.0, 1.0);
                }
                else
                {
                    colors[i] = new Keyboard.HSVColor(300.0, 1.0, ratio * 2.0);
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
