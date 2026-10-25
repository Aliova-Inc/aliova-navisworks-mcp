using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

// ModuleInitializerAttribute n'existe pas dans .NET Framework 4.8, mais le compilateur C# 9
// honore n'importe quel type portant ce nom complet. On le declare donc ici : c'est la seule
// facon d'executer du code AVANT tout le reste de l'assembly, y compris avant le JIT des
// methodes qui referencent System.Text.Json.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace NavisworksMcpPlugin.Plugin
{
    /// <summary>
    /// Charge les assemblies compagnes depuis le dossier du plugin.
    ///
    /// Pourquoi c'est necessaire : Navisworks charge le plugin par Assembly.LoadFrom, mais le
    /// CLR resout ensuite ses dependances depuis le dossier de l'application hote et son
    /// chemin de sondage prive (Navisworks\Dependencies), PAS depuis le dossier du plugin.
    /// C'est pour cela que l'ancienne installation copiait System.Text.Json et ses satellites
    /// dans Program Files - et donc exigeait les droits administrateur.
    ///
    /// En resolvant nous-memes, tout tient dans le dossier du plugin. L'installation devient
    /// une simple copie sous %APPDATA%, sans elevation, et desinstaller redevient supprimer
    /// un dossier. Aucun fichier n'est depose dans le repertoire de Navisworks.
    /// </summary>
    internal static class DependencyResolver
    {
        private static readonly object Gate = new object();
        private static bool _installed;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Install()
        {
            lock (Gate)
            {
                if (_installed) return;
                _installed = true;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var simpleName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(simpleName)) return null;

                // Les assemblies satellites de ressources se nomment "X.resources" et n'existent
                // pas a cote de la DLL. Les ignorer evite un acces disque a chaque langue
                // sollicitee par le CLR.
                if (simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;

                var dir = PluginDirectory;
                if (dir == null) return null;

                var candidate = Path.Combine(dir, simpleName + ".dll");
                if (!File.Exists(candidate)) return null;

                var loaded = Assembly.LoadFrom(candidate);
                Trace.TraceInformation("[NavisworksMCP] Resolved " + simpleName + " from the plugin folder.");
                return loaded;
            }
            catch (Exception ex)
            {
                // Renvoyer null laisse le CLR poursuivre sa propre resolution et produire une
                // erreur de chargement explicite. Lever ici masquerait la cause reelle.
                Trace.TraceWarning("[NavisworksMCP] Dependency resolution failed for " + args.Name + ": " + ex.Message);
                return null;
            }
        }

        private static string PluginDirectory
        {
            get
            {
                try
                {
                    // Location plutot que CodeBase : Location donne le fichier reellement
                    // charge, seul chemin correct quand l'assembly a ete copiee ailleurs.
                    var location = typeof(DependencyResolver).Assembly.Location;
                    return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("[NavisworksMCP] Plugin directory lookup failed: " + ex.Message);
                    return null;
                }
            }
        }
    }
}
