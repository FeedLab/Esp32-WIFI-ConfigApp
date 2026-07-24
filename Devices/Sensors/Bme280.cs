namespace WifiAP.Devices.Sensors
{
    public class Bme280 : IDevice
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