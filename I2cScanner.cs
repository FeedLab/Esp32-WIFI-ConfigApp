using System;
using System.Device.I2c;

namespace WifiAP
{
    public class I2cScanner
    {
        public static void ScanBus(int busId)
        {
            Console.WriteLine($"Scanning I2C bus {busId}...");

            int found = 0;

            for (int address = 0x08; address <= 0x77; address++)
            {
                var settings = new I2cConnectionSettings(busId, address);

                try
                {
                    using (var device = I2cDevice.Create(settings))
                    {
                        // Address probe: write a single dummy byte. The driver only
                        // needs the slave to ACK the address byte itself - it doesn't
                        // matter whether the device does anything useful with the data.
                        // This is the standard technique because writes reliably surface
                        // a NACK as an exception, unlike reads on an empty bus, which can
                        // silently "succeed" with a zero-filled buffer.
                        device.WriteByte(0x00);

                        Console.WriteLine($"Found device at 0x{address:X2}");
                        found++;
                    }
                }
                catch (Exception)
                {
                    // Expected for every address with nothing listening - NACK/timeout.
                    // If you need to debug a specific address, temporarily log ex.Message here.
                }
            }

            Console.WriteLine($"Scan complete. {found} device(s) found.");
        }
    }
}