using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Aliova.McpSetup
{
    /// <summary>Un pas d'installation raconte a l'utilisateur.</summary>
    internal sealed class StepReport
    {
        public string Text { get; set; }
        public bool Ok { get; set; }
        public bool Warning { get; set; }
    }

    /// <summary>
    /// Pose et retire le paquet du plugin.
    ///
    /// L'installation est une extraction de dossier sous %APPDATA%, rien d'autre : pas de
    /// registre, pas de service, pas de fichier dans le repertoire de Navisworks, pas
    /// d'elevation. Desinstaller revient a supprimer ce dossier. C'est le format de paquet
    /// d'application d'Autodesk, celui qu'utilisent les plugins commerciaux.
    /// </summary>
    internal static class PluginInstaller
    {
        public const string BundleFolderName = "Aliova.NavisworksMcp.bundle";
        private const string PayloadResource = "Aliova.McpSetup.bundle.zip";

        /// <summary>Dossier des paquets d'application de l'utilisateur courant.</summary>
        public static string ApplicationPluginsDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "ApplicationPlugins");
            }
        }

        public static string BundleDir
        {
            get { return Path.Combine(ApplicationPluginsDir, BundleFolderName); }
        }

        public static bool IsInstalled
        {
            get { return File.Exists(Path.Combine(BundleDir, "PackageContents.xml")); }
        }

        /// <summary>Version deja posee, lue dans le manifeste, ou null.</summary>
        public static string InstalledVersion
        {
            get
            {
                try
                {
                    var manifest = Path.Combine(BundleDir, "PackageContents.xml");
                    if (!File.Exists(manifest)) return null;
                    var doc = System.Xml.Linq.XDocument.Load(manifest);
                    var attr = doc.Root == null ? null : doc.Root.Attribute("AppVersion");
                    return attr == null ? null : attr.Value;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Lecture du manifeste installe echouee : " + ex.Message);
                    return null;
                }
            }
        }

        public static string PayloadVersion
        {
            get
            {
                var v = typeof(PluginInstaller).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        /// <summary>
        /// Navisworks doit etre ferme : une fois le plugin charge, sa DLL est verrouillee et
        /// l'ecriture echouerait a mi-chemin. On refuse avant de toucher au disque plutot que
        /// de laisser une installation partielle.
        /// </summary>
        public static List<Process> RunningNavisworks()
        {
            var names = new[] { "Roamer", "Navisworks" };
            var running = new List<Process>();
            foreach (var name in names)
            {
                try { running.AddRange(Process.GetProcessesByName(name)); }
                catch (Exception ex) { Trace.TraceWarning("Enumeration des processus echouee : " + ex.Message); }
            }
            return running;
        }

        public static List<StepReport> Install()
        {
            var steps = new List<StepReport>();

            // 1. remplacement propre : on retire l'ancien dossier avant d'extraire, sinon des
            //    fichiers d'une version precedente survivent et se melangent a la nouvelle.
            if (Directory.Exists(BundleDir))
            {
                DeleteDirectory(BundleDir);
                steps.Add(new StepReport { Text = Strings.PreviousRemoved, Ok = true });
            }

            Directory.CreateDirectory(ApplicationPluginsDir);

            // 2. extraction du paquet embarque
            using (var stream = OpenPayload())
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(BundleDir);
            }

            var files = Directory.GetFiles(BundleDir, "*", SearchOption.AllDirectories);
            steps.Add(new StepReport
            {
                Text = Strings.PackageInstalled(files.Length),
                Ok = true
            });

            // 3. Windows marque comme bloques les fichiers venus d'ailleurs, et le CLR refuse
            //    alors de charger l'assembly. C'est la cause classique du plugin qui
            //    n'apparait pas apres un telechargement.
            var unblocked = UnblockAll(BundleDir);
            steps.Add(new StepReport
            {
                Text = Strings.Unblocked(unblocked),
                Ok = true
            });

            // 4. verification du contrat de ressources : mieux vaut le dire ici que de laisser
            //    l'utilisateur decouvrir un onglet sans icone.
            steps.AddRange(VerifyLayout());

            return steps;
        }

        public static List<StepReport> Uninstall()
        {
            var steps = new List<StepReport>();

            if (Directory.Exists(BundleDir))
            {
                DeleteDirectory(BundleDir);
                steps.Add(new StepReport { Text = Strings.PackageRemoved, Ok = true });
            }
            else
            {
                steps.Add(new StepReport { Text = Strings.NothingToRemove, Ok = true });
            }

            // Le fichier d'adresse publie par le serveur n'a plus d'objet.
            try
            {
                var endpointDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aliova", "NavisworksMcp");
                if (Directory.Exists(endpointDir))
                {
                    DeleteDirectory(endpointDir);
                    steps.Add(new StepReport { Text = Strings.EndpointRemoved, Ok = true });
                }
            }
            catch (Exception ex)
            {
                steps.Add(new StepReport { Text = Strings.EndpointKept(ex.Message), Warning = true });
            }

            return steps;
        }

        /// <summary>
        /// Verifie que chaque ressource nommee par le plugin existe la ou Navisworks la
        /// cherchera : les icones a plat a cote de la DLL, le XAML du ruban dans un
        /// sous-dossier de langue. Un chemin faux donne un onglet muet, jamais une erreur.
        /// </summary>
        private static List<StepReport> VerifyLayout()
        {
            var steps = new List<StepReport>();
            var contents = Path.Combine(BundleDir, "Contents", "2026");

            if (!File.Exists(Path.Combine(contents, "NavisworksMcpPlugin.dll")))
            {
                steps.Add(new StepReport { Text = Strings.PluginDllMissing, Warning = true });
                return steps;
            }

            var icons = Directory.GetFiles(contents, "*.ico");
            var localeXaml = Directory.GetDirectories(contents)
                .SelectMany(d => Directory.GetFiles(d, "*.xaml"))
                .ToList();

            steps.Add(icons.Length >= 6
                ? new StepReport { Text = Strings.IconsOk(icons.Length), Ok = true }
                : new StepReport { Text = Strings.IconsMissing(icons.Length), Warning = true });

            steps.Add(localeXaml.Count >= 1
                ? new StepReport { Text = Strings.LayoutOk(localeXaml.Count), Ok = true }
                : new StepReport { Text = Strings.LayoutMissing, Warning = true });

            var deps = Directory.GetFiles(contents, "System.*.dll").Length;
            steps.Add(deps > 0
                ? new StepReport { Text = Strings.DepsOk(deps), Ok = true }
                : new StepReport { Text = Strings.DepsMissing, Warning = true });

            return steps;
        }

        private static Stream OpenPayload()
        {
            var asm = typeof(PluginInstaller).Assembly;
            var stream = asm.GetManifestResourceStream(PayloadResource);
            if (stream == null)
            {
                throw new InvalidOperationException(
                    Strings.PayloadMissing);
            }
            return stream;
        }

        /// <summary>
        /// Retire le flux alternatif Zone.Identifier, celui que Windows ajoute aux fichiers
        /// telecharges et qui fait echouer le chargement de l'assembly sans message clair.
        /// </summary>
        private static int UnblockAll(string root)
        {
            var count = 0;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (NativeMethods.DeleteFile(file + ":Zone.Identifier")) count++;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Deblocage echoue sur " + file + " : " + ex.Message);
                }
            }
            return count;
        }

        private static void DeleteDirectory(string path)
        {
            // Les fichiers en lecture seule font echouer Directory.Delete ; on leve l'attribut
            // avant, plutot que de renvoyer une erreur que l'utilisateur ne peut pas corriger.
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch (Exception ex) { Trace.TraceWarning("Attributs non modifies sur " + file + " : " + ex.Message); }
            }
            Directory.Delete(path, true);
        }
    }
}
