using nanoFramework.Networking;
using System;
using System.IO.Ports;
using System.Threading;

namespace WifiAP.Devices.Sensors
{
    public class Hc8 : IDevice
    {
        private static readonly byte[] GetPpmCmd = { 0x64, 0x69, 0x03, 0x5E, 0x4E };
        private SerialPort uart;
        private DeviceConfigurationEntry deviceInformation;
        private Thread pollThread;
        private DateTime lastSensorPollTime = DateTime.MinValue;
        private double co2;
        private const int PollIntervalMs = 2000;

        public string Name { get; set; }
        public string DisplayName { get; set; }

        public void Configure(DeviceConfigurationEntry deviceData)
        {
            deviceInformation = deviceData;
            
            Name = deviceInformation.DeviceName;
            DisplayName = deviceInformation.DeviceName;

            uart = new SerialPort("COM2", 9600, Parity.None, 8, StopBits.One);
            uart.Open();

            pollThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        ReadSensorInternal();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"{nameof(SHT3x)}: poll failed: {ex.Message}");
                    }

                    Thread.Sleep(PollIntervalMs);
                }
            });
            pollThread.Start();
        }

        public SensorReadingResponse ReadSensor(string sensorName)
        {
            if (sensorName == "Co2")
            {
                return new SensorReadingResponse { Value = co2, Timestamp = lastSensorPollTime };
            }
            else
            {
                throw new ArgumentException($"Unknown sensor name: {sensorName}");
            }
        }

        private void ReadSensorInternal()
        {
            DrainRxBuffer(); // clear out any stray/late frame before asking

            uart.Write(GetPpmCmd, 0, GetPpmCmd.Length);

            // Sensor is slow to reply; reading immediately after write times out.
            Thread.Sleep(50);

            byte[] response = new byte[14];
            int totalRead = 0;
            DateTime start = DateTime.UtcNow;

            while (totalRead < response.Length && (DateTime.UtcNow - start).TotalMilliseconds < 200)
            {
                if (uart.BytesToRead > 0)
                {
                    totalRead += uart.Read(response, totalRead, response.Length - totalRead);
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            if (totalRead < response.Length)
            {
                Log.Debug($"{nameof(Hc8)}: incomplete response ({totalRead}/{response.Length} bytes)");
                co2 = double.NaN;
            }

            if (response[0] != 0x64 || response[1] != 0x69)
            {
                Log.Debug($"{nameof(Hc8)}: invalid preamble");
                co2 = double.NaN;
            }

            ushort crc = Crc16Modbus(response, 12);
            ushort expectedCrc = (ushort)((response[13] << 8) | response[12]);

            if (crc != expectedCrc)
            {
                Log.Debug($"{nameof(Hc8)}: checksum mismatch");
                co2 = double.NaN;
            }

            int ppm = (response[5] << 8) | response[4];

            co2 = ppm;
            lastSensorPollTime = DateTime.UtcNow;
        }

        private void DrainRxBuffer()
        {
            while (uart.BytesToRead > 0)
            {
                byte[] discard = new byte[uart.BytesToRead];
                uart.Read(discard, 0, discard.Length);
            }
        }

        private static ushort Crc16Modbus(byte[] data, int length)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];

                for (int b = 0; b < 8; b++)
                {
                    crc = (crc & 0x0001) != 0
                        ? (ushort)((crc >> 1) ^ 0xA001)
                        : (ushort)(crc >> 1);
                }
            }

            return crc;
        }


    }
}