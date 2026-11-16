using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace OISblocca.Core
{
    public static class FirewallService
    {
        private static string RulesTrackerFile => Path.Combine(SecurityService.AppDataFolder, "rules_registry.txt");
        private static string IpsTrackerFile => Path.Combine(SecurityService.AppDataFolder, "ips_registry.txt");
        private static HashSet<string> _currentlyAppliedIps = new(StringComparer.OrdinalIgnoreCase);

        public static async Task FlushDnsCacheAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ipconfig.exe",
                        Arguments = "/flushdns",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(3000);
                }
                catch
                {
                }
            });
        }

        public static bool IsLockedDown()
        {
            // 1. Controlla file di stato moderno
            try
            {
                if (File.Exists(SecurityService.StateFilePath))
                {
                    string state = File.ReadAllText(SecurityService.StateFilePath).Trim();
                    if (state.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            // 2. Controlla file di stato legacy (%TEMP%\OISblocca.txt)
            try
            {
                if (File.Exists(SecurityService.LegacyTempStatePath))
                {
                    string state = File.ReadAllText(SecurityService.LegacyTempStatePath).Trim();
                    if (state.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            // 3. Verifica policy effettiva Windows Firewall
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "advfirewall show currentprofile firewallpolicy",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    if (output.IndexOf("BlockOutbound", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static async Task<string> RunNetshAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = arguments,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) return "Process creation failed";

                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return output + " [Err: " + error.Trim() + "]";
                    }
                    return output.Trim();
                }
                catch (Exception ex)
                {
                    return "Exception: " + ex.Message;
                }
            });
        }

        public static string GetSafeRuleNameForIp(string ip)
        {
            return $"OIS_Allow_{ip.Replace(':', '_').Replace('/', '_')}";
        }

        public static async Task<bool> ApplyLockdownAsync(IEnumerable<SiteItem> sites, Action<string> log)
        {
            log("[stato] attivazione blocco navigazione in corso...");

            // 1. Abilita Windows Firewall su tutti i profili
            await RunNetshAsync("advfirewall set allprofiles state on");

            // 2. Bonifica preliminare di tutte le regole precedenti
            await CleanAllOisRulesAsync();

            // 3. Imposta policy globale: blocca tutto il traffico non esplicitamente autorizzato
            await RunNetshAsync("advfirewall set allprofiles firewallpolicy blockinbound,blockoutbound");
            log("[stato] configurazione applicata: traffico non autorizzato bloccato.");

            var activeRules = new List<string>();

            // 4. Regole core di sistema (DNS UDP/TCP, DHCP, Loopback locale)
            string rDnsUdp = "OIS_Core_DNS_UDP";
            await RunNetshAsync($"advfirewall firewall add rule name=\"{rDnsUdp}\" dir=out action=allow protocol=UDP remoteport=53 enable=yes");
            activeRules.Add(rDnsUdp);

            string rDnsTcp = "OIS_Core_DNS_TCP";
            await RunNetshAsync($"advfirewall firewall add rule name=\"{rDnsTcp}\" dir=out action=allow protocol=TCP remoteport=53 enable=yes");
            activeRules.Add(rDnsTcp);

            string rDhcp = "OIS_Core_DHCP";
            await RunNetshAsync($"advfirewall firewall add rule name=\"{rDhcp}\" dir=out action=allow protocol=UDP remoteport=67,68 enable=yes");
            activeRules.Add(rDhcp);

            string rLoop = "OIS_Core_Loopback";
            await RunNetshAsync($"advfirewall firewall add rule name=\"{rLoop}\" dir=out action=allow remoteip=127.0.0.1,::1 enable=yes");
            activeRules.Add(rLoop);

            // 5. Risoluzione immediata e forzata del portale di gara (gara.squadre.olinfo.it)
            var olinfoIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var addrs = await Dns.GetHostAddressesAsync("gara.squadre.olinfo.it");
                foreach (var a in addrs) olinfoIps.Add(a.ToString());
            }
            catch
            {
            }
            if (olinfoIps.Count == 0) olinfoIps.Add("213.144.152.12");

            foreach (var ip in olinfoIps)
            {
                // Regola con il nome esatto verificato dai controlli OIS
                string rOlinfo = "OIS_ConsentiOlinfo";
                await RunNetshAsync($"advfirewall firewall add rule name=\"{rOlinfo}\" dir=out action=allow remoteip={ip} enable=yes");
                activeRules.Add(rOlinfo);

                string rOlinfoIp = GetSafeRuleNameForIp(ip);
                await RunNetshAsync($"advfirewall firewall add rule name=\"{rOlinfoIp}\" dir=out action=allow remoteip={ip} enable=yes");
                activeRules.Add(rOlinfoIp);
            }
            log($"[gara] regola abilitata per gara.squadre.olinfo.it ({string.Join(", ", olinfoIps)}).");

            // 6. Risolvi e applica tutti gli IP per ciascun sito abilitato (apex, www, CDN e alias)
            var otherIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var site in sites.Where(s => s.IsEnabled))
            {
                string cleanDomain = HealthCheckService.SanitizeDomain(site.Domain);
                if (cleanDomain == "gara.squadre.olinfo.it") continue;

                if (site.ResolvedIps == null || site.ResolvedIps.Count == 0)
                {
                    await HealthCheckService.ResolveSiteIpsAsync(site);
                }

                if (site.ResolvedIps != null)
                {
                    foreach (var ip in site.ResolvedIps)
                    {
                        if (!string.IsNullOrWhiteSpace(ip) && !olinfoIps.Contains(ip.Trim()))
                        {
                            otherIps.Add(ip.Trim());
                        }
                    }
                }
            }

            foreach (var ip in otherIps)
            {
                string ruleName = GetSafeRuleNameForIp(ip);
                await RunNetshAsync($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=allow remoteip={ip} enable=yes");
                activeRules.Add(ruleName);
            }

            var allActive = new HashSet<string>(olinfoIps.Concat(otherIps), StringComparer.OrdinalIgnoreCase);
            _currentlyAppliedIps = allActive;

            // Salva registri per rimozione pulita e sincronizzazione post riavvio
            SaveActiveRulesRegistry(activeRules);
            SaveActiveIpsRegistry(allActive);

            // Sincronizza stato su disco
            SyncStateFiles("Blocked");

            // Svuota cache DNS di Windows per applicare subito i nuovi instradamenti
            await FlushDnsCacheAsync();

            log($"[successo] blocco attivo. autorizzati {sites.Count(s => s.IsEnabled)} siti web ({allActive.Count} indirizzi ip totali).");
            return true;
        }

        public static async Task<bool> RemoveLockdownAsync(Action<string> log)
        {
            log("[stato] rimozione del blocco navigazione in corso...");

            // 1. Elimina tutte le regole OIS create
            await CleanAllOisRulesAsync();

            // 2. Ripristina policy standard di Windows: in uscita consentito
            await RunNetshAsync("advfirewall set allprofiles firewallpolicy blockinbound,allowoutbound");

            _currentlyAppliedIps.Clear();
            SaveActiveRulesRegistry(new List<string>());
            SaveActiveIpsRegistry(new List<string>());

            // 3. Sincronizza stato
            SyncStateFiles("Unlocked");

            await FlushDnsCacheAsync();

            log("[successo] navigazione libera ripristinata su tutti i programmi.");
            return true;
        }

        public static async Task UpdateActiveRulesAsync(IEnumerable<SiteItem> sites, Action<string> log)
        {
            if (!IsLockedDown()) return;

            if (_currentlyAppliedIps.Count == 0)
            {
                _currentlyAppliedIps = LoadActiveIpsRegistry();
            }

            var newIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Risolvi e raccogli tutti gli IP attuali dei siti abilitati
            foreach (var site in sites.Where(s => s.IsEnabled))
            {
                if (site.ResolvedIps == null || site.ResolvedIps.Count == 0)
                {
                    await HealthCheckService.ResolveSiteIpsAsync(site);
                }

                if (site.ResolvedIps != null)
                {
                    foreach (var ip in site.ResolvedIps)
                    {
                        if (!string.IsNullOrWhiteSpace(ip))
                        {
                            newIps.Add(ip.Trim());
                        }
                    }
                }
            }

            // Assicura sempre gara.squadre.olinfo.it
            try
            {
                var olinfoAddrs = await Dns.GetHostAddressesAsync("gara.squadre.olinfo.it");
                foreach (var a in olinfoAddrs) newIps.Add(a.ToString());
            }
            catch
            {
            }
            newIps.Add("213.144.152.12");

            var toRemove = _currentlyAppliedIps.Except(newIps, StringComparer.OrdinalIgnoreCase).ToList();
            var toAdd = newIps.Except(_currentlyAppliedIps, StringComparer.OrdinalIgnoreCase).ToList();

            if (toRemove.Count == 0 && toAdd.Count == 0) return;

            var currentRegistry = LoadActiveRulesRegistry();

            foreach (var ip in toRemove)
            {
                if (ip == "213.144.152.12") continue; // Non rimuovere mai l'IP di gara
                string ruleName = GetSafeRuleNameForIp(ip);
                await RunNetshAsync($"advfirewall firewall delete rule name=\"{ruleName}\"");
                currentRegistry.Remove(ruleName);
            }

            foreach (var ip in toAdd)
            {
                string ruleName = GetSafeRuleNameForIp(ip);
                // Elimina prima per evitare conflitti o duplicati
                await RunNetshAsync($"advfirewall firewall delete rule name=\"{ruleName}\"");
                await RunNetshAsync($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=allow remoteip={ip} enable=yes");
                currentRegistry.Add(ruleName);
            }

            _currentlyAppliedIps = newIps;
            SaveActiveRulesRegistry(currentRegistry);
            SaveActiveIpsRegistry(_currentlyAppliedIps);

            await FlushDnsCacheAsync();

            log($"[aggiornamento] regole sincronizzate: +{toAdd.Count} autorizzati, -{toRemove.Count} rimossi.");
        }

        private static async Task CleanAllOisRulesAsync()
        {
            // 1. Elimina regole registrate
            var registered = LoadActiveRulesRegistry();
            foreach (var rule in registered)
            {
                await RunNetshAsync($"advfirewall firewall delete rule name=\"{rule}\"");
            }

            // 2. Elimina sempre le regole fisse e storiche
            await RunNetshAsync("advfirewall firewall delete rule name=\"OIS_ConsentiOlinfo\"");
            await RunNetshAsync("advfirewall firewall delete rule name=\"OIS_Core_DNS_UDP\"");
            await RunNetshAsync("advfirewall firewall delete rule name=\"OIS_Core_DNS_TCP\"");
            await RunNetshAsync("advfirewall firewall delete rule name=\"OIS_Core_DHCP\"");
            await RunNetshAsync("advfirewall firewall delete rule name=\"OIS_Core_Loopback\"");
            await RunNetshAsync("advfirewall firewall delete rule name=\"OIS_Core_DoH\"");

            // 3. Pulizia a tappeto tramite PowerShell per eventuali regole con prefisso OIS_
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-NetFirewallRule -DisplayName 'OIS*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
            }
            catch
            {
            }
        }

        private static List<string> LoadActiveRulesRegistry()
        {
            try
            {
                if (File.Exists(RulesTrackerFile))
                {
                    return File.ReadAllLines(RulesTrackerFile).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
            catch
            {
            }
            return new List<string>();
        }

        private static void SaveActiveRulesRegistry(List<string> rules)
        {
            try
            {
                if (!Directory.Exists(SecurityService.AppDataFolder))
                {
                    Directory.CreateDirectory(SecurityService.AppDataFolder);
                }
                File.WriteAllLines(RulesTrackerFile, rules.Distinct(StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
            }
        }

        private static HashSet<string> LoadActiveIpsRegistry()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(IpsTrackerFile))
                {
                    var lines = File.ReadAllLines(IpsTrackerFile);
                    foreach (var l in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(l)) result.Add(l.Trim());
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private static void SaveActiveIpsRegistry(IEnumerable<string> ips)
        {
            try
            {
                if (!Directory.Exists(SecurityService.AppDataFolder))
                {
                    Directory.CreateDirectory(SecurityService.AppDataFolder);
                }
                File.WriteAllLines(IpsTrackerFile, ips.Distinct(StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
            }
        }

        private static void SyncStateFiles(string state)
        {
            try
            {
                if (!Directory.Exists(SecurityService.AppDataFolder))
                {
                    Directory.CreateDirectory(SecurityService.AppDataFolder);
                }
                File.WriteAllText(SecurityService.StateFilePath, state);
            }
            catch
            {
            }

            try
            {
                File.WriteAllText(SecurityService.LegacyTempStatePath, state);
            }
            catch
            {
            }
        }
    }
}
