using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace Aliova.McpSetup
{
    /// <summary>
    /// Detecte et retire l'ancienne installation dans le repertoire de Navisworks.
    ///
    /// Pourquoi ce cas existe : la premiere version deposait la DLL dans
    /// Navisworks\Plugins\NavisworksMcpPlugin et ses dependances dans Navisworks\Dependencies,
    /// ce qui exigeait les droits administrateur. Le nouveau paquet n'en a plus besoin, mais
    /// si l'ancienne copie reste, Navisworks charge DEUX fois les memes plugins : memes
    /// identifiants declares deux fois, comportement indefini. Il faut donc la retirer, et
    /// c'est la seule etape qui demande l'elevation - une fois, avec une raison affichable.
    /// </summary>
    internal static class LegacyInstall
    {
        private static readonly string[] DependencyFiles =
        {
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "System.Buffers.dll",
            "System.Memory.dll",
            "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Text.Encodings.Web.dll",
            "System.Text.Json.dll",
            "System.Threading.Tasks.Extensions.dll",
            "System.ValueTuple.dll",
        };

        private static readonly string[] PluginFiles =
        {
            "NavisworksMcpPlugin.dll",
            "NavisworksMcpPlugin.pdb",
            "PackageContents.xml",
        };

        /// <summary>Chemins d'une ancienne installation encore presents, pour toutes les cibles detectees.</summary>
        public static List<string> Find(IEnumerable<NavisworksTarget> targets)
        {
            var leftovers = new List<string>();

            foreach (var target in targets)
            {
                var plugins = Path.Combine(target.InstallPath, "Plugins");
                var dependencies = Path.Combine(target.InstallPath, "Dependencies");

                var pluginDir = Path.Combine(plugins, "NavisworksMcpPlugin");
                if (Directory.Exists(pluginDir)) leftovers.Add(pluginDir);

                // Variantes historiques : fichiers poses a plat dans Plugins ou Plugins\net48.
                foreach (var dir in new[] { plugins, Path.Combine(plugins, "net48") })
                {
                    if (!Directory.Exists(dir)) continue;
                    leftovers.AddRange(PluginFiles
                        .Select(f => Path.Combine(dir, f))
                        .Where(File.Exists));
                }

                if (Directory.Exists(dependencies))
                {
                    leftovers.AddRange(DependencyFiles
                        .Select(f => Path.Combine(dependencies, f))
                        .Where(File.Exists));
                }
            }

            // Un ancien paquet utilisateur sous l'ancien nom declarerait les memes plugins.
            var oldBundle = Path.Combine(PluginInstaller.ApplicationPluginsDir, "NavisworksMcpPlugin.bundle");
            if (Directory.Exists(oldBundle)) leftovers.Add(oldBundle);

            return leftovers;
        }

        /// <summary>
        /// Les chemins sous %APPDATA% se suppriment sans elevation ; ceux sous Program Files
        /// ne le peuvent pas. On distingue les deux pour ne demander l'administrateur que si
        /// c'est reellement necessaire.
        /// </summary>
        public static bool NeedsElevation(IEnumerable<string> paths)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return paths.Any(p => !p.StartsWith(appData, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsElevated
        {
            get
            {
                try
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                    {
                        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Verification des droits echouee : " + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>Supprime les chemins donnes. Renvoie ceux qui ont resiste.</summary>
        public static List<string> Remove(IEnumerable<string> paths)
        {
            var failed = new List<string>();

            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        {
                            try { File.SetAttributes(file, FileAttributes.Normal); }
                            catch (Exception ex) { Trace.TraceWarning("Attributs : " + ex.Message); }
                        }
                        Directory.Delete(path, true);
                    }
                    else if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Suppression impossible : " + path + " : " + ex.Message);
                    failed.Add(path);
                }
            }

            return failed;
        }

        /// <summary>
        /// Relance l'installateur en administrateur pour la seule etape de nettoyage. On passe
        /// par un argument dedie plutot que d'elever toute l'installation : le paquet doit
        /// etre pose par l'utilisateur courant, sinon il atterrit dans le %APPDATA% de
        /// l'administrateur et Navisworks ne le voit jamais.
        /// </summary>
        public static bool RunElevatedCleanup()
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Arguments = Program.CleanupArgument,
                    UseShellExecute = true,
                    Verb = "runas",
                };

                var proc = Process.Start(info);
                if (proc == null) return false;
                proc.WaitForExit(120000);
                return proc.HasExited && proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                // L'utilisateur a refuse l'elevation : ce n'est pas une erreur, c'est un choix.
                Trace.TraceWarning("Nettoyage eleve non effectue : " + ex.Message);
                return false;
            }
        }
    }
}
