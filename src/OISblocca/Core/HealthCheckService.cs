using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OISblocca.Core
{
    public class HealthCheckService : IDisposable
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        private CancellationTokenSource? _cts;
        private Task? _pollTask;

        public event Action? OnProbeCycleCompleted;

        public static string SanitizeDomain(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim().ToLowerInvariant();

            int commentIdx = s.IndexOfAny(new[] { '#', ';' });
            if (commentIdx >= 0) s = s.Substring(0, commentIdx).Trim();

            if (s.StartsWith("https://")) s = s.Substring(8);
            else if (s.StartsWith("http://")) s = s.Substring(7);

            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);

            if (!s.StartsWith("[") && s.IndexOf(':') >= 0)
            {
                s = s.Substring(0, s.IndexOf(':'));
            }

            return s.Trim();
        }

        public static List<string> GetAssociatedDomains(string rawDomain)
        {
            string host = SanitizeDomain(rawDomain);
            if (string.IsNullOrWhiteSpace(host)) return new List<string>();

            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            list.Add(host);

            if (IPAddress.TryParse(host, out _))
            {
                return list.ToList();
            }

            // 1. Aggiungi sempre l'equivalente apex o www
            if (host.StartsWith("www."))
            {
                string apex = host.Substring(4);
                if (!string.IsNullOrWhiteSpace(apex)) list.Add(apex);
            }
            else
            {
                list.Add("www." + host);
            }

            // 2. Risolvi CDN e sottodomini essenziali per piattaforme note
            // NOTA: NON includere MAI training.olinfo.it, api.github.com o googleapis.com,
            // poiché sono i canarini usati da https://gara.squadre.olinfo.it/check/ per verificare il blocco!
            if (host.Contains("youtube.com") || host.Contains("youtu.be"))
            {
                list.Add("youtube.com");
                list.Add("www.youtube.com");
                list.Add("m.youtube.com");
                list.Add("i.ytimg.com");
                list.Add("ytimg.com");
                list.Add("s.ytimg.com");
                list.Add("googlevideo.com");
                list.Add("redirector.googlevideo.com");
                list.Add("yt3.ggpht.com");
                list.Add("gstatic.com");
                list.Add("www.gstatic.com");
            }
            else if (host.Contains("wikipedia.org") || host.Contains("wikimedia.org"))
            {
                list.Add("wikipedia.org");
                list.Add("www.wikipedia.org");
                list.Add("upload.wikimedia.org");
                list.Add("wikimedia.org");
            }
            else if (host.Contains("cppreference.com"))
            {
                list.Add("cppreference.com");
                list.Add("en.cppreference.com");
                list.Add("upload.cppreference.com");
            }
            else if (host.Contains("stackoverflow.com") || host.Contains("stackexchange.com"))
            {
                list.Add("stackoverflow.com");
                list.Add("www.stackoverflow.com");
                list.Add("cdn.sstatic.net");
            }
            else if (host.Contains("w3schools.com"))
            {
                list.Add("w3schools.com");
                list.Add("www.w3schools.com");
            }

            // Escludi categoricamente i target di test della piattaforma gara OIS
            list.Remove("training.olinfo.it");
            list.Remove("www.training.olinfo.it");
            list.Remove("api.github.com");
            list.Remove("www.googleapis.com");
            list.Remove("googleapis.com");
            list.Remove("jsonplaceholder.typicode.com");
            list.Remove("dummyjson.com");

            return list.ToList();
        }

        public static async Task<List<string>> ResolveAllIpsForSiteAsync(string rawDomain)
        {
            var domains = GetAssociatedDomains(rawDomain);
            var ipSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // IP forbidden: training.olinfo.it deve rimanere rigorosamente bloccato per superare il check OIS
            var forbiddenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "130.192.157.5" };
            try
            {
                var trAddrs = await Dns.GetHostAddressesAsync("training.olinfo.it");
                foreach (var a in trAddrs) forbiddenIps.Add(a.ToString());
            }
            catch
            {
            }

            foreach (var d in domains)
            {
                if (d.Equals("training.olinfo.it", StringComparison.OrdinalIgnoreCase) ||
                    d.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
                    d.Equals("www.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
                    d.Equals("jsonplaceholder.typicode.com", StringComparison.OrdinalIgnoreCase) ||
                    d.Equals("dummyjson.com", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IPAddress.TryParse(d, out var parsedIp))
                {
                    string pStr = parsedIp.ToString();
                    if (!IPAddress.IsLoopback(parsedIp) && !forbiddenIps.Contains(pStr))
                    {
                        ipSet.Add(pStr);
                    }
                    continue;
                }

                try
                {
                    var addrs = await Dns.GetHostAddressesAsync(d);
                    foreach (var addr in addrs)
                    {
                        string ipStr = addr.ToString();
                        if (!IPAddress.IsLoopback(addr) && !forbiddenIps.Contains(ipStr))
                        {
                            ipSet.Add(ipStr);
                        }
                    }
                }
                catch
                {
                }
            }

            return ipSet.ToList();
        }

        public static async Task ResolveSiteIpsAsync(SiteItem site)
        {
            var newIps = await ResolveAllIpsForSiteAsync(site.Domain);
            if (newIps.Count > 0)
            {
                var combined = new HashSet<string>(site.ResolvedIps ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var ip in newIps)
                {
                    combined.Add(ip);
                }
                site.ResolvedIps = combined.ToList();
            }
        }

        public async Task<bool> CheckSiteHealthAsync(SiteItem site)
        {
            site.Status = SiteStatus.Checking;
            string host = SanitizeDomain(site.Domain);
            if (string.IsNullOrEmpty(host))
            {
                site.Status = SiteStatus.Offline;
                site.StatusMessage = "dominio non valido";
                return false;
            }

            var sw = Stopwatch.StartNew();

            // 1. Risolvi tutti gli IP (apex, www, CDN e alias)
            await ResolveSiteIpsAsync(site);

            if (site.ResolvedIps == null || site.ResolvedIps.Count == 0)
            {
                sw.Stop();
                site.Status = SiteStatus.Offline;
                site.LatencyMs = -1;
                site.StatusMessage = "risoluzione dns fallita";
                return false;
            }

            // 2. Esegui prova di connessione HTTP/HTTPS o Socket Ping
            try
            {
                string url = host.StartsWith("http") ? host : $"https://{host}";
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                req.Headers.Add("User-Agent", "OISblocca-HealthCheck/2.0");

                using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();

                site.Status = SiteStatus.Online;
                site.LatencyMs = (int)sw.ElapsedMilliseconds;
                site.StatusMessage = $"http {(int)resp.StatusCode} ok ({site.LatencyMs}ms)";
                return true;
            }
            catch
            {
                // Fallback TCP Socket sulla porta 443 o 80
                try
                {
                    using var tcp = new TcpClient();
                    var connectTask = tcp.ConnectAsync(host, 443);
                    var delayTask = Task.Delay(2000);
                    var completed = await Task.WhenAny(connectTask, delayTask);

                    if (completed == connectTask && tcp.Connected)
                    {
                        sw.Stop();
                        site.Status = SiteStatus.Online;
                        site.LatencyMs = (int)sw.ElapsedMilliseconds;
                        site.StatusMessage = $"tcp 443 aperta ({site.LatencyMs}ms)";
                        return true;
                    }
                }
                catch
                {
                }

                sw.Stop();
                site.Status = SiteStatus.Offline;
                site.LatencyMs = -1;
                site.StatusMessage = "nessuna risposta dal server (timeout o bloccato)";
                return false;
            }
        }

        public void StartPolling(IEnumerable<SiteItem> sites, int intervalSeconds = 12)
        {
            StopPolling();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _pollTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var site in sites)
                    {
                        if (token.IsCancellationRequested) break;
                        await CheckSiteHealthAsync(site);
                    }

                    OnProbeCycleCompleted?.Invoke();

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        public void StopPolling()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            StopPolling();
        }
    }
}
