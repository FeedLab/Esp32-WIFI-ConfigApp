namespace WifiAP.Devices.Sensors
{
    public class SHT4x : IDevice
    {
        public void Configure(DeviceConfigurationEntry deviceData)
        {
        }

        public SensorReadingResponse ReadSensor(string sensorName)
        {
            return new SensorReadingResponse { Value = 0 };
        }

        public string Name { get; set; }
        public string DisplayName { get; set; }
    }
}
