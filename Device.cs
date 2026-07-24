using System;

namespace WifiAP
{
    public interface IDevice
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }

        void Configure(DeviceConfigurationEntry deviceData);

        SensorReadingResponse ReadSensor(string sensorName);
    }
    
    public class SensorReadingResponse
    {
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
