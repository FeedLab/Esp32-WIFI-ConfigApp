using System;
using System.Device.I2c;
using System.Threading;

namespace WifiAP.Devices.Sensors
{
    public class SHT3x : IDevice
    {
        private const int PollIntervalMs = 2000;

        private I2cDevice sht3x;
        private double humidity;
        private double temperature;
        private Thread _pollThread;

        public void Configure(DeviceConfigurationEntry deviceData)
        {
            var settings = new I2cConnectionSettings(1, 0x44); // bus 1, address 0x44
            sht3x = I2cDevice.Create(settings);

            _pollThread = new Thread(() =>
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
            _pollThread.Start();
        }

        public double ReadSensor(string sensorName)
        {
            if(sensorName == "Temperature")
            {
                return temperature;
            }
            else if (sensorName == "Humidity")
            {
                return humidity;
            }
            else
            {
                throw new ArgumentException($"Unknown sensor name: {sensorName}");
            }
        }

        public void ReadSensorInternal()
        {
            byte[] cmd = new byte[] { 0x24, 0x00 };
            sht3x.Write(cmd);

            Thread.Sleep(150); // Wait for measurement to complete

            byte[] buffer = new byte[6];
            sht3x.Read(buffer);

            int rawTemp = (buffer[0] << 8) | buffer[1];
            int rawHum = (buffer[3] << 8) | buffer[4];

            temperature = -45 + 175 * (rawTemp / 65535.0);
            humidity = 100 * (rawHum / 65535.0);
        }

        public string Name { get; }
        public string DisplayName { get; }
    }
}
