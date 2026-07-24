//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Device.Gpio;
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
            Log.Debug("Welcome to WiFI Soft AP world!");
            Log.Debug($"[boot] free memory: {nanoFramework.Runtime.Native.GC.Run(false)} bytes");

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
                    Log.Info("Button pressed!");
                    Log.Debug("[boot] setup button held - forcing Soft AP setup mode");
                    WirelessAP.SetWifiAp(forceReconfigure: true);
                    wifiApMode = true;
                }
                else if (args.ChangeType == PinEventTypes.Rising)
                {
                    Log.Info("Button released!");
                }
            };

            // ValueChanged only fires on a transition. If the button is already held down from
            // before boot, the pin reads Low the moment it's opened and no Falling edge ever
            // occurs, so the check above alone would miss it - check the level directly too.
            if (setupButton.Read() == PinValue.Low)
            {
                Log.Info("Button held at boot!");
                Log.Debug("[boot] setup button already held at boot - entering Soft AP setup mode");

                // Don't force a reconfigure/reboot here: we just booted, so a fresh AP is not
                // needed. If the AP is already configured with the wanted settings, this brings
                // it up without another reboot - otherwise, holding the button through the first
                // config-writing reboot would keep re-triggering this same branch every time,
                // forcing another reboot, forever, for as long as the button stays held.
                WirelessAP.SetWifiAp();
                wifiApMode = true;
            }
            else
            {
                Log.Debug("[boot] Attempting station connect (or Soft AP fallback)");
                wifiApMode = Wireless80211.ConnectOrSetAp();
            }

            Log.Debug($"[boot] resulting mode: {(wifiApMode ? "AccessPoint" : "Station")}");
            Log.Info($"Connected with wifi credentials. IP Address: {(wifiApMode ? WirelessAP.GetIpAddress() : Wireless80211.GetCurrentIPAddress())}");

            if (wifiApMode)
            {
                StatusLed.ShowWifiMode(true);

                // Don't start the web server yet - wait for a station to actually connect
                // first (see NetworkChange_NetworkAPStationChanged below). The network scan
                // that used to run as part of this start-up has been moved earlier, into
                // WirelessAP.SetWifiAp, before the AP's beacon comes up at all - see the
                // comment there for why running it concurrently with the beacon was risky.
                Log.Debug("[boot] AP mode - deferring web server until a station connects");
                NetworkChange.NetworkAPStationChanged += NetworkChange_NetworkAPStationChanged;
            }
            else
            {
                StatusLed.ShowWifiMode(false);

                // Already a connected station - safe to start the read-only /info endpoint now.
                Log.Debug("[boot] starting web server");
                server.Start(false);

                Log.Debug("[boot] loading configured equipment");
                EquipmentLoader.LoadAndConfigure();
            }
            Log.Debug($"[boot] free memory after start-up: {nanoFramework.Runtime.Native.GC.Run(false)} bytes");

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
            Log.Debug($"[ap-event] NetworkAPStationChanged Index:{NetworkIndex} Connected:{e.IsConnected} Station:{e.StationIndex} connectedCount:{connectedCount}");

            // if connected then get information on the connecting station
            if (e.IsConnected)
            {
                WirelessAPConfiguration wapconf = WirelessAPConfiguration.GetAllWirelessAPConfigurations()[0];
                WirelessAPStation station = wapconf.GetConnectedStations(e.StationIndex);

                string macString = BitConverter.ToString(station.MacAddress);
                Log.Debug($"[ap-event] Station mac {macString} Rssi:{station.Rssi} PhyMode:{station.PhyModes} ");

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
                    Log.Debug("[ap-event] first station connected - starting web server after settle delay");
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
                        Log.Debug("[ap-event] last station disconnected - stopping web server");
                        server.Stop();
                    }
                }
            }

        }
    }
}
