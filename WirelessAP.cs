//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Iot.Device.DhcpServer;
using nanoFramework.Runtime.Native;

namespace WifiAP
{
    /// <summary>
    /// Provides methods and properties to manage a wireless access point.
    /// </summary>
    public static class WirelessAP
    {
        /// <summary>
        /// The IP address of the Soft AP (also the captive-portal / web-server address).
        /// </summary>
        public const string SoftApIpAddress = "192.168.4.1";

        /// <summary>
        /// Gets or sets the IP address of the Soft AP.
        /// </summary>
        private static string SoftAppIp { get; set; } = SoftApIpAddress;

        /// <summary>
        /// The default SSID used for the Soft AP the first time it is ever configured. Once a
        /// name has been persisted via <see cref="SetApName"/>, that value is used instead of
        /// falling back to this default.
        /// </summary>
        private const string DefaultApSsid = "Sensor-Setup";

        /// <summary>
        /// Sets the configuration for the wireless access point.
        /// </summary>
        /// <param name="forceReconfigure">
        /// If <see langword="true"/>, writes and saves the configuration even if it already
        /// matches the wanted settings. Use this when the caller wants a guaranteed reboot
        /// into a fresh Soft AP (e.g. the user explicitly requested setup mode via the button).
        /// </param>
        public static void SetWifiAp(bool forceReconfigure = false)
        {
            Debug.WriteLine("[ap] SetWifiAp starting");
            Wireless80211.Disable();

            bool configWritten = ConfigureAp(forceReconfigure);
            Debug.WriteLine($"[ap] ConfigureAp wrote new config: {configWritten}");
            if (configWritten)
            {
                // A new configuration was written; reboot to activate the Access Point on restart.
                Console.WriteLine("Setup Soft AP, Rebooting device");
                Power.RebootDevice();
            }

            Debug.WriteLine($"[ap] starting DHCP server on {SoftAppIp}");
            var dhcpserver = new DhcpServer
            {
                CaptivePortalUrl = $"http://{SoftAppIp}"
            };
            var dhcpInitResult = dhcpserver.Start(IPAddress.Parse(SoftAppIp), new IPAddress(new byte[] { 255, 255, 255, 0 }));
            if (!dhcpInitResult)
            {
                Console.WriteLine($@"Error initializing DHCP server.");
                // This happens after a very freshly flashed device
                Debug.WriteLine("[ap] DHCP server failed to start - rebooting");
                Power.RebootDevice();
            }

            Console.WriteLine($@"Running Soft AP, waiting for client to connect");
            Console.WriteLine($@"Soft AP IP address :{GetIpAddress()}");
        }

        /// <summary>
        /// Disable the Soft AP for the next restart.
        /// </summary>
        public static void Disable()
        {
            Debug.WriteLine("[ap] Disable: turning off Soft AP for next boot");
            WirelessAPConfiguration wirelessAPConfiguration = GetConfiguration();
            if (wirelessAPConfiguration == null)
            {
                Debug.WriteLine("[ap] Disable: no WirelessAP configuration found, nothing to do");
                return;
            }

            wirelessAPConfiguration.Options = WirelessAPConfiguration.ConfigurationOptions.None;
            wirelessAPConfiguration.SaveConfiguration();
        }

        /// <summary>
        /// Ensures the Wireless AP settings are configured, enabled, and saved.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if a new configuration was written and the device must
        /// reboot to activate it; <see langword="false"/> if it was already configured.
        /// </returns>
        private static bool ConfigureAp(bool forceReconfigure = false)
        {
            NetworkInterface ni = GetInterface();
            WirelessAPConfiguration wapconf = GetConfiguration();

            if (ni == null || wapconf == null)
            {
                // No Wireless AP interface is available on this device/firmware.
                Debug.WriteLine("[ap] ConfigureAp: no WirelessAP interface found");
                throw new InvalidOperationException("No WirelessAP network interface was found.");
            }

            // Keep whatever name the user has already chosen (see SetApName); only fall back to
            // the default the very first time, when nothing has been configured yet.
            string wantedSsid = string.IsNullOrEmpty(wapconf.Ssid) ? DefaultApSsid : wapconf.Ssid;

            Debug.WriteLine($"[ap] ConfigureAp: current options={wapconf.Options}, current IP={ni.IPv4Address}, wanted IP={SoftAppIp}, current SSID={wapconf.Ssid}, wanted SSID={wantedSsid}, forceReconfigure={forceReconfigure}");

            // If already enabled with the expected IP and SSID, no configuration change is
            // needed - unless the caller explicitly wants a fresh configuration written
            // regardless. SSID must be checked too, otherwise a stale/default SSID saved from
            // an earlier boot would keep matching on Options+IP alone and never get corrected.
            if (!forceReconfigure &&
                wapconf.Options == (WirelessAPConfiguration.ConfigurationOptions.Enable |
                                    WirelessAPConfiguration.ConfigurationOptions.AutoStart) &&
                ni.IPv4Address == SoftAppIp &&
                wapconf.Ssid == wantedSsid)
            {
                Debug.WriteLine("[ap] ConfigureAp: already configured, no changes needed");
                return false;
            }

            // Set up IP address for Soft AP
            ni.EnableStaticIPv4(SoftAppIp, "255.255.255.0", SoftAppIp);

            // Set Options for Network Interface
            //
            // Enable    - Enable the Soft AP ( Disable to reduce power )
            // AutoStart - Start Soft AP when system boots.
            // HiddenSSID- Hide the SSID
            //
            wapconf.Options = WirelessAPConfiguration.ConfigurationOptions.AutoStart |
                            WirelessAPConfiguration.ConfigurationOptions.Enable;

            // Set the SSID for Access Point.
            wapconf.Ssid = wantedSsid;

            // Maximum number of simultaneous connections, reserves memory for connections
            wapconf.MaxConnections = 1;

            // To set-up Access point with no Authentication
            wapconf.Authentication = System.Net.NetworkInformation.AuthenticationType.Open;
            wapconf.Password = "";

            // To set up Access point with no Authentication. Password minimum 8 chars.
            //wapconf.Authentication = AuthenticationType.WPA2;
            //wapconf.Password = "password";

            // Save the configuration so on restart Access point will be running.
            wapconf.SaveConfiguration();

            return true;
        }

        /// <summary>
        /// Find the Wireless AP configuration
        /// </summary>
        /// <returns>Wireless AP configuration or <see langword="null"/> if not available.</returns>
        private static WirelessAPConfiguration GetConfiguration()
        {
            NetworkInterface ni = GetInterface();
            if (ni == null)
            {
                return null;
            }

            return WirelessAPConfiguration.GetAllWirelessAPConfigurations()[ni.SpecificConfigId];
        }

        /// <summary>
        /// Gets the network interface for the wireless access point.
        /// </summary>
        /// <returns>The network interface for the wireless access point.</returns>
        private static NetworkInterface GetInterface()
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Find WirelessAP interface
            foreach (NetworkInterface ni in interfaces)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.WirelessAP)
                {
                    return ni;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the IP address of the Soft AP
        /// </summary>
        /// <returns>IP address</returns>
        public static string GetIpAddress()
        {
            NetworkInterface ni = GetInterface();
            if (ni == null)
            {
                return null;
            }

            return ni.IPv4Address;
        }

        /// <summary>
        /// Gets the name currently configured for the Soft AP, falling back to the default if
        /// none has been set yet.
        /// </summary>
        public static string GetApName()
        {
            WirelessAPConfiguration wapconf = GetConfiguration();
            if (wapconf == null || string.IsNullOrEmpty(wapconf.Ssid))
            {
                return DefaultApSsid;
            }

            return wapconf.Ssid;
        }

        /// <summary>
        /// Persists a new name for the Soft AP. Takes effect the next time Soft AP mode is
        /// (re)configured - it does not rename an already-running AP.
        /// </summary>
        public static void SetApName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            WirelessAPConfiguration wapconf = GetConfiguration();
            if (wapconf == null)
            {
                return;
            }

            // WiFi SSIDs are capped at 32 bytes.
            if (name.Length > 32)
            {
                name = name.Substring(0, 32);
            }

            wapconf.Ssid = name;
            wapconf.SaveConfiguration();
            Debug.WriteLine($"[ap] SetApName: persisted new Soft AP name '{name}'");
        }

    }
}

