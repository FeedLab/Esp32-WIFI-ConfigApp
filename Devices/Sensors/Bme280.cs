using System;

namespace WifiAP.Devices.Sensors
{
    public class Bme280 : IDevice
    {
        public void Configure(DeviceConfigurationEntry deviceData)
        {
        }

        public SensorReadingResponse ReadSensor(string sensorName)
        {
            return new SensorReadingResponse { Value = 0, Timestamp = DateTime.UtcNow };
        }

        public string Name { get; set; }
        public string DisplayName { get; set; }
    }
}