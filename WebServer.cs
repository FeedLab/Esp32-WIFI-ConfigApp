//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using nanoFramework.Runtime.Native;

namespace WifiAP
{
    public class WebServer
    {
        HttpListener listener;
        Thread serverThread;

        // True when running as the Soft AP setup portal; false when connected as a station.
        private bool _apMode;

        // Networks discovered by the WiFi scan performed at server start-up.
        private static Wireless80211.ScannedNetwork[] _availableNetworks;

        /// <summary>
        /// Starts the web server.
        /// </summary>
        /// <param name="apMode">
        /// <see langword="true"/> to serve the Soft AP setup portal (scans for networks and
        /// serves the configuration form); <see langword="false"/> to serve only the read-only
        /// <c>/info</c> endpoint while connected to a network as a station.
        /// </param>
        public void Start(bool apMode)
        {
            Debug.WriteLine($"[web] Start requested, apMode={apMode}, already running={listener != null}");
            if (listener == null)
            {
                _apMode = apMode;
                listener = new HttpListener("http");
                serverThread = new Thread(RunServer);
                serverThread.Start();
            }
        }

        public void Stop()
        {
            Debug.WriteLine("[web] Stop requested");
            if (listener != null)
                listener.Stop();
        }
        private void RunServer()
        {
            listener.Start();
            Debug.WriteLine($"[web] listener started, apMode={_apMode}, free memory={nanoFramework.Runtime.Native.GC.Run(false)} bytes");

            if (_apMode)
            {
                // Scan for nearby networks once, so the setup page can offer a list. Run it
                // on its own thread so the up-to-8-second scan doesn't delay the listener from
                // accepting connections - the AP is already broadcasting and handing out DHCP
                // leases by this point, so a client could otherwise reach the AP but find the
                // web server unreachable until the scan finished. Only relevant in AP setup
                // mode - scanning while connected as a station would disrupt that connection.
                new Thread(() =>
                {
                    _availableNetworks = Wireless80211.Scan();
                    Debug.WriteLine($"[web] background scan populated {_availableNetworks.Length} network(s)");
                }).Start();
            }

            while (listener.IsListening)
            {
                try
                {
                    var context = listener.GetContext();
                    if (context != null)
                        ProcessRequest(context);
                }
                catch (Exception ex)
                {
                    // Never let a single bad request tear down the listener thread.
                    Debug.WriteLine($"Request processing failed: {ex.Message}");
                }
            }
            listener.Close();
            Debug.WriteLine("[web] listener closed");

            listener = null;
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string ssid = null;
            string password = null;
            bool isApSet = false;

            Debug.WriteLine($"[web] request: {request.HttpMethod} {request.RawUrl}, free memory={nanoFramework.Runtime.Native.GC.Run(false)} bytes");

            if (request.HttpMethod == "POST")
            {
                // The scan results are no longer needed once the form is submitted; drop them
                // now to free memory before parsing/responding on this very constrained device.
                _availableNetworks = null;

                // Form submission (sent as POST so the password is not exposed in the URL).
                // Pick the manually typed SSID (hidden networks) if given, otherwise the one
                // selected from the scanned list.
                Hashtable pars = ParseParamsFromStream(request);
                string selected = UrlDecode((string)pars["ssid"]);
                string manual = UrlDecode((string)pars["ssidManual"]);
                password = UrlDecode((string)pars["password"]);
                if (password == null)
                {
                    // Open network - no password supplied.
                    password = "";
                }

                ssid = string.IsNullOrEmpty(manual) ? selected : manual;

                Debug.WriteLine($"Wireless parameters SSID:{ssid} PASSWORD:{password}");

                // Compact the heap now that the scan cache has been dropped, to maximize free
                // contiguous memory before the allocations needed to build and send the response.
                nanoFramework.Runtime.Native.GC.Run(true);

                // Send the confirmation page now, while the Soft AP link to this client is
                // still up. Joining the new network (below, after the response is sent) takes
                // over the device's single WiFi radio and drops the Soft AP almost immediately -
                // if we tried to connect before responding, the client's connection would be
                // cut before the response could be delivered ("can't reach this page").
                response.ContentType = "text/html";
                OutPutResponse(response, CreateResultPage(
                    "<p>Settings saved. Attempting to connect and reboot the device.</p>" +
                    "<p>If it doesn't rejoin this setup network, check the SSID/password and try again.</p>"));
                isApSet = true;
            }
            else
            {
                string path = request.RawUrl.Split('?')[0];

                if (path == "/favicon.ico")
                {
                    // The bundled favicon is a ~10 KB image and loading it reliably throws
                    // OutOfMemoryException on memory-constrained targets (e.g. ESP32-C3).
                    // The pages suppress the request with <link rel='icon' href='data:,'>,
                    // so just answer any stray request with an empty 404 - no allocation.
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.ContentLength64 = 0;
                }
                else if (path == "/info")
                {
                    // Read-only device information, callable from a PC on the same network.
                    response.ContentType = "application/json";
                    OutPutResponse(response, CreateInfoJson());
                }
                else if (_apMode)
                {
                    if (IsCaptivePortalProbe(request))
                    {
                        // The client's OS is checking for internet access (captive.apple.com,
                        // connectivitycheck.gstatic.com, msftconnecttest.com, ...). Redirect its
                        // browser to the setup page so the config form pops up automatically.
                        RedirectToPortal(response);
                    }
                    else
                    {
                        response.ContentType = "text/html";
                        OutPutResponse(response, CreateSetupPage());
                    }
                }
                else
                {
                    // Station mode: no setup UI, expose the device info instead.
                    response.ContentType = "application/json";
                    OutPutResponse(response, CreateInfoJson());
                }
            }

            response.Close();
            Debug.WriteLine("[web] response closed");

            if (isApSet && !string.IsNullOrEmpty(ssid))
            {
                // Give the client's TCP stack a moment to actually receive the response
                // before the radio switches over to join the new network.
                Thread.Sleep(300);

                // Now attempt the station connection and reboot into normal mode. Any
                // Soft AP client (including the one that just submitted this form) will
                // lose its connection to the device at this point - that is expected.
                Debug.WriteLine($"[web] connecting to '{ssid}' as station before reboot");
                bool connected = Wireless80211.Configure(ssid, password);
                Debug.WriteLine($"[web] station connect result: {connected}");

                WirelessAP.Disable();
                Debug.WriteLine("[web] rebooting into normal mode");
                Thread.Sleep(200);
                Power.RebootDevice();
            }
        }

        /// <summary>
        /// Builds the WiFi setup page with a selectable list of the scanned networks (SSID on
        /// the left, signal meter on the right) plus a manual entry field for hidden networks.
        /// </summary>
        static string CreateSetupPage()
        {
            string networkList = BuildNetworkList();

            return "<!DOCTYPE html><html><head>" +
                   "<meta charset='utf-8'>" +
                   "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
                   "<link rel='icon' href='data:,'>" +
                   "<title>WiFi Setup</title>" +
                   "<style>" +
                   "body{font-family:sans-serif;font-size:16px;margin:0;padding:24px;background:#f2f2f2;}" +
                   ".card{max-width:420px;margin:0 auto;background:#fff;padding:20px;border-radius:10px;" +
                   "box-shadow:0 1px 4px rgba(0,0,0,.2);}" +
                   "h2{margin-top:0;}" +
                   "label{display:block;margin:14px 0 4px;font-weight:bold;}" +
                   "input{width:100%;box-sizing:border-box;padding:10px;font-size:16px;" +
                   "border:1px solid #ccc;border-radius:6px;}" +
                   ".nets{border:1px solid #ccc;border-radius:6px;overflow:hidden;}" +
                   ".net{display:flex;align-items:center;gap:10px;padding:10px;" +
                   "border-bottom:1px solid #eee;cursor:pointer;font-weight:normal;margin:0;}" +
                   ".net:last-child{border-bottom:0;}" +
                   ".net input{width:auto;margin:0;flex:none;}" +
                   ".nm{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}" +
                   ".sig{font-family:monospace;letter-spacing:2px;color:#0a7aa7;}" +
                   ".empty{padding:10px;color:#888;}" +
                   "button{width:100%;margin-top:20px;padding:12px;font-size:16px;border:0;" +
                   "border-radius:6px;background:#0a7aa7;color:#fff;}" +
                   "</style></head><body><div class='card'>" +
                   "<h2>WiFi Setup</h2>" +
                   "<form action='/save' method='POST'>" +
                   "<label>Network</label>" +
                   "<div class='nets'>" + networkList + "</div>" +
                   "<label>Or enter SSID (hidden networks)</label>" +
                   "<input name='ssidManual' placeholder='optional'>" +
                   "<label>Password</label>" +
                   "<input name='password' type='password'>" +
                   "<button type='submit'>Connect</button>" +
                   "</form></div></body></html>";
        }

        /// <summary>
        /// Builds the selectable network rows from the last scan. Each row is a flexbox with
        /// the SSID on the left and the signal meter pushed to the right edge.
        /// </summary>
        static string BuildNetworkList()
        {
            Wireless80211.ScannedNetwork[] networks = _availableNetworks;
            if (networks == null || networks.Length == 0)
            {
                return "<div class='empty'>No networks found - use manual entry below.</div>";
            }

            string rows = "";
            foreach (Wireless80211.ScannedNetwork network in networks)
            {
                string encoded = HtmlEncode(network.Ssid);
                rows += "<label class='net'>" +
                        "<input type='radio' name='ssid' value='" + encoded + "'>" +
                        "<span class='nm'>" + encoded + "</span>" +
                        "<span class='sig'>" + SignalMeter(network.SignalBars) + "</span>" +
                        "</label>";
            }

            return rows;
        }

        /// <summary>
        /// Renders a signal strength as filled/empty blocks (bars are 0-4).
        /// </summary>
        static string SignalMeter(int bars)
        {
            if (bars < 0) bars = 0;
            if (bars > 4) bars = 4;

            string meter = "";
            for (int i = 0; i < 4; i++)
            {
                // &#9608; = full block, &#9617; = light shade.
                meter += i < bars ? "&#9608;" : "&#9617;";
            }

            return meter;
        }

        /// <summary>
        /// Builds a small JSON document describing the device, served from <c>/info</c>.
        /// </summary>
        string CreateInfoJson()
        {
            NetworkInterface ni = GetActiveInterface(_apMode);
            string ip = ni == null ? "" : ni.IPv4Address;
            string mac = (ni == null || ni.PhysicalAddress == null)
                ? ""
                : BitConverter.ToString(ni.PhysicalAddress);

            string ssid = "";
            try
            {
                Wireless80211Configuration wconf = Wireless80211.GetConfiguration();
                if (wconf != null)
                {
                    ssid = wconf.Ssid;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not read WiFi configuration: {ex.Message}");
            }

            // GC.Run returns the free memory in bytes after collection.
            uint freeMemory = nanoFramework.Runtime.Native.GC.Run(false);

            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"mode\":\"" + (_apMode ? "AccessPoint" : "Station") + "\",");
            sb.Append("\"ipAddress\":" + JsonString(ip) + ",");
            sb.Append("\"macAddress\":" + JsonString(mac) + ",");
            sb.Append("\"ssid\":" + JsonString(ssid) + ",");
            sb.Append("\"platform\":" + JsonString(SystemInfo.Platform) + ",");
            sb.Append("\"target\":" + JsonString(SystemInfo.TargetName) + ",");
            sb.Append("\"oem\":" + JsonString(SystemInfo.OEMString) + ",");
            sb.Append("\"firmwareVersion\":" +
                      JsonString(SystemInfo.Version == null ? "" : SystemInfo.Version.ToString()) + ",");
            sb.Append("\"freeMemoryBytes\":" + freeMemory.ToString());
            sb.Append("}");

            return sb.ToString();
        }

        /// <summary>
        /// Finds the network interface for the current mode (Soft AP or station).
        /// </summary>
        static NetworkInterface GetActiveInterface(bool apMode)
        {
            NetworkInterfaceType wanted = apMode
                ? NetworkInterfaceType.WirelessAP
                : NetworkInterfaceType.Wireless80211;

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == wanted)
                {
                    return ni;
                }
            }

            return null;
        }

        /// <summary>
        /// Encodes a string as a quoted, escaped JSON string literal.
        /// </summary>
        static string JsonString(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u00");
                            sb.Append(HexDigit((c >> 4) & 0xF));
                            sb.Append(HexDigit(c & 0xF));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');

            return sb.ToString();
        }

        static char HexDigit(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }

        /// <summary>
        /// Simple confirmation page shown after the settings are saved.
        /// </summary>
        static string CreateResultPage(string message)
        {
            return "<!DOCTYPE html><html><head>" +
                   "<meta charset='utf-8'>" +
                   "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
                   "<link rel='icon' href='data:,'>" +
                   "<title>WiFi Setup</title></head>" +
                   "<body style='font-family:sans-serif;padding:24px;'>" +
                   "<h2>NanoFramework</h2>" + message +
                   "</body></html>";
        }

        /// <summary>
        /// Determines whether a GET request is an operating-system connectivity check
        /// (captive-portal probe) rather than a request addressed to the setup page itself.
        /// </summary>
        static bool IsCaptivePortalProbe(HttpListenerRequest request)
        {
            // request.Url is not supported in nanoFramework, so inspect the Host header.
            string host = request.Headers == null ? null : request.Headers["Host"];
            if (string.IsNullOrEmpty(host))
            {
                host = request.UserHostName;
            }

            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            // Strip an optional ":port" suffix.
            int portSeparator = host.IndexOf(':');
            if (portSeparator >= 0)
            {
                host = host.Substring(0, portSeparator);
            }

            // Anything not addressed to the AP itself is treated as an external probe.
            return host != WirelessAP.SoftApIpAddress;
        }

        /// <summary>
        /// Sends an HTTP 302 redirect pointing the client's browser at the setup page.
        /// </summary>
        static void RedirectToPortal(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.Redirect;
            response.RedirectLocation = $"http://{WirelessAP.SoftApIpAddress}/";
            response.ContentLength64 = 0;
        }

        static void OutPutResponse(HttpListenerResponse response, string responseString)
        {
            OutPutByteResponse(response, System.Text.Encoding.UTF8.GetBytes(responseString));
        }

        static void OutPutByteResponse(HttpListenerResponse response, Byte[] responseBytes)
        {
            response.ContentLength64 = responseBytes.Length;
            response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
        }

        // The setup form only ever sends a few short fields; anything beyond this is not a
        // request this device needs to service, and reading it would risk a large allocation
        // on a target with very little free RAM.
        const int MaxFormBodyBytes = 1024;

        static Hashtable ParseParamsFromStream(HttpListenerRequest request)
        {
            long contentLength = request.ContentLength64;
            if (contentLength <= 0 || contentLength > MaxFormBodyBytes)
            {
                Debug.WriteLine($"Rejecting POST body of {contentLength} bytes");
                return new Hashtable();
            }

            int length = (int)contentLength;
            byte[] buffer = new byte[length];

            int totalRead = 0;
            while (totalRead < length)
            {
                int read = request.InputStream.Read(buffer, totalRead, length - totalRead);
                if (read <= 0)
                {
                    break;
                }
                totalRead += read;
            }

            return ParseParams(System.Text.Encoding.UTF8.GetString(buffer, 0, totalRead));
        }

        static Hashtable ParseParams(string rawParams)
        {
            Hashtable hash = new Hashtable();

            if (string.IsNullOrEmpty(rawParams))
            {
                return hash;
            }

            string[] parPairs = rawParams.Split('&');
            foreach (string pair in parPairs)
            {
                string[] nameValue = pair.Split('=');
                if (nameValue.Length == 2 && !hash.Contains(nameValue[0]))
                {
                    hash.Add(nameValue[0], nameValue[1]);
                }
            }

            return hash;
        }

        /// <summary>
        /// Decodes an application/x-www-form-urlencoded value ('+' to space and %XX escapes).
        /// </summary>
        static string UrlDecode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '+')
                {
                    sb.Append(' ');
                }
                else if (c == '%' && i + 2 < value.Length)
                {
                    int hi = HexValue(value[i + 1]);
                    int lo = HexValue(value[i + 2]);
                    if (hi >= 0 && lo >= 0)
                    {
                        sb.Append((char)((hi << 4) | lo));
                        i += 2;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>
        /// Minimal HTML-attribute encoding so scanned SSIDs cannot break the markup.
        /// </summary>
        static string HtmlEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }
    }
}
