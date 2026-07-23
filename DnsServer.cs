//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace WifiAP
{
    /// <summary>
    /// Minimal "DNS trap" for the Soft AP captive portal: answers every DNS query, regardless
    /// of hostname or type, with the Soft AP's own IPv4 address. This is what makes phone-OS
    /// captive-portal probes (e.g. captive.apple.com, connectivitycheck.gstatic.com) resolve to
    /// the AP at all - the AP has no real upstream DNS, so without this those probes just time
    /// out and the OS never realizes there's a portal to show.
    /// </summary>
    public static class DnsServer
    {
        private const int DnsPort = 53;
        private const int MaxPacketSize = 512;
        private const int MinQueryLength = 12; // DNS header size

        private static Socket _socket;
        private static Thread _listenThread;
        private static bool _running;
        private static byte[] _apAddressBytes;

        /// <summary>
        /// Starts the DNS trap, answering every query with <paramref name="apIpAddress"/>.
        /// </summary>
        public static void Start(string apIpAddress)
        {
            if (_running)
            {
                Debug.WriteLine("[dns] Start requested but already running");
                return;
            }

            _apAddressBytes = IPAddress.Parse(apIpAddress).GetAddressBytes();

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, DnsPort));

            _running = true;
            _listenThread = new Thread(ListenLoop);
            _listenThread.Start();

            Debug.WriteLine($"[dns] Start: listening on UDP :{DnsPort}, trapping all queries to {apIpAddress}");
        }

        /// <summary>
        /// Stops the DNS trap. Not currently called anywhere - Soft AP mode in this app is only
        /// ever torn down via a full device reboot - but kept for completeness/testability,
        /// matching how the DHCP server's own Stop() is likewise never called today.
        /// </summary>
        public static void Stop()
        {
            if (!_running)
            {
                return;
            }

            Debug.WriteLine("[dns] Stop requested");
            _running = false;
            _socket?.Close(); // unblocks the pending ReceiveFrom in the listen thread
            _socket = null;
        }

        private static void ListenLoop()
        {
            var buffer = new byte[MaxPacketSize];

            while (_running)
            {
                try
                {
                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    int received = _socket.ReceiveFrom(buffer, ref remoteEndPoint);

                    if (received < MinQueryLength)
                    {
                        continue;
                    }

                    byte[] response = BuildResponse(buffer, received);
                    _socket.SendTo(response, response.Length, SocketFlags.None, remoteEndPoint);

                    Debug.WriteLine($"[dns] answered query from {remoteEndPoint}, {received} bytes in");
                }
                catch (Exception ex)
                {
                    // Covers both a malformed/short packet mid-parse and the SocketException
                    // that Stop()'s Close() call raises here - either way, don't let one bad
                    // iteration kill the whole thread. If _running is now false this is just
                    // the Stop()-triggered exit and the loop condition ends things cleanly.
                    if (_running)
                    {
                        Debug.WriteLine($"[dns] request handling failed: {ex.Message}");
                    }
                }
            }

            Debug.WriteLine("[dns] listen thread exiting");
        }

        /// <summary>
        /// Builds a response by echoing the query's header/question verbatim and appending a
        /// single A-record answer pointing at the Soft AP - no domain-name parsing needed.
        /// </summary>
        private static byte[] BuildResponse(byte[] query, int queryLength)
        {
            // Response = echoed header+question (bytes 0..queryLength-1) + one A-record answer.
            var response = new byte[queryLength + 16];
            Array.Copy(query, 0, response, 0, queryLength);

            // ID: response[0..1] already correct (copied from query).

            // Flags: standard response, no error, recursion available.
            response[2] = 0x81;
            response[3] = 0x80;

            // QDCOUNT: response[4..5] already correct (copied from query, normally 0x00 0x01).

            // ANCOUNT = 1.
            response[6] = 0x00;
            response[7] = 0x01;

            // NSCOUNT = 0.
            response[8] = 0x00;
            response[9] = 0x00;

            // ARCOUNT = 0.
            response[10] = 0x00;
            response[11] = 0x00;

            // Answer record, appended right after the echoed question section.
            int offset = queryLength;
            response[offset++] = 0xC0; // NAME: compressed pointer back to
            response[offset++] = 0x0C; // the QNAME at offset 12.
            response[offset++] = 0x00; // TYPE = A
            response[offset++] = 0x01;
            response[offset++] = 0x00; // CLASS = IN
            response[offset++] = 0x01;
            response[offset++] = 0x00; // TTL = 60 seconds
            response[offset++] = 0x00;
            response[offset++] = 0x00;
            response[offset++] = 0x3C;
            response[offset++] = 0x00; // RDLENGTH = 4
            response[offset++] = 0x04;
            Array.Copy(_apAddressBytes, 0, response, offset, 4);

            return response;
        }
    }
}
