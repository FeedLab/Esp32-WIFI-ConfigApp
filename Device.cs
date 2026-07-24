namespace WifiAP
{
    public interface IDevice
    {
        string Name { get; }
        string DisplayName { get; }

        void Configure(string json);

        double ReadSensor(string sensorName);
    }
}
