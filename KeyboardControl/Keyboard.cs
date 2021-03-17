using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

namespace KeyboardControl
{
    public class Keyboard
    {
        #region Subclasses
        public enum Command
        {
            PING = 1,

            RGBLIGHT_ENABLE_GET = 10,
            RGBLIGHT_ENABLE_SET = 11,
            RGBLIGHT_HSV_GET = 12,
            RGBLIGHT_HSV_SET = 13,
            RGBLIGHT_HSV_SET_NOEEPROM = 14,
            RGBLIGHT_SET_MULTI_RGB = 15,
            RGBLIGHT_SET_MULTI_HSV = 16,
            RGBLIGHT_SET_OFFSET_RGB = 17,
            RGBLIGHT_SET_OFFSET_HSV = 18,

            NKRO_GET = 30,
            NKRO_SET = 31,

            RESET = 60,
            RESET_EEPROM = 61,
        }

        public enum Code
        {
            OK = 1,
            UNHANDLED =2,
            ERROR = 3,
            INVALID = 4,
        }

        public class KeyboardException : Exception
        {
            public KeyboardException(Code code) : base("Error code " + code) { }
        }

        public struct HSVColor : IEquatable<HSVColor>
        {
            public double h;
            public double s;
            public double v;

            public void FromBytes(byte[] hsv)
            {
                h = (hsv[0] / 360.0) * 255.0;
                s = (hsv[1] / 255.0);
                v = (hsv[2] / 255.0);
            }

            public override int GetHashCode()
            {
                int hashCode = -716921630;
                hashCode = hashCode * -1521134295 + h.GetHashCode();
                hashCode = hashCode * -1521134295 + s.GetHashCode();
                hashCode = hashCode * -1521134295 + v.GetHashCode();
                return hashCode;
            }

            public byte[] ToBytes()
            {
                var hb = (byte)((h / 360.0) * 255.0);
                var sb = (byte)(s * 255.0);
                var vb = (byte)(v * 255.0);
                return new byte[] { hb, sb, vb };
            }

            public override bool Equals(object obj)
            {
                return obj is HSVColor color && Equals(color);
            }

            public bool Equals(HSVColor other)
            {
                return h == other.h &&
                       s == other.s &&
                       v == other.v;
            }

            public override string ToString()
            {
                return string.Format("H = {0}; S = {1}; V = {2}", h, s, v);
            }
        }
        #endregion

        #region Fields
        private HidDevice device;

        private readonly ushort vendorId;
        private readonly ushort productId;
        private readonly ushort usagePage;
        private readonly ushort usageId;

        private readonly Queue<TaskCompletionSource<byte[]>> reportResponses = new Queue<TaskCompletionSource<byte[]>>();
        private readonly SemaphoreSlim reportSemaphore = new SemaphoreSlim(1, 1);
        #endregion

        #region Constants
        private static readonly byte[] BYTE_EMPTY = new byte[0];
        private const byte REPORT_ID = 0;

        private const int REPORT_LEN = 32;
        private const int REPORT_LEN_WITH_ID = REPORT_LEN + 1;
        private const int DATA_MAX_LEN = REPORT_LEN - 1;
        #endregion

        public Keyboard(ushort vendorId = 0x4B42, ushort productId = 0x6061, ushort usagePage = 0xFF60, ushort usageId = 0x61)
        {
            this.vendorId = vendorId;
            this.productId = productId;
            this.usagePage = usagePage;
            this.usageId = usageId;
        }

        private static bool ByteToBool(byte n)
        {
            return n != 0;
        }

        private static byte BoolToByte(bool b)
        {
            return b ? (byte)1 : (byte)0;
        }

        public async Task Initialize()
        {
            var selector = HidDevice.GetDeviceSelector(usagePage, usageId, vendorId, productId);
            var devices = await DeviceInformation.FindAllAsync(selector);
            if (!devices.Any())
            {
                throw new Exception("Could not find keyboard!");
            }

            device = await HidDevice.FromIdAsync(devices.First().Id, FileAccessMode.ReadWrite);
            if (device == null)
            {
                throw new Exception("Could not open keyboard!");
            }

            device.InputReportReceived += InputReportReceived;

            await SendCommand(Command.PING);
        }

        public async Task SetRGBLightEnabled(bool enable)
        {
            await this.SendCommand(Command.RGBLIGHT_ENABLE_SET, new byte[] { BoolToByte(enable) });
        }

        public async Task<bool> GetRGBLightEnabled()
        {
            var res = await this.SendCommand(Command.RGBLIGHT_ENABLE_GET);
            return ByteToBool(res[0]);
        }

        public async Task SetRGBLightHSV(HSVColor color, bool noeeprom = false)
        {
            await SendCommand(noeeprom ? Command.RGBLIGHT_HSV_SET_NOEEPROM : Command.RGBLIGHT_HSV_SET, color.ToBytes());
        }

        public async Task<HSVColor> GetRGBLightHSV()
        {
            var res = await this.SendCommand(Command.RGBLIGHT_HSV_GET, new byte[] { });
            HSVColor ret = new HSVColor();
            ret.FromBytes(res);
            return ret;
        }

        #region Basic HID I/O
        private void InputReportReceived(HidDevice sender, HidInputReportReceivedEventArgs args)
        {
            var data = args.Report.Data.ToArray();
            // var reportId = data[0];
            // var cmd = data[1];
            var code = (Code)data[2];
            
            Console.Out.WriteLine("[<] {0}", BitConverter.ToString(data));

            TaskCompletionSource<byte[]> tcs;
            lock (reportResponses)
            {
                tcs = reportResponses.Dequeue();
            }
            if (code == Code.OK)
            {
                var res = new byte[data.Length - 3];
                Buffer.BlockCopy(data, 3, res, 0, res.Length);
                tcs.SetResult(res);
            }
            else
            {
                tcs.SetException(new KeyboardException(code));
            }
        }

        private async Task<byte[]> SendCommand(Command cmd, byte[] data = null, bool silent = false)
        {
            if (data == null)
            {
                data = BYTE_EMPTY;
            }

            if (data.Length > DATA_MAX_LEN)
            {
                throw new Exception("data length > " + DATA_MAX_LEN);
            }

            var sendData = new byte[REPORT_LEN_WITH_ID];
            sendData[0] = REPORT_ID;
            sendData[1] = (byte)cmd;
            if (silent)
            {
                sendData[1] |= 128;
            }
            data.CopyTo(sendData, 2);

            var report = device.CreateOutputReport(REPORT_ID);
            report.Data = sendData.AsBuffer();

            Console.Out.WriteLine("[>] {0}", BitConverter.ToString(sendData));

            if (silent)
            {
                await device.SendOutputReportAsync(report);
                return BYTE_EMPTY;
            }

            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await reportSemaphore.WaitAsync();
                lock (reportResponses)
                {
                    reportResponses.Enqueue(tcs);
                }
                await device.SendOutputReportAsync(report);
            }
            finally
            {
                reportSemaphore.Release();
            }
            return await tcs.Task;
        }
        #endregion
    }
}
