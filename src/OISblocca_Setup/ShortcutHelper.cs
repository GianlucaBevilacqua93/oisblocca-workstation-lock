using System;
using System.IO;

namespace OISblocca_Setup
{
    public static class ShortcutHelper
    {
        public static string CommonProgramsFolder =>
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

        public static string CommonDesktopFolder =>
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        public static void CreateShortcut(string shortcutPath, string targetPath, string description, string? workingDir = null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDir ?? Path.GetDirectoryName(targetPath);
                shortcut.Description = description;
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
            }
            catch
            {
            }
        }

        public static void DeleteShortcut(string shortcutPath)
        {
            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch
            {
            }
        }
    }
}
