using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

namespace KeyboardControl
{
    public class Keyboard
    {
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

        HidDevice device;

        readonly ushort vendorId = 0x4B42;
        readonly ushort productId = 0x6061;
        readonly ushort usagePage = 0xFF60;
        readonly ushort usageId = 0x61;

        private static readonly byte[] BYTE_EMPTY = new byte[0];
        private const byte REPORT_ID = 0;

        private const int REPORT_LEN = 32;
        private const int REPORT_LEN_WITH_ID = REPORT_LEN + 1;
        private const int DATA_MAX_LEN = REPORT_LEN - 1;

        private readonly Queue<TaskCompletionSource<ArraySegment<byte>>> reportResponses = new Queue<TaskCompletionSource<ArraySegment<byte>>>();
        private readonly SemaphoreSlim reportSemaphore = new SemaphoreSlim(1, 1);

        public Keyboard()
        {

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

        private void InputReportReceived(HidDevice sender, HidInputReportReceivedEventArgs args)
        {
            var data = args.Report.Data.ToArray();
            // var reportId = data[0];
            // var cmd = data[1];
            var code = (Code)data[2];
            
            Console.Out.WriteLine("[<] {0}", BitConverter.ToString(data));

            TaskCompletionSource<ArraySegment<byte>> tcs;
            lock (reportResponses)
            {
                tcs = reportResponses.Dequeue();
            }
            if (code == Code.OK)
            {
                tcs.SetResult(new ArraySegment<byte>(data, 2, data.Length - 2));
            }
            else
            {
                tcs.SetException(new KeyboardException(code));
            }
        }

        private async Task<ArraySegment<byte>?> SendCommand(Command cmd, bool silent = false, byte[] data = null)
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
                return null;
            }

            var tcs = new TaskCompletionSource<ArraySegment<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
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
    }
}
