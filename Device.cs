namespace WifiAP
{
    public interface IDevice
    {
        string Name { get; }
        string DisplayName { get; }

        void Configure(DeviceConfigurationEntry deviceData);

        double ReadSensor(string sensorName);
    }
}
