using System;

namespace WifiAP
{
    /// <summary>
    /// Thin logging facade so every line gets a consistent timestamp and call sites don't talk
    /// to Console/Debug directly. <see cref="Info"/> goes through Console.WriteLine (visible
    /// over serial even without a debugger attached); <see cref="Debug"/> goes through
    /// System.Diagnostics.Debug.WriteLine (visible only while a debugger is attached) - matching
    /// how this codebase already used each of those two channels before this class existed.
    /// </summary>
    public static class Log
    {
        // Set once, on first use. nanoFramework has no battery-backed real-time clock without an
        // NTP sync, so timestamps are time-since-first-log-call (hh:mm:ss.fff) rather than
        // wall-clock - still useful for correlating log rows and measuring gaps between them.
        private static readonly DateTime _start = DateTime.UtcNow;

        public static void Info(string message)
        {
            Console.WriteLine(Timestamp() + " [INF] " + message);
        }

        public static void Debug(string message)
        {
            System.Diagnostics.Debug.WriteLine(Timestamp() + " [DBG] " + message);
        }

        /// <summary>
        /// The same time-since-first-log-call timestamp used to prefix log lines, exposed for
        /// anything else on the device that needs a consistent notion of "now" (e.g. sensor
        /// reading payloads) without its own real-time clock.
        /// </summary>
        public static string Now()
        {
            return Timestamp();
        }

        private static string Timestamp()
        {
            TimeSpan t = DateTime.UtcNow - _start;
            return Pad2((int)t.TotalHours) + ":" + Pad2(t.Minutes) + ":" + Pad2(t.Seconds) + "." + Pad3(t.Milliseconds);
        }

        private static string Pad2(int value)
        {
            return value < 10 ? "0" + value : value.ToString();
        }

        private static string Pad3(int value)
        {
            if (value < 10) return "00" + value;
            if (value < 100) return "0" + value;
            return value.ToString();
        }
    }
}
