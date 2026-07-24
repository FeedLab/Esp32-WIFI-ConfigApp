using nanoFramework.Networking;
using System;
using System.IO.Ports;
using System.Threading;
using nanoFramework.Hardware.Esp32;

namespace WifiAP.Devices.Sensors
{
    public class Hc8 : IDevice
    {
        private static readonly byte[] GetPpmCmd = { 0x64, 0x69, 0x03, 0x5E, 0x4E };
        private SerialPort uart;
        private DeviceConfigurationEntry deviceInformation;

        public string Name { get; } = "Hc8";
        public string DisplayName { get; } = "HC-8";

        public void Configure(DeviceConfigurationEntry deviceData)
        {
            deviceInformation = deviceData;
            
            


            uart = new SerialPort("COM2", 9600, Parity.None, 8, StopBits.One);
            uart.Open();

            Thread.Sleep(3000);
            Log.Debug($"Bytes waiting after 3s passive listen: {uart.BytesToRead}");
        }

        public double ReadSensor(string sensorName)
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
                return double.NaN;
            }

            if (response[0] != 0x64 || response[1] != 0x69)
            {
                Log.Debug($"{nameof(Hc8)}: invalid preamble");
                return double.NaN;
            }

            ushort crc = Crc16Modbus(response, 12);
            ushort expectedCrc = (ushort)((response[13] << 8) | response[12]);

            if (crc != expectedCrc)
            {
                Log.Debug($"{nameof(Hc8)}: checksum mismatch");
                return double.NaN;
            }

            int ppm = (response[5] << 8) | response[4];

            Log.Debug($"{nameof(Hc8)} Response: CO2={ppm}ppm");

            return ppm;
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