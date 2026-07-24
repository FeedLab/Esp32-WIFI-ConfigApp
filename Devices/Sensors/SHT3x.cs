using System;
using System.Device.I2c;
using System.Threading;

namespace WifiAP.Devices.Sensors
{
    public class SHT3x : IDevice
    {
        private const int PollIntervalMs = 2000;

        private I2cDevice sht3X;
        private double humidity;
        private double temperature;
        private DateTime lastSensorPollTime = DateTime.MinValue;
        private Thread pollThread;
        private DeviceConfigurationEntry deviceInformation;

        public void Configure(DeviceConfigurationEntry deviceData)
        {
            deviceInformation = deviceData;
            
            Name = deviceInformation.DeviceName;
            DisplayName = deviceInformation.DeviceName;
            
            var settings = new I2cConnectionSettings(1, 0x44); // bus 1, address 0x44
            sht3X = I2cDevice.Create(settings);

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
            if (sensorName == "Temperature")
            {
                return new SensorReadingResponse { Value = temperature, Timestamp = lastSensorPollTime };
            }
            else if (sensorName == "Humidity")
            {
                return new SensorReadingResponse { Value = humidity, Timestamp = lastSensorPollTime };
            }
            else
            {
                throw new ArgumentException($"Unknown sensor name: {sensorName}");
            }
        }

        private void ReadSensorInternal()
        {
            byte[] cmd = new byte[] { 0x24, 0x00 };
            sht3X.Write(cmd);

            Thread.Sleep(150);

            var buffer = new byte[6];
            sht3X.Read(buffer);

            int rawTemp = (buffer[0] << 8) | buffer[1];
            int rawHum = (buffer[3] << 8) | buffer[4];

            temperature = -45 + 175 * (rawTemp / 65535.0);
            humidity = 100 * (rawHum / 65535.0);
            lastSensorPollTime = DateTime.UtcNow;
        }

        public string Name { get; set; }
        public string DisplayName { get; set; }
    }
}