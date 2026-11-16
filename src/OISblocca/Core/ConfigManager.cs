using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OISblocca.Core
{
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = true;
        public List<SiteItem> Sites { get; set; } = new();
    }

    public static class ConfigManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static AppSettings LoadSettings()
        {
            SecurityService.EnsureSecureEnvironment();



            if (!File.Exists(SecurityService.SitesConfigPath))
            {
                var migrated = TryMigrateLegacyWhitelistTxt();
                if (migrated != null && migrated.Sites.Count > 0)
                {
                    SaveSettings(migrated);
                    return migrated;
                }

                var defaults = GetDefaultSettings();
                SaveSettings(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(SecurityService.SitesConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return settings ?? GetDefaultSettings();
            }
            catch
            {
                return GetDefaultSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(SecurityService.AppDataFolder))
                {
                    Directory.CreateDirectory(SecurityService.AppDataFolder);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SecurityService.SitesConfigPath, json);

                // Sincronizza anche whitelist.txt per retrocompatibilità verso la versione console/WinForms
                SyncLegacyWhitelistTxt(settings.Sites);
            }
            catch
            {
            }
        }

        private static AppSettings? TryMigrateLegacyWhitelistTxt()
        {
            if (!File.Exists(SecurityService.LegacyWhitelistTxtPath))
            {
                return null;
            }

            try
            {
                var sites = new List<SiteItem>();
                var lines = File.ReadAllLines(SecurityService.LegacyWhitelistTxtPath);

                foreach (var line in lines)
                {
                    string host = HealthCheckService.SanitizeDomain(line);
                    if (string.IsNullOrEmpty(host)) continue;

                    if (!sites.Any(s => s.Domain.Equals(host, StringComparison.OrdinalIgnoreCase)))
                    {
                        string desc = host.Contains("olinfo") ? "Piattaforma Gara OIS" : "Sito autorizzato";
                        sites.Add(new SiteItem
                        {
                            Domain = host,
                            Description = desc,
                            IsEnabled = true
                        });
                    }
                }

                if (sites.Count > 0)
                {
                    return new AppSettings
                    {
                        StartWithWindows = true,
                        Sites = sites
                    };
                }
            }
            catch
            {
            }

            return null;
        }

        private static void SyncLegacyWhitelistTxt(List<SiteItem> sites)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# ====================================================================");
                sb.AppendLine("# OISblocca - Whitelist Sincronizzata");
                sb.AppendLine("# ====================================================================");
                foreach (var s in sites.Where(x => x.IsEnabled))
                {
                    sb.AppendLine(s.Domain);
                }
                File.WriteAllText(SecurityService.LegacyWhitelistTxtPath, sb.ToString());
            }
            catch
            {
            }
        }

        private static AppSettings GetDefaultSettings()
        {
            return new AppSettings
            {
                StartWithWindows = true,
                Sites = new List<SiteItem>
                {
                    new SiteItem
                    {
                        Domain = "gara.squadre.olinfo.it",
                        Description = "Piattaforma Ufficiale di Gara OIS",
                        IsEnabled = true
                    },
                    new SiteItem
                    {
                        Domain = "squadre.olinfo.it",
                        Description = "Portale Notizie Olimpiadi a Squadre",
                        IsEnabled = true
                    },
                    new SiteItem
                    {
                        Domain = "cppreference.com",
                        Description = "Documentazione C / C++ di Riferimento",
                        IsEnabled = true
                    },
                    new SiteItem
                    {
                        Domain = "docs.python.org",
                        Description = "Documentazione Ufficiale Python",
                        IsEnabled = true
                    }
                }
            };
        }
    }
}
