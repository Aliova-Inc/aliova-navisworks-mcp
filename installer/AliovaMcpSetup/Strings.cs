using System;
using System.Globalization;

namespace Aliova.McpSetup
{
    /// <summary>
    /// Textes de l'installateur, francais et anglais.
    /// Installer strings, French and English.
    ///
    /// La langue de depart suit celle de Windows, et l'utilisateur peut la changer par la
    /// bascule FR / EN de l'en-tete. Le choix reste necessaire : dans un bureau bilingue, le
    /// poste peut etre en anglais et l'utilisateur travailler en francais, ou l'inverse.
    /// </summary>
    internal static class Strings
    {
        internal enum Lang { Fr, En }

        // English is the default, always, whatever the Windows language. French is one click
        // away on the FR / EN switch, or forced with --lang=fr. Shipping English first is a
        // product decision, not a limitation: the tool goes to colleagues and to a public
        // repository, and English is the language everyone there reads.
        //
        // L'anglais est le defaut, toujours. Le francais est a un clic, ou impose par
        // --lang=fr.
        private static Lang _current = Lang.En;

        internal static Lang Current
        {
            get { return _current; }
            set { _current = value; }
        }

        internal static bool IsFr { get { return _current == Lang.Fr; } }

        private static string P(string fr, string en) { return _current == Lang.Fr ? fr : en; }

        // --- fenetre ----------------------------------------------------------------------

        internal static string WindowTitle { get { return "Aliova MCP " + P("pour Navisworks", "for Navisworks"); } }
        internal static string ProductName { get { return "Aliova MCP " + P("pour Navisworks", "for Navisworks"); } }

        internal static string Subtitle(string version)
        {
            return P("Version " + version + "   serveur MCP local et onglet de ruban",
                     "Version " + version + "   local MCP server and ribbon tab");
        }

        // --- boutons ----------------------------------------------------------------------

        internal static string Install { get { return P("Installer", "Install"); } }
        internal static string Reinstall { get { return P("Reinstaller", "Reinstall"); } }
        internal static string Update { get { return P("Mettre a jour", "Update"); } }
        internal static string Uninstall { get { return P("Desinstaller", "Uninstall"); } }
        internal static string Close { get { return P("Fermer", "Close"); } }
        internal static string OpenFolder { get { return P("Ouvrir le dossier installe", "Open installed folder"); } }

        // --- etat du poste ----------------------------------------------------------------

        internal static string NavisworksNotFound
        {
            get
            {
                return P("Navisworks 2026 n'a pas ete trouve sur ce poste.",
                         "Navisworks 2026 was not found on this computer.");
            }
        }

        internal static string NavisworksNotFoundHint
        {
            get
            {
                return P("Le paquet peut quand meme etre pose : il sera charge des que Navisworks 2026 sera installe.",
                         "The package can still be installed: it will load as soon as Navisworks 2026 is present.");
            }
        }

        internal static string Detected(string list) { return list + P(" detecte.", " detected."); }

        internal static string NotInstalled { get { return P("Plugin : pas installe.", "Plugin: not installed."); } }

        internal static string AlreadyInstalled(string version)
        {
            return P("Plugin : version " + version + " deja installee.",
                     "Plugin: version " + version + " already installed.");
        }

        internal static string UpdateAvailable(string installed, string available)
        {
            return P("Plugin : version " + installed + " installee, " + available + " disponible.",
                     "Plugin: version " + installed + " installed, " + available + " available.");
        }

        internal static string LegacyFound(int count)
        {
            var plural = count > 1 ? "s" : "";
            return P("Ancienne installation detectee dans le repertoire de Navisworks (" + count +
                     " element" + plural + ") : elle sera retiree.",
                     "Previous installation found in the Navisworks folder (" + count +
                     " item" + plural + "): it will be removed.");
        }

        // --- etapes -----------------------------------------------------------------------

        internal static string NavisworksOpen
        {
            get
            {
                return P("Navisworks est ouvert. Fermez-le puis relancez l'installation.",
                         "Navisworks is running. Close it, then start the installation again.");
            }
        }

        internal static string NavisworksOpenDetail(string action)
        {
            return P("Navisworks doit etre ferme pendant l'installation : la DLL du plugin est verrouillee tant que " +
                     "l'application tourne.\n\nFermez Navisworks, puis cliquez de nouveau sur " + action + ".",
                     "Navisworks must be closed during installation: the plugin DLL stays locked while the " +
                     "application runs.\n\nClose Navisworks, then click " + action + " again.");
        }

        internal static string PreviousRemoved { get { return P("Version precedente retiree", "Previous version removed"); } }

        internal static string PackageInstalled(int files)
        {
            return P("Paquet installe : " + files + " fichiers dans %APPDATA%\\Autodesk\\ApplicationPlugins",
                     "Package installed: " + files + " files in %APPDATA%\\Autodesk\\ApplicationPlugins");
        }

        internal static string Unblocked(int count)
        {
            return count > 0
                ? P("Marque de provenance retiree sur " + count + " fichiers",
                    "Zone marker removed from " + count + " files")
                : P("Aucune marque de provenance a retirer", "No zone marker to remove");
        }

        internal static string IconsOk(int count) { return P("Icones du ruban en place (" + count + ")", "Ribbon icons in place (" + count + ")"); }
        internal static string IconsMissing(int count) { return P("Icones du ruban incompletes (" + count + " sur 6)", "Ribbon icons incomplete (" + count + " of 6)"); }
        internal static string LayoutOk(int count) { return P("Mise en page du ruban en place (" + count + " langues)", "Ribbon layout in place (" + count + " languages)"); }
        internal static string LayoutMissing { get { return P("Mise en page du ruban manquante : l'onglet ne s'affichera pas", "Ribbon layout missing: the tab will not appear"); } }
        internal static string DepsOk(int count) { return P("Dependances embarquees dans le paquet (" + count + ")", "Dependencies bundled in the package (" + count + ")"); }
        internal static string DepsMissing { get { return P("Dependances absentes du paquet", "Dependencies missing from the package"); } }
        internal static string PluginDllMissing { get { return P("DLL du plugin introuvable dans le paquet", "Plugin DLL not found in the package"); } }

        internal static string LegacyElevationAsked
        {
            get
            {
                return P("Ancienne installation dans Program Files : autorisation administrateur demandee.",
                         "Previous installation in Program Files: administrator approval requested.");
            }
        }

        internal static string LegacyRemoved
        {
            get
            {
                return P("Ancienne installation retiree du repertoire de Navisworks.",
                         "Previous installation removed from the Navisworks folder.");
            }
        }

        internal static string LegacyKept
        {
            get
            {
                return P("Ancienne installation conservee : autorisation refusee ou indisponible.",
                         "Previous installation kept: approval declined or unavailable.");
            }
        }

        internal static string LegacyKeptConsequence
        {
            get
            {
                return P("Tant qu'elle est la, Navisworks charge deux fois les memes plugins.",
                         "While it remains, Navisworks loads the same plugins twice.");
            }
        }

        internal static string LegacyPartial(int failed)
        {
            return P(failed + " element(s) de l'ancienne installation n'ont pas pu etre retires.",
                     failed + " item(s) of the previous installation could not be removed.");
        }

        internal static string ServerWillListen(string url)
        {
            return P("Le serveur MCP ecoutera sur " + url + " au demarrage de Navisworks.",
                     "The MCP server will listen on " + url + " when Navisworks starts.");
        }

        internal static string LaunchHint
        {
            get
            {
                return P("Lancez Navisworks : l'onglet Aliova apparait dans le ruban.",
                         "Start Navisworks: the Aliova tab appears in the ribbon.");
            }
        }

        internal static string InstallInterrupted(string message)
        {
            return P("Installation interrompue : " + message, "Installation stopped: " + message);
        }

        // --- desinstallation ---------------------------------------------------------------

        internal static string ConfirmUninstall
        {
            get
            {
                return P("Retirer Aliova MCP pour Navisworks ?\n\n" +
                         "Le paquet sera supprime de %APPDATA%\\Autodesk\\ApplicationPlugins.\n" +
                         "Vos modeles, vos points de vue et vos simulations ne sont pas touches.",
                         "Remove Aliova MCP for Navisworks?\n\n" +
                         "The package will be deleted from %APPDATA%\\Autodesk\\ApplicationPlugins.\n" +
                         "Your models, viewpoints and simulations are untouched.");
            }
        }

        internal static string PackageRemoved
        {
            get
            {
                return P("Paquet retire de %APPDATA%\\Autodesk\\ApplicationPlugins",
                         "Package removed from %APPDATA%\\Autodesk\\ApplicationPlugins");
            }
        }

        internal static string NothingToRemove { get { return P("Aucun paquet a retirer", "No package to remove"); } }
        internal static string EndpointRemoved { get { return P("Fichier d'adresse du serveur supprime", "Server address file deleted"); } }
        internal static string EndpointKept(string message) { return P("Fichier d'adresse non supprime : " + message, "Address file not deleted: " + message); }

        internal static string McpDeclarationRemains
        {
            get
            {
                return P("La declaration du serveur MCP reste dans la configuration de Claude.",
                         "The MCP server declaration remains in your Claude configuration.");
            }
        }

        internal static string McpDeclarationHarmless
        {
            get
            {
                return P("Elle est sans effet une fois le plugin retire.",
                         "It has no effect once the plugin is removed.");
            }
        }

        internal static string UninstallInterrupted(string message)
        {
            return P("Desinstallation interrompue : " + message, "Uninstall stopped: " + message);
        }

        internal static string NavisworksOpenUninstall
        {
            get
            {
                return P("Navisworks est ouvert. Fermez-le puis relancez la desinstallation.",
                         "Navisworks is running. Close it, then start the uninstall again.");
            }
        }

        // --- erreurs ------------------------------------------------------------------------

        internal static string PayloadMissing
        {
            get
            {
                return P("Le paquet embarque est absent de cet installateur. Reconstruisez-le avec " +
                         "installer\\build.ps1, qui fabrique payload\\bundle.zip avant la compilation.",
                         "The embedded package is missing from this installer. Rebuild it with " +
                         "installer\\build.ps1, which produces payload\\bundle.zip before compiling.");
            }
        }
    }
}
