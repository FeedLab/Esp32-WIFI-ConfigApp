namespace WifiAP.Devices.Sensors
{
    public class SHT4x : IDevice
    {
        public void Configure(string json)
        {
        }

        public double ReadSensor(string sensorName)
        {
            return 0;
        }

        public string Name { get; }
        public string DisplayName { get; }
    }
}
