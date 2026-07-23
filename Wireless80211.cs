//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections;
using System.Device.Wifi;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using nanoFramework.Networking;

namespace WifiAP
{
    class Wireless80211
    {
        // Signalled when an asynchronous scan completes.
        private static ManualResetEvent _scanCompleted;

        // Short delay to let the CLR finish booting before the network stack is initialised;
        // connecting too early can fail on some targets.
        private const int NetworkStartupDelayMs = 1000;

        /// <summary>
        /// A WiFi network discovered by a scan.
        /// </summary>
        public class ScannedNetwork
        {
            public ScannedNetwork(string ssid, int signalBars)
            {
                Ssid = ssid;
                SignalBars = signalBars;
            }

            /// <summary>Network name.</summary>
            public string Ssid { get; set; }

            /// <summary>Signal strength as a number of bars (0-4).</summary>
            public int SignalBars { get; set; }
        }

        /// <summary>
        /// Scans for nearby WiFi networks and returns the unique, non-empty ones
        /// in the order reported by the adapter.
        /// </summary>
        /// <returns>
        /// The discovered networks, or an empty array if the scan failed or found nothing.
        /// </returns>
        /// <remarks>
        /// The scan uses the station side of the WiFi radio. Whether it works while the
        /// Soft AP is active depends on the device firmware (ESP32 supports AP+STA scanning),
        /// and a connected client may see a brief interruption while the scan runs. Call this
        /// before clients connect, and treat the result as best-effort: the caller should always
        /// keep a manual SSID entry option so setup still works when scanning returns nothing.
        /// </remarks>
        public static ScannedNetwork[] Scan()
        {
            Debug.WriteLine("[scan] starting WiFi scan");
            WifiAdapter adapter = WifiAdapter.FindAllAdapters()[0];

            _scanCompleted = new ManualResetEvent(false);
            adapter.AvailableNetworksChanged += Adapter_AvailableNetworksChanged;

            try
            {
                adapter.ScanAsync();

                // Wait for the scan-complete event, capped so the web server is never blocked
                // indefinitely if the scan is not signalled.
                bool signalled = _scanCompleted.WaitOne(8000, false);
                Debug.WriteLine($"[scan] wait completed, signalled={signalled}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WiFi scan failed: {ex.Message}");
                adapter.AvailableNetworksChanged -= Adapter_AvailableNetworksChanged;
                return new ScannedNetwork[0];
            }

            adapter.AvailableNetworksChanged -= Adapter_AvailableNetworksChanged;

            WifiNetworkReport report = adapter.NetworkReport;
            if (report == null || report.AvailableNetworks == null)
            {
                Debug.WriteLine("[scan] no network report available");
                return new ScannedNetwork[0];
            }

            Debug.WriteLine($"[scan] raw networks reported: {report.AvailableNetworks.Length}");

            WifiAvailableNetwork[] networks = report.AvailableNetworks;

            // Deduplicate SSIDs (an AP can appear on multiple bands/BSSIDs) preserving order,
            // keeping the strongest signal seen for each name.
            ArrayList found = new ArrayList();
            foreach (WifiAvailableNetwork network in networks)
            {
                string ssid = network.Ssid;
                if (string.IsNullOrEmpty(ssid))
                {
                    continue;
                }

                int bars = network.SignalBars;

                ScannedNetwork existing = null;
                foreach (ScannedNetwork candidate in found)
                {
                    if (candidate.Ssid == ssid)
                    {
                        existing = candidate;
                        break;
                    }
                }

                if (existing == null)
                {
                    found.Add(new ScannedNetwork(ssid, bars));
                }
                else if (bars > existing.SignalBars)
                {
                    existing.SignalBars = bars;
                }
            }

            ScannedNetwork[] result = new ScannedNetwork[found.Count];
            for (int i = 0; i < found.Count; i++)
            {
                result[i] = (ScannedNetwork)found[i];
            }

            Debug.WriteLine($"[scan] finished, unique SSIDs found: {result.Length}");
            return result;
        }

        private static void Adapter_AvailableNetworksChanged(WifiAdapter sender, object e)
        {
            _scanCompleted.Set();
        }
        /// <summary>
        /// Checks if the wireless 802.11 interface is enabled.
        /// </summary>
        /// <returns>
        /// Returns true if the wireless 802.11 interface is enabled (i.e., the SSID is not null or empty), 
        /// otherwise returns false.
        /// </returns>
        public static bool IsEnabled()
        {
            Wireless80211Configuration wconf = GetConfiguration();
            return !string.IsNullOrEmpty(wconf.Ssid);
        }

        /// <summary>
        /// Get current IP address. Only valid if successfully provisioned and connected
        /// </summary>
        /// <returns>IP address string</returns>
        public static string GetCurrentIPAddress()
        {
            NetworkInterface ni = NetworkInterface.GetAllNetworkInterfaces()[0];

            // get first NI ( Wifi on ESP32 )
            return ni.IPv4Address.ToString();
        }

        /// <summary>
        /// Coonnects to the Wifi or sets the Access Point mode.
        /// </summary>
        /// <returns>True if access point is setup.</returns>
        public static bool ConnectOrSetAp()
        {
            bool enabled = IsEnabled();
            Debug.WriteLine($"[sta] ConnectOrSetAp: station configured (IsEnabled)={enabled}");

            if (enabled)
            {
                Debug.WriteLine("Wireless client activated");

                // Give the CLR a moment to finish booting before initialising the network
                // stack, otherwise the connect can fail this early in start-up.
                Thread.Sleep(NetworkStartupDelayMs);

                Debug.WriteLine("[sta] attempting Reconnect (30s timeout)");
                bool reconnected = WifiNetworkHelper.Reconnect(true, token: new CancellationTokenSource(30_000).Token);
                Debug.WriteLine($"[sta] Reconnect result: {reconnected}");

                if (!reconnected)
                {
                    Debug.WriteLine("[sta] Reconnect failed - falling back to Soft AP setup mode");
                    WirelessAP.SetWifiAp();
                    return true;
                }
            }
            else
            {
                Debug.WriteLine("[sta] no station configuration saved - entering Soft AP setup mode");
                WirelessAP.SetWifiAp();
                return true;
            }

            Debug.WriteLine($"[sta] connected as station, IP: {GetCurrentIPAddress()}");
            return false;
        }

        /// <summary>
        /// Disable the Wireless station interface.
        /// </summary>
        public static void Disable()
        {
            Wireless80211Configuration wconf = GetConfiguration();
            wconf.Options = Wireless80211Configuration.ConfigurationOptions.None | Wireless80211Configuration.ConfigurationOptions.SmartConfig;

            // IsEnabled() (used by ConnectOrSetAp() to decide whether to even attempt a station
            // reconnect) checks for a saved SSID, not the Options flags above - clearing only
            // Options left the credentials in place, so the very next boot's ConnectOrSetAp()
            // saw IsEnabled()==true and reconnected to the old network anyway, undoing the
            // switch to Soft AP mode.
            wconf.Ssid = "";
            wconf.Password = "";
            wconf.SaveConfiguration();
        }

        /// <summary>
        /// Configure and enable the Wireless station interface
        /// </summary>
        /// <param name="ssid"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static bool Configure(string ssid, string password)
        {
            Debug.WriteLine($"[sta] Configure: ssid={ssid}, free memory before={nanoFramework.Runtime.Native.GC.Run(false)} bytes");

            // Make sure we are disconnected before we start connecting otherwise
            // ConnectDhcp will just return success instead of reconnecting.
            WifiAdapter wa = WifiAdapter.FindAllAdapters()[0];
            wa.Disconnect();
            Debug.WriteLine("[sta] Configure: disconnected existing STA adapter");

            CancellationTokenSource cs = new(30_000);
            Console.WriteLine("ConnectDHCP");
            WifiNetworkHelper.Disconnect();

            // Reconfigure properly the normal wifi
            Wireless80211Configuration wconf = GetConfiguration();
            wconf.Options = Wireless80211Configuration.ConfigurationOptions.AutoConnect | Wireless80211Configuration.ConfigurationOptions.Enable;
            wconf.Ssid = ssid;
            wconf.Password = password;
            wconf.SaveConfiguration();
            Debug.WriteLine("[sta] Configure: station configuration saved");

            WifiNetworkHelper.Disconnect();
            bool success;

            Debug.WriteLine("[sta] Configure: calling ConnectDhcp (30s timeout)");
            success = WifiNetworkHelper.ConnectDhcp(ssid, password, WifiReconnectionKind.Automatic, true, token: cs.Token);
            Debug.WriteLine($"[sta] Configure: ConnectDhcp result: {success}");

            if (!success)
            {
                wa.Disconnect();
                // Bug in network helper, we've most likely try to connect before, let's make it manual
                Debug.WriteLine("[sta] Configure: ConnectDhcp failed - retrying with manual wa.Connect");
                var res = wa.Connect(ssid, WifiReconnectionKind.Automatic, password);
                success = res.ConnectionStatus == WifiConnectionStatus.Success;
                Console.WriteLine($"Connected: {res.ConnectionStatus}");
            }

            Debug.WriteLine($"[sta] Configure: final result={success}, free memory after={nanoFramework.Runtime.Native.GC.Run(false)} bytes");
            return success;
        }

        /// <summary>
        /// Get the Wireless station configuration.
        /// </summary>
        /// <returns>Wireless80211Configuration object</returns>
        public static Wireless80211Configuration GetConfiguration()
        {
            NetworkInterface ni = GetInterface();
            return Wireless80211Configuration.GetAllWireless80211Configurations()[ni.SpecificConfigId];
        }

        public static NetworkInterface GetInterface()
        {
            NetworkInterface[] Interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Find WirelessAP interface
            foreach (NetworkInterface ni in Interfaces)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    return ni;
                }
            }
            return null;
        }
    }
}
