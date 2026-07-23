//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Iot.Device.DhcpServer;
using nanoFramework.Networking;
using nanoFramework.Runtime.Native;

namespace WifiAP
{
    public class Program
    {
        // Start Simple WebServer
        private static WebServer server = new WebServer();
        private static bool wifiApMode = false;

        // Connected Station count
        private static int connectedCount = 0;

        // GPIO pin used to put device into AP set-up mode
        private const int SetupPin = 2;

        // Guard delay held at the very start of every boot. Provisioning triggers one or
        // more reboots, and without an idle window here the device reboots faster than the
        // deployment tool (Visual Studio / nanoff) can attach - which is what forces a
        // "--masserase" to recover. Keeping the CLR idle briefly guarantees a deploy window
        // on every boot, even during a reboot loop.
        private const int DeployGuardDelayMs = 5000;
        private static DateTime lastPress = DateTime.MinValue;
        private const int debounceMs = 50;

        public static void Main()
        {
            Debug.WriteLine("Welcome to WiFI Soft AP world!");
            Debug.WriteLine($"[boot] free memory: {nanoFramework.Runtime.Native.GC.Run(false)} bytes");

            StatusLed.Initialize();

            // Give the deployment tool a chance to connect before any reboot/network logic runs.
            Thread.Sleep(DeployGuardDelayMs);

            var gpioController = new GpioController();
            GpioPin setupButton = gpioController.OpenPin(SetupPin, PinMode.InputPullUp);

            setupButton.ValueChanged += (sender, args) =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastPress).TotalMilliseconds < debounceMs)
                {
                    // Ignore bounce
                    return;
                }

                if (args.ChangeType == PinEventTypes.Falling)
                {
                    Console.WriteLine("Button pressed!");
                    Debug.WriteLine("[boot] setup button held - forcing Soft AP setup mode");
                    WirelessAP.SetWifiAp(forceReconfigure: true);
                    wifiApMode = true;
                }
                else if (args.ChangeType == PinEventTypes.Rising)
                {
                    Console.WriteLine("Button released!");
                }
            };

            Debug.WriteLine("[boot] Attempting station connect (or Soft AP fallback)");
            wifiApMode = Wireless80211.ConnectOrSetAp();

            Debug.WriteLine($"[boot] resulting mode: {(wifiApMode ? "AccessPoint" : "Station")}");
            Console.WriteLine($"Connected with wifi credentials. IP Address: {(wifiApMode ? WirelessAP.GetIpAddress() : Wireless80211.GetCurrentIPAddress())}");

            if (wifiApMode)
            {
                StatusLed.ShowWifiMode(true);

                // Don't start the web server (and its background WiFi scan) yet. The scan
                // uses the station radio, and running it while the Soft AP is still trying to
                // broadcast/accept its first association can knock out the AP's beacon on some
                // ESP32 targets before a client ever sees it. Wait for a station to actually
                // connect first - see NetworkChange_NetworkAPStationChanged below.
                Debug.WriteLine("[boot] AP mode - deferring web server/scan until a station connects");
                NetworkChange.NetworkAPStationChanged += NetworkChange_NetworkAPStationChanged;
            }
            else
            {
                StatusLed.ShowWifiMode(false);

                // Already a connected station - safe to start the read-only /info endpoint now.
                Debug.WriteLine("[boot] starting web server");
                server.Start(false);
            }
            Debug.WriteLine($"[boot] free memory after start-up: {nanoFramework.Runtime.Native.GC.Run(false)} bytes");

            // Just wait for now
            // Here you would have the reset of your program using the client WiFI link
            Thread.Sleep(Timeout.Infinite);
        }

        /// <summary>
        /// Event handler for Stations connecting or Disconnecting
        /// </summary>
        /// <param name="NetworkIndex">The index of Network Interface raising event</param>
        /// <param name="e">Event argument</param>
        private static void NetworkChange_NetworkAPStationChanged(int NetworkIndex, NetworkAPStationEventArgs e)
        {
            Debug.WriteLine($"[ap-event] NetworkAPStationChanged Index:{NetworkIndex} Connected:{e.IsConnected} Station:{e.StationIndex} connectedCount:{connectedCount}");

            // if connected then get information on the connecting station
            if (e.IsConnected)
            {
                WirelessAPConfiguration wapconf = WirelessAPConfiguration.GetAllWirelessAPConfigurations()[0];
                WirelessAPStation station = wapconf.GetConnectedStations(e.StationIndex);

                string macString = BitConverter.ToString(station.MacAddress);
                Debug.WriteLine($"[ap-event] Station mac {macString} Rssi:{station.Rssi} PhyMode:{station.PhyModes} ");

                connectedCount++;

                // Start web server when it connects otherwise the bind to network will fail as
                // no connected network. Start web server when first station connects
                if (connectedCount == 1)
                {
                    // Stop the LED blink before the scan/web server startup below - both are
                    // memory-heavy, and leaving the blink thread's periodic RMT allocation
                    // running concurrently has caused OutOfMemoryException on this device.
                    StatusLed.StopApBlinking();

                    // Wait for Station to be fully connected before starting web server
                    // other you will get a Network error
                    Debug.WriteLine("[ap-event] first station connected - starting web server after settle delay");
                    Thread.Sleep(2000);
                    server.Start(true);
                }
            }
            else
            {
                // Station disconnected. When no more station connected then stop web server
                if (connectedCount > 0)
                {
                    connectedCount--;
                    if (connectedCount == 0)
                    {
                        Debug.WriteLine("[ap-event] last station disconnected - stopping web server");
                        server.Stop();
                    }
                }
            }

        }
    }
}
