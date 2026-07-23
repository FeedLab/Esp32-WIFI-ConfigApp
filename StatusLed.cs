using System.Diagnostics;
using System.Drawing;
using System.Threading;
using Iot.Device.Ws28xx.Esp32;

namespace WifiAP
{
    public static class StatusLed
    {
        private const int NeoPixelPin = 3;
        private const int LedCount = 3; // WiFi Strength, WiFi Mode, Battery Level
        private const int BlinkIntervalMs = 1000;

        private static Ws2812b _pixels;
        private static Thread _blinkThread;
        private static bool _blinking;

        public static void Initialize()
        {
            Debug.WriteLine($"[led] Initialize: pin={NeoPixelPin}, count={LedCount}");
            _pixels = new Ws2812b(NeoPixelPin, LedCount);
            ShowBooting();
        }

        // LED 0 = WiFi Strength
        public static void ShowWifiStrength(int level)
        {
            Debug.WriteLine($"[led] ShowWifiStrength: level={level}");

            // Example: map strength to color
            Color c = level switch
            {
                0 => Color.Red,
                1 => Color.Orange,
                2 => Color.Yellow,
                3 => Color.Green,
                _ => Color.Black
            };

            SetColor(0, c);
        }

        // LED 1 = WiFi Mode
        public static void ShowWifiMode(bool apMode)
        {
            Debug.WriteLine($"[led] ShowWifiMode: apMode={apMode}");

            StopBlinking();

            if (apMode)
            {
                // Blink orange on LED 0
                Debug.WriteLine("[led] ShowWifiMode: starting blink thread");
                _blinking = true;
                _blinkThread = new Thread(() =>
                {
                    bool on = false;
                    while (_blinking)
                    {
                        on = !on;
                        SetColor(0, on ? Color.DarkBlue : Color.Black);
                        Thread.Sleep(BlinkIntervalMs);
                    }

                    Debug.WriteLine("[led] blink thread exiting");
                });
                _blinkThread.Start();
            }
            else
            {
                // Solid orange for station mode
                SetColor(0, Color.DarkBlue);
            }
        }

        // Stop blinking and hold LED 0 solid. Call this once a client has actually associated
        // with the Soft AP - blinking's only purpose was to signal "waiting for a client," and
        // every blink tick allocates a fresh RMT command buffer (Ws28xx.Update() rebuilds it
        // from scratch). Leaving that running while the WiFi scan and web server start up -
        // the highest memory-pressure moment of the whole boot - has caused OutOfMemoryException
        // on this device's tiny heap.
        public static void StopApBlinking()
        {
            Debug.WriteLine("[led] StopApBlinking: client connected, holding solid");
            StopBlinking();
            SetColor(0, Color.DarkBlue);
        }

        // LED 2 = Battery Level
        public static void ShowBatteryLevel(int percent)
        {
            Debug.WriteLine($"[led] ShowBatteryLevel: percent={percent}");

            Color c = percent switch
            {
                >= 80 => Color.Green,
                >= 50 => Color.Yellow,
                >= 20 => Color.Orange,
                _ => Color.Red
            };

            SetColor(2, c);
        }

        private static void ShowBooting()
        {
            Debug.WriteLine("[led] ShowBooting: all LEDs red");

            // Boot LED red
            SetColor(0, Color.Red);
            SetColor(1, Color.Red);
            SetColor(2, Color.Red);
        }

        private static void StopBlinking()
        {
            if (_blinkThread == null)
            {
                return;
            }

            // Signal only - don't Join(). This is called from NetworkChange_NetworkAPStationChanged,
            // which runs on nanoFramework's shared event-dispatch thread; blocking there with
            // Join() throws InvalidOperationException (and would stall all event delivery even
            // if it didn't). The blink thread notices _blinking==false and exits on its own
            // within one BlinkIntervalMs tick.
            Debug.WriteLine("[led] StopBlinking: signalling blink thread to stop");
            _blinking = false;
            _blinkThread = null;
        }

        private static void SetColor(int index, Color color)
        {
            // No per-call logging here: this is invoked every second, indefinitely, by the
            // blink thread while in AP mode. A Debug.WriteLine with string interpolation on
            // every tick allocates on each call and will exhaust/fragment heap on this
            // memory-constrained device over time - log at the call sites instead.
            _pixels.Image.SetPixel(index, 0, color);
            _pixels.Update();
        }
    }
}
