namespace WifiAP
{
    /// <summary>
    /// Verbatim copies of ConfiguredEquipments.json and DeviceConfigurations.json, embedded as
    /// string constants. Content-included files aren't reliably readable back at runtime on
    /// this device without extra flash-filesystem support (and a normal VS/nanoff deploy doesn't
    /// push them), so the .json files in the project root remain the human-editable source but
    /// are not read directly - update the matching constant here by hand whenever they change.
    /// </summary>
    public static class EquipmentCatalog
    {
        public const string ConfiguredEquipmentsJson = @"[
  {
    ""DeviceName"": ""MH‑Z19B"",
    ""AccessType"": ""UART""
  }
]";

        public const string DeviceConfigurationsJson = @"[
  {
    ""DeviceNames"": [""MH‑Z19"", ""MH‑Z19B"", ""MH‑Z19C"", ""MH‑Z19E""],
    ""ClassName"": ""Hc8"",
    ""AccessType"": ""UART"",
    ""UART"": {
      ""Port"": ""Com1"",
      ""Parity"": ""None"",
      ""BaudRate"": 9600,
      ""StopBits"": 1
    },
    ""Sensors"": [
      {
        ""Name"": ""Co2"",
        ""Unit"": ""ppm"",
        ""EndPoint"": ""Readings/Co2""
      }
    ]
  },
  {
    ""DeviceNames"": [""SHT40"", ""SHT41""],
    ""ClassName"": ""SHT4x"",
    ""AccessType"": ""I2C"",
    ""Sensors"": [
      {
        ""Name"": ""Temperature"",
        ""Unit"": ""°C"",
        ""EndPoint"": ""Readings/Temperature""
      },
      {
        ""Name"": ""Humidity"",
        ""Unit"": ""%"",
        ""EndPoint"": ""Readings/Humidity""
      }
    ]
  },
  {
    ""DeviceNames"": [""BME280""],
    ""ClassName"": ""Bme280"",
    ""AccessType"": ""I2C"",
    ""I2C"": {
      ""Address"": ""0x76""
    },
    ""Sensors"": [
      {
        ""Name"": ""Temperature"",
        ""Unit"": ""°C"",
        ""EndPoint"": ""Readings/Temperature""
      },
      {
        ""Name"": ""Humidity"",
        ""Unit"": ""%"",
        ""EndPoint"": ""Readings/Humidity""
      },
      {
        ""Name"": ""Pressure"",
        ""Unit"": ""hPa"",
        ""EndPoint"": ""Readings/Pressure""
      }
    ]
  }
]";
    }
}
