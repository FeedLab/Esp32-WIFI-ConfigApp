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
            Log.Debug($"[led] Initialize: pin={NeoPixelPin}, count={LedCount}");
            _pixels = new Ws2812b(NeoPixelPin, LedCount);
            ShowBooting();
        }

        // LED 0 = WiFi Strength
        public static void ShowWifiStrength(int level)
        {
            Log.Debug($"[led] ShowWifiStrength: level={level}");

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
            Log.Debug($"[led] ShowWifiMode: apMode={apMode}");

            StopBlinking();

            if (apMode)
            {
                // Blink orange on LED 0
                Log.Debug("[led] ShowWifiMode: starting blink thread");
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

                    Log.Debug("[led] blink thread exiting");
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
            Log.Debug("[led] StopApBlinking: client connected, holding solid");
            StopBlinking();
            SetColor(0, Color.DarkBlue);
        }

        // LED 2 = Battery Level
        public static void ShowBatteryLevel(int percent)
        {
            Log.Debug($"[led] ShowBatteryLevel: percent={percent}");

            Color c = percent switch
            {
                >= 80 => Color.Green,
                >= 50 => Color.Yellow,
                >= 20 => Color.Orange,
                _ => Color.Red
            };

            SetColor(2, c);
        }

        // Boot-time fault signal: a configured piece of equipment failed to match/create/
        // configure (see EquipmentLoader). Blocks the caller for the duration of the blink
        // burst - acceptable since this only runs once, synchronously, during startup.
        public static void ShowEquipmentError()
        {
            Log.Debug("[led] ShowEquipmentError: blinking all LEDs red");

            const int blinks = 3;
            for (int i = 0; i < blinks; i++)
            {
                SetColor(0, Color.Red);
                SetColor(1, Color.Red);
                SetColor(2, Color.Red);
                Thread.Sleep(300);

                SetColor(0, Color.Black);
                SetColor(1, Color.Black);
                SetColor(2, Color.Black);
                Thread.Sleep(300);
            }
        }

        private static void ShowBooting()
        {
            Log.Debug("[led] ShowBooting: all LEDs red");

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
            Log.Debug("[led] StopBlinking: signalling blink thread to stop");
            _blinking = false;
            _blinkThread = null;
        }

        private static void SetColor(int index, Color color)
        {
            // No per-call logging here: this is invoked every second, indefinitely, by the
            // blink thread while in AP mode. A Log.Debug call with string concatenation on
            // every tick allocates on each call and will exhaust/fragment heap on this
            // memory-constrained device over time - log at the call sites instead.
            _pixels.Image.SetPixel(index, 0, color);
            _pixels.Update();
        }
    }
}
