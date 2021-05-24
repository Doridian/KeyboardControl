using CSCore;
using CSCore.Codecs.WAV;
using CSCore.DSP;
using CSCore.SoundIn;
using CSCore.Streams;
using KeyboardControl.Visualization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeyboardControl
{
    public class AudioHandler
    {
        public int ledCount = 16;

        private readonly ISoundIn waveIn;
        private readonly Keyboard keyboard;

        public int numBars = 16;

        public int minFreq = 5;
        public int maxFreq = 4500;
        public int barSpacing = 0;
        public bool logScale = true;
        public bool isAverage = false;

        public float highScaleAverage = 2.0f;
        public float highScaleNotAverage = 3.0f;

        LineSpectrum lineSpectrum;

        //WaveWriter writer;
        FftSize fftSize;
        float[] fftBuffer;

        //SingleBlockNotificationStream notificationSource;

        BasicSpectrumProvider spectrumProvider;

        IWaveSource finalSource;

        public AudioHandler(ISoundIn waveIn, Keyboard keyboard)
        {
            this.waveIn = waveIn;
            this.keyboard = keyboard;

            waveIn.Initialize();

            // Get our capture as a source
            IWaveSource source = new SoundInSource(waveIn);

            // From https://github.com/filoe/cscore/blob/master/Samples/WinformsVisualization/Form1.cs

            // This is the typical size, you can change this for higher detail as needed
            fftSize = FftSize.Fft4096;

            // Actual fft data
            fftBuffer = new float[(int)fftSize];

            // These are the actual classes that give you spectrum data
            // The specific vars of lineSpectrum here aren't that important because they can be changed by the user
            spectrumProvider = new BasicSpectrumProvider(waveIn.WaveFormat.Channels,
                        waveIn.WaveFormat.SampleRate, fftSize);

            lineSpectrum = new LineSpectrum(fftSize)
            {
                SpectrumProvider = spectrumProvider,
                UseAverage = true,
                BarCount = numBars,
                BarSpacing = 2,
                IsXLogScale = false,
                ScalingStrategy = ScalingStrategy.Linear
            };

            // Tells us when data is available to send to our spectrum
            var notificationSource = new SingleBlockNotificationStream(source.ToSampleSource());

            notificationSource.SingleBlockRead += NotificationSource_SingleBlockRead;

            // We use this to request data so it actualy flows through (figuring this out took forever...)
            finalSource = notificationSource.ToWaveSource();

            waveIn.DataAvailable += Capture_DataAvailable;
        }

        private void Capture_DataAvailable(object sender, DataAvailableEventArgs e)
        {
            finalSource.Read(e.Data, e.Offset, e.ByteCount);
        }

        private void NotificationSource_SingleBlockRead(object sender, SingleBlockReadEventArgs e)
        {
            spectrumProvider.Add(e.Left, e.Right);
        }

        public float[] barData = new float[20];

        private float[] GetFFtData()
        {
            lock (barData)
            {
                lineSpectrum.BarCount = numBars;
                if (numBars != barData.Length)
                {
                    barData = new float[numBars];
                }
            }

            if (spectrumProvider.IsNewDataAvailable)
            {
                lineSpectrum.MinimumFrequency = minFreq;
                lineSpectrum.MaximumFrequency = maxFreq;
                lineSpectrum.IsXLogScale = logScale;
                lineSpectrum.BarSpacing = barSpacing;
                lineSpectrum.SpectrumProvider.GetFftData(fftBuffer, this);
                return lineSpectrum.GetSpectrumPoints(100.0f, fftBuffer);
            }
            else
            {
                return null;
            }
        }

        public void RefreshKeyboard()
        {
            if (ComputeData())
            {
                SendToKeyboard();
            }
        }

        private bool ComputeData()
        {
            float[] resData = GetFFtData();

            int numBars = barData.Length;

            if (resData == null)
            {
                return false;
            }

            lock (barData)
            {
                for (int i = 0; i < numBars && i < resData.Length; i++)
                {
                    // Make the data between 0.0 and 1.0
                    barData[i] = resData[i] / 100.0f;
                }

                for (int i = 0; i < numBars && i < resData.Length; i++)
                {
                    if (lineSpectrum.UseAverage)
                    {
                        // Scale the data because for some reason bass is always loud and treble is soft
                        barData[i] = barData[i] + highScaleAverage * (float)Math.Sqrt(i / (numBars + 0.0f)) * barData[i];
                    }
                    else
                    {
                        barData[i] = barData[i] + highScaleNotAverage * (float)Math.Sqrt(i / (numBars + 0.0f)) * barData[i];
                    }
                }
            }

            return true;
        }

        private static readonly int[] BUCKET_LEDS = { 0, 15, 1, 14, 2, 13, 3, 12, 4, 11, 5, 10, 6, 9, 8, 7 };

        private async void SendToKeyboard()
        {
            var colors = new Keyboard.HSVColor[ledCount];
            for (int i = 0; i < ledCount; i++)
            {
                var ratio = barData[i];
                var l = BUCKET_LEDS[i];

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
            waveIn.Start();
        }

        public void Stop()
        {
            waveIn.Stop();
        }
    }
}
