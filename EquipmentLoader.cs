using System;
using System.Collections;
using nanoFramework.Hardware.Esp32;
using nanoFramework.Json;

namespace WifiAP
{
    /// <summary>Entry from ConfiguredEquipments.json - equipment installed on this device.</summary>
    public class EquipmentEntry
    {
        public string DeviceName { get; set; }
        public string AccessType { get; set; }
    }

    public class UartSettings
    {
        public string Port { get; set; }
        public string Parity { get; set; }
        public int BaudRate { get; set; }
        public int StopBits { get; set; }
    }

    public class I2CSettings
    {
        public string Address { get; set; }
    }

    public class SensorDef
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public string EndPoint { get; set; }
    }

    /// <summary>Entry from DeviceConfigurations.json - a known device type in the catalog.</summary>
    public class DeviceConfigurationEntry
    {
        public string[] DeviceNames { get; set; }
        public string ClassName { get; set; }
        public string AccessType { get; set; }
        public UartSettings UART { get; set; }
        public I2CSettings I2C { get; set; }
        public SensorDef[] Sensors { get; set; }
    }

    /// <summary>A sensor reading route, registered by EndPoint path for WebServer to serve.</summary>
    public class SensorEndpoint
    {
        public IDevice Device;
        public string SensorName;
    }

    /// <summary>
    /// JSON body for a single sensor reading. Needs public properties, not fields -
    /// nanoFramework.Json's JsonConvert.SerializeObject only reflects over public properties
    /// with getters (confirmed in its source/docs), so plain fields silently serialize to
    /// nothing.
    /// </summary>
    public class SensorReading
    {
        public double value { get; set; }
        public string timestamp { get; set; }
    }

    /// <summary>
    /// Matches ConfiguredEquipments.json against the DeviceConfigurations.json catalog,
    /// instantiates and configures the resulting IDevice instances, and registers each of
    /// their sensors' EndPoint paths so WebServer can serve live readings.
    /// </summary>
    public static class EquipmentLoader
    {
        public static ArrayList Devices { get; } = new ArrayList();

        public static Hashtable EndpointsByPath { get; } = new Hashtable();

        public static void LoadAndConfigure()
        {
            Log.Debug("[equip] LoadAndConfigure starting");

            EquipmentEntry[] equipment;
            DeviceConfigurationEntry[] catalog;

            try
            {
                // Parsing allocates a fair number of intermediate objects on top of the JSON
                // text itself - compact the heap around it, matching WebServer.cs's practice
                // before its own largest allocations.
                nanoFramework.Runtime.Native.GC.Run(true);
                equipment = (EquipmentEntry[])JsonConvert.DeserializeObject(
                    EquipmentCatalog.ConfiguredEquipmentsJson, typeof(EquipmentEntry[]));
                catalog = (DeviceConfigurationEntry[])JsonConvert.DeserializeObject(
                    EquipmentCatalog.DeviceConfigurationsJson, typeof(DeviceConfigurationEntry[]));
                nanoFramework.Runtime.Native.GC.Run(true);
            }
            catch (Exception ex)
            {
                Log.Info($"[equip] Failed to parse equipment configuration: {ex.Message}");
                StatusLed.ShowEquipmentError();
                return;
            }

            foreach (EquipmentEntry entry in equipment)
            {
                ConfigureOne(entry, catalog);
            }

            Log.Debug($"[equip] LoadAndConfigure finished, {Devices.Count} device(s) configured");
        }

        private static void ConfigureOne(EquipmentEntry entry, DeviceConfigurationEntry[] catalog)
        {
            DeviceConfigurationEntry matched = FindCatalogEntry(catalog, entry.DeviceName);
            if (matched == null)
            {
                Log.Info($"[equip] No catalog entry found for configured device '{entry.DeviceName}' - skipping");
                StatusLed.ShowEquipmentError();
                return;
            }

            IDevice device;
            try
            {
                device = CreateDevice(matched.ClassName);
            }
            catch (Exception ex)
            {
                Log.Info($"[equip] Could not create device for class '{matched.ClassName}': {ex.Message} - skipping");
                StatusLed.ShowEquipmentError();
                return;
            }

            try
            {
                Configuration.SetPinFunction(4, DeviceFunction.COM2_RX);
                Configuration.SetPinFunction(5, DeviceFunction.COM2_TX);
                
                Configuration.SetPinFunction(6, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(7, DeviceFunction.I2C1_CLOCK);
                
                // I2cScanner.ScanBus(1);
                
                device.Configure(matched);
                Devices.Add(device);
                RegisterEndpoints(device, matched);
                Log.Debug($"[equip] Configured '{entry.DeviceName}' as {matched.ClassName}");
            }
            catch (Exception ex)
            {
                Log.Info($"[equip] Configure failed for '{entry.DeviceName}' ({matched.ClassName}): {ex.Message} - skipping");
                StatusLed.ShowEquipmentError();
            }
        }

        private static void RegisterEndpoints(IDevice device, DeviceConfigurationEntry matched)
        {
            if (matched.Sensors == null)
            {
                return;
            }

            foreach (SensorDef sensor in matched.Sensors)
            {
                if (string.IsNullOrEmpty(sensor.EndPoint))
                {
                    continue;
                }

                string path = "/" + sensor.EndPoint.ToLower();
                if (!EndpointsByPath.Contains(path))
                {
                    EndpointsByPath.Add(path, new SensorEndpoint { Device = device, SensorName = sensor.Name });
                }
            }
        }

        private static DeviceConfigurationEntry FindCatalogEntry(DeviceConfigurationEntry[] catalog, string deviceName)
        {
            foreach (DeviceConfigurationEntry candidate in catalog)
            {
                if (candidate.DeviceNames == null)
                {
                    continue;
                }

                foreach (string name in candidate.DeviceNames)
                {
                    if (name == deviceName)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        // Manual factory switch - nanoFramework's reflection support (Type.GetType/Activator)
        // depends on a firmware-level opt-in that can't be relied on for this device, and
        // nothing else in this codebase uses reflection. Add a case whenever a new IDevice
        // class is added.
        private static IDevice CreateDevice(string className)
        {
            switch (className)
            {
                case "Bme280":
                    return new WifiAP.Devices.Sensors.Bme280();
                case "Hc8":
                    return new WifiAP.Devices.Sensors.Hc8();
                case "SHT3x":
                    return new WifiAP.Devices.Sensors.SHT3x();
                case "SHT4x":
                    return new WifiAP.Devices.Sensors.SHT4x();
                default:
                    throw new NotSupportedException("Unknown device class: " + className);
            }
        }
    }
}
