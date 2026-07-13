# Esp32-WIFI-ConfigApp

A WiFi setup ("Soft AP") captive portal for .NET nanoFramework devices (developed and tested on an ESP32-C3).

On first boot (or when the setup button on GPIO5 is held), the device starts a Soft AP named
`Sensor-Setup` and serves a configuration page listing nearby WiFi networks. Pick a network (or
type a hidden SSID manually), enter the password, and the device saves the configuration, joins
that network as a station, and reboots into normal mode.

This started as the official [.NET nanoFramework WiFi Soft AP sample](https://github.com/nanoframework/Samples/tree/main/samples/WiFiAP)
and has since been extended with:

- A network scan on the setup page (radio list with signal strength, instead of typing the SSID blind).
- A captive-portal redirect so phones/OSes auto-open the setup page on connect.
- A `/info` JSON endpoint (reachable in both AP and station mode) reporting device/firmware/network info.
- Defensive fixes for the ESP32-C3's very limited RAM (bounded POST body reads, freeing the scan
  cache before the heaviest allocations, forced GC compaction at key points).
- Response-then-connect ordering on save, so the confirmation page reaches the client before the
  WiFi radio switches over to join the new network (which otherwise drops the SoftAP connection
  before the response can be sent).
- A deploy-guard delay at boot so the deployment tool always has a window to attach, even mid
  reboot-loop during provisioning.
- Verbose `[boot]`/`[ap]`/`[sta]`/`[scan]`/`[web]`-tagged debug logging across the whole flow.

## Hardware requirements

A device with WiFi networking capabilities running a nanoFramework image (developed against ESP32-C3).

GPIO pin 5 is used as the setup button (pulled up; grounding it at boot forces Soft AP setup mode).

## Build the sample

1. Open `WiFiAP.sln` in Visual Studio (2019+) or Rider with the nanoFramework extension installed.
2. Restore NuGet packages.
3. Build the solution.

## Run

- Deploy to your device (`Build > Deploy Solution`, or `F5` to debug). Make sure the device is
  visible in the Device Explorer first.
- On boot without the setup button held, if no WiFi is configured yet the device starts the Soft
  AP. Connect to `Sensor-Setup` and browse to `http://192.168.4.1/`.
- Pick a network, enter the password, submit. The device reboots and joins that network.
- Once connected, check your router's DHCP client list (or the debug console's
  `Connected with wifi credentials. IP Address: ...` line) for its new IP, then browse to
  `http://<ip>/info` for device status.

## License

This project's own additions are licensed under the terms in [LICENSE.md](./LICENSE.md) (Apache 2.0).
It is derived from the MIT-licensed nanoFramework sample referenced above — see
[THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for the original license and attribution.
