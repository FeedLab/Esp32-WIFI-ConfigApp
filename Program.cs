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
        private const int SetupPin = 5;

        // Guard delay held at the very start of every boot. Provisioning triggers one or
        // more reboots, and without an idle window here the device reboots faster than the
        // deployment tool (Visual Studio / nanoff) can attach - which is what forces a
        // "--masserase" to recover. Keeping the CLR idle briefly guarantees a deploy window
        // on every boot, even during a reboot loop.
        private const int DeployGuardDelayMs = 5000;

        public static void Main()
        {
            Debug.WriteLine("Welcome to WiFI Soft AP world!");
            Debug.WriteLine($"[boot] free memory: {nanoFramework.Runtime.Native.GC.Run(false)} bytes");

            // Give the deployment tool a chance to connect before any reboot/network logic runs.
            Thread.Sleep(DeployGuardDelayMs);

            var gpioController = new GpioController();
            GpioPin setupButton = gpioController.OpenPin(SetupPin, PinMode.InputPullUp);

            PinValue buttonState = setupButton.Read();
            Debug.WriteLine($"[boot] setup button pin state: {buttonState}");

            // If Wireless station is not enabled then start Soft AP to allow Wireless configuration
            // or Button pressed. The pin is pulled up, so it reads Low only while the button is
            // physically held down/grounded; High is the idle (unpressed) state.
            if (buttonState == PinValue.Low)
            {
                Debug.WriteLine("[boot] setup button held - forcing Soft AP setup mode");
                WirelessAP.SetWifiAp();
                wifiApMode = true;
            }
            else
            {
                Debug.WriteLine("[boot] setup button not held - attempting station connect (or Soft AP fallback)");
                wifiApMode = Wireless80211.ConnectOrSetAp();
            }

            Debug.WriteLine($"[boot] resulting mode: {(wifiApMode ? "AccessPoint" : "Station")}");
            Console.WriteLine($"Connected with wifi credentials. IP Address: {(wifiApMode ? WirelessAP.GetIpAddress() : Wireless80211.GetCurrentIPAddress())}");

            // Start the web server in both modes: the setup form in Soft AP mode, and the
            // read-only /info endpoint once connected to the network as a station.
            Debug.WriteLine("[boot] starting web server");
            server.Start(wifiApMode);
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
