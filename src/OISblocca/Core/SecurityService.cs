using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace OISblocca.Core
{
    public static class SecurityService
    {
        public static string? CustomDataFolder { get; set; }
        public static string AppDataFolder => CustomDataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OISLockdown");
        public static string PasswordFilePath => Path.Combine(AppDataFolder, "auth.dat");
        public static string LegacyPinFilePath => Path.Combine(AppDataFolder, "pin.dat");
        public static string SitesConfigPath => Path.Combine(AppDataFolder, "whitelist.json");
        public static string LegacyWhitelistTxtPath => Path.Combine(AppDataFolder, "whitelist.txt");
        public static string StateFilePath => Path.Combine(AppDataFolder, "state.txt");
        public static string LegacyTempStatePath => Path.Combine(Path.GetTempPath(), "OISblocca.txt");

        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureSecureEnvironment()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                // Applica permessi restrittivi solo alla cartella di produzione di sistema
                if (CustomDataFolder == null)
                {
                    var dirInfo = new DirectoryInfo(AppDataFolder);
                    var dirSecurity = dirInfo.GetAccessControl();

                    dirSecurity.SetAccessRuleProtection(true, false);

                    var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                    var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                    var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                    dirSecurity.PurgeAccessRules(usersSid);

                    dirSecurity.AddAccessRule(new FileSystemAccessRule(
                        adminSid,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                    dirSecurity.AddAccessRule(new FileSystemAccessRule(
                        systemSid,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                    dirSecurity.AddAccessRule(new FileSystemAccessRule(
                        usersSid,
                        FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                    dirInfo.SetAccessControl(dirSecurity);
                }
            }
            catch
            {
            }

            // Inizializza auth se né auth.dat né pin.dat esistono
            if (!File.Exists(PasswordFilePath) && !File.Exists(LegacyPinFilePath))
            {
                SetPassword("ois2026");
            }
        }

        public static bool VerifyPassword(string inputPassword)
        {
            if (string.IsNullOrEmpty(inputPassword)) return false;

            // 1. Verifica hash moderno (auth.dat)
            if (File.Exists(PasswordFilePath))
            {
                try
                {
                    string savedHash = File.ReadAllText(PasswordFilePath).Trim();
                    string inputHash = HashPassword(inputPassword);
                    return string.Equals(savedHash, inputHash, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }

            // 2. Retrocompatibilità: verifica hash precedente (pin.dat con sale v2.0)
            if (File.Exists(LegacyPinFilePath))
            {
                try
                {
                    string legacyHash = File.ReadAllText(LegacyPinFilePath).Trim();
                    string legacyInputHash = HashLegacyPin(inputPassword);
                    if (string.Equals(legacyHash, legacyInputHash, StringComparison.OrdinalIgnoreCase))
                    {
                        // Migra automaticamente all'hash moderno
                        SetPassword(inputPassword);
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }

            // 3. Password predefinite di fabbrica come fallback iniziale (se nessun file di autenticazione esiste ancora)
            if (inputPassword == "ois2026" || inputPassword == "1234")
            {
                SetPassword(inputPassword);
                return true;
            }

            return false;
        }

        public static bool ChangePassword(string currentPassword, string newPassword, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!VerifyPassword(currentPassword))
            {
                errorMessage = "La password attuale non è corretta.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            {
                errorMessage = "La nuova password deve contenere almeno 4 caratteri.";
                return false;
            }

            SetPassword(newPassword);
            return true;
        }

        public static void SetPassword(string newPassword)
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            string hash = HashPassword(newPassword);
            File.WriteAllText(PasswordFilePath, hash, Encoding.UTF8);

            // Sincronizza anche pin.dat per retrocompatibilità verso la versione precedente
            try
            {
                string legacyHash = HashLegacyPin(newPassword);
                File.WriteAllText(LegacyPinFilePath, legacyHash, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string HashPassword(string pwd)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("OIS_SECURITY_SALT_2026_" + pwd));
            var sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string HashLegacyPin(string pin)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("OIS_SALT_2026_" + pin));
            var sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
