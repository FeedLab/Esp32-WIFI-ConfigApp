//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
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
        /// The SSID always used for the Soft AP, independent of any device name the user has
        /// set via <see cref="SetDeviceName"/> - that name only affects how the device presents
        /// itself as a station on the user's own router, not the Soft AP's own broadcast name.
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
            Log.Debug("[ap] SetWifiAp starting");

            // Scan now, before the AP's beacon comes up. Scanning uses the station radio, and
            // doing it after the AP is already broadcasting/accepting its first association
            // can knock the beacon out on this hardware before a client ever sees it - doing it
            // here means the scan and the beacon never coexist, and the setup page's network
            // list is already cached and ready by the time any client could possibly connect.
            WebServer.SetAvailableNetworks(Wireless80211.Scan());

            Wireless80211.Disable();

            bool configWritten = ConfigureAp(forceReconfigure);
            Log.Debug($"[ap] ConfigureAp wrote new config: {configWritten}");
            if (configWritten)
            {
                // A new configuration was written; reboot to activate the Access Point on restart.
                Log.Info("Setup Soft AP, Rebooting device");
                Power.RebootDevice();
            }

            Log.Debug($"[ap] starting DHCP server on {SoftAppIp}");
            var dhcpserver = new DhcpServer
            {
                CaptivePortalUrl = $"http://{SoftAppIp}",
                // Without these, clients get an IP but no DNS server/gateway - meaning a
                // phone has nowhere to even send a captive-portal DNS probe, so it never
                // reaches the DnsServer trap below at all.
                DnsServer = IPAddress.Parse(SoftAppIp),
                Gateway = IPAddress.Parse(SoftAppIp)
            };
            var dhcpInitResult = dhcpserver.Start(IPAddress.Parse(SoftAppIp), new IPAddress(new byte[] { 255, 255, 255, 0 }));
            if (!dhcpInitResult)
            {
                Log.Info($@"Error initializing DHCP server.");
                // This happens after a very freshly flashed device
                Log.Debug("[ap] DHCP server failed to start - rebooting");
                Power.RebootDevice();
            }

            Log.Debug($"[ap] starting DNS captive-portal trap on {SoftAppIp}");
            DnsServer.Start(SoftAppIp);

            Log.Info($@"Running Soft AP, waiting for client to connect");
            Log.Info($@"Soft AP IP address :{GetIpAddress()}");
        }

        /// <summary>
        /// Disable the Soft AP for the next restart.
        /// </summary>
        public static void Disable()
        {
            Log.Debug("[ap] Disable: turning off Soft AP for next boot");
            WirelessAPConfiguration wirelessAPConfiguration = GetConfiguration();
            if (wirelessAPConfiguration == null)
            {
                Log.Debug("[ap] Disable: no WirelessAP configuration found, nothing to do");
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
                Log.Debug("[ap] ConfigureAp: no WirelessAP interface found");
                throw new InvalidOperationException("No WirelessAP network interface was found.");
            }

            // The Soft AP always broadcasts the fixed default name - see DefaultApSsid.
            string wantedSsid = DefaultApSsid;

            Log.Debug($"[ap] ConfigureAp: current options={wapconf.Options}, current IP={ni.IPv4Address}, wanted IP={SoftAppIp}, current SSID={wapconf.Ssid}, wanted SSID={wantedSsid}, forceReconfigure={forceReconfigure}");

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
                Log.Debug("[ap] ConfigureAp: already configured, no changes needed");
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

            // Password is deliberately left untouched here - with Open authentication it is
            // never used for the Soft AP itself, so this field is reused as persistent storage
            // for the user's chosen device name (see SetDeviceName/GetDeviceName). Overwriting
            // it here would erase that name every time the Soft AP is (re)configured.

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
        /// Gets the user-chosen device name, or an empty string if none has been set yet.
        /// This is not the Soft AP's own SSID (see <see cref="DefaultApSsid"/>) - it is the name
        /// the device advertises when connecting as a station on the user's own router.
        /// </summary>
        public static string GetDeviceName()
        {
            WirelessAPConfiguration wapconf = GetConfiguration();
            if (wapconf == null)
            {
                return "";
            }

            return wapconf.Password;
        }

        /// <summary>
        /// Persists a new device name, used the next time the device connects as a station (see
        /// <see cref="Wireless80211.Configure"/> and <see cref="Wireless80211.ConnectOrSetAp"/>).
        /// </summary>
        public static void SetDeviceName(string name)
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

            // Matches the setup page's maxlength and WifiAdapter.SetDeviceName's own limit.
            if (name.Length > 32)
            {
                name = name.Substring(0, 32);
            }

            wapconf.Password = name;
            wapconf.SaveConfiguration();
            Log.Debug($"[ap] SetDeviceName: persisted new device name '{name}'");
        }

    }
}

