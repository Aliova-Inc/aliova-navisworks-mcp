using System;
using System.Globalization;

namespace NavisworksMcpPlugin.Plugin
{
    /// <summary>
    /// Textes de l'interface du plugin, en francais et en anglais.
    /// Plugin interface strings, French and English.
    ///
    /// Le XAML du ruban est deja livre par langue (fr-FR, en-US), mais tout ce qui est ecrit
    /// a l'execution - libelle d'etat du bouton, infobulles, boites de dialogue - etait
    /// francais en dur. Sur un poste anglais, l'onglet melangeait donc les deux langues.
    ///
    /// La langue suit CultureInfo.CurrentUICulture, c'est-a-dire la langue d'interface de
    /// Navisworks, la meme qui decide quel XAML est charge. Les deux restent ainsi coherents.
    /// Tout ce qui n'est pas francais retombe sur l'anglais.
    /// </summary>
    internal static class Texts
    {
        /// <summary>true quand l'interface de Navisworks est en francais.</summary>
        internal static bool Fr
        {
            get
            {
                try
                {
                    return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                        .Equals("fr", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static string Pick(string fr, string en) { return Fr ? fr : en; }

        // --- libelle du bouton, deux lignes comme le reste du ruban ----------------------

        internal static string ServerOpen(int port)
        {
            return Pick("MCP ouvert\nport " + port, "MCP open\nport " + port);
        }

        internal static string ServerClosed()
        {
            return Pick("MCP ferme\ncliquer ici", "MCP closed\nclick here");
        }

        internal static string ServerFailed()
        {
            return Pick("MCP en echec\nvoir A propos", "MCP failed\nsee About");
        }

        // --- infobulles ------------------------------------------------------------------

        internal static string TooltipOpen(string url, bool fallback, int preferredPort)
        {
            var text = Pick("Canal MCP OUVERT", "MCP channel OPEN") + Environment.NewLine +
                       url + Environment.NewLine + Environment.NewLine +
                       Pick("Cliquer pour fermer.", "Click to close.");

            if (fallback)
            {
                text += Environment.NewLine + Environment.NewLine +
                    Pick("Le port souhaite " + preferredPort + " etait occupe.",
                         "Preferred port " + preferredPort + " was busy.") + Environment.NewLine +
                    Pick("Reportez l'adresse ci-dessus dans la configuration de Claude.",
                         "Copy the address above into your Claude configuration.");
            }

            return text;
        }

        internal static string TooltipFailed(string cause)
        {
            return Pick("Canal MCP EN ECHEC", "MCP channel FAILED") + Environment.NewLine +
                   cause + Environment.NewLine + Environment.NewLine +
                   Pick("Cliquer pour reessayer.", "Click to retry.");
        }

        internal static string TooltipClosed(int preferredPort)
        {
            return Pick("Canal MCP ferme", "MCP channel closed") + Environment.NewLine +
                   Pick("Port souhaite : ", "Preferred port: ") + preferredPort + Environment.NewLine +
                   Environment.NewLine +
                   Pick("Cliquer pour ouvrir.", "Click to open.");
        }

        // --- boites de dialogue -----------------------------------------------------------

        internal const string DialogTitle = "Aliova MCP";

        internal static string StartFailure(string cause, int preferredPort, string logPath)
        {
            return Pick("Le canal MCP n'a pas pu s'ouvrir.", "The MCP channel could not open.") +
                   Environment.NewLine + Environment.NewLine +
                   Pick("Cause : ", "Cause: ") + cause + Environment.NewLine + Environment.NewLine +
                   Pick("Port souhaite : ", "Preferred port: ") + preferredPort + Environment.NewLine +
                   Pick("Journal : ", "Log: ") + logPath + Environment.NewLine + Environment.NewLine +
                   Pick("Si un autre programme occupe la plage de ports, fermez-le et recliquez sur le bouton Serveur MCP.",
                        "If another program holds the port range, close it and click the MCP server button again.");
        }

        internal static string FallbackPortWarning(int preferredPort, string url)
        {
            return Pick("Canal MCP ouvert, mais pas sur le port souhaite.",
                        "MCP channel open, but not on the preferred port.") +
                   Environment.NewLine + Environment.NewLine +
                   Pick("Port " + preferredPort + " occupe par un autre programme.",
                        "Port " + preferredPort + " is held by another program.") + Environment.NewLine +
                   Pick("Adresse en service : ", "Address in service: ") + url +
                   Environment.NewLine + Environment.NewLine +
                   Pick("Reportez cette adresse dans la configuration de Claude.",
                        "Copy this address into your Claude configuration.");
        }

        internal static string ChatPaneMissing(string bundlePath)
        {
            return Pick("Le panneau de discussion Claude est introuvable.",
                        "The Claude chat pane could not be found.") +
                   Environment.NewLine + Environment.NewLine +
                   Pick("Verifiez que le paquet du plugin est complet dans",
                        "Check that the plugin package is complete in") + Environment.NewLine +
                   bundlePath;
        }

        internal static string CommandError(string message)
        {
            return Pick("Erreur : ", "Error: ") + message;
        }

        // --- a propos ---------------------------------------------------------------------

        internal static string AboutProduct() { return "Aliova MCP " + Pick("pour Navisworks", "for Navisworks"); }
        internal static string AboutVersion() { return Pick("Version ", "Version "); }
        internal static string AboutChannel() { return Pick("Canal MCP  : ", "MCP channel : "); }
        internal static string AboutOpen() { return Pick("OUVERT", "OPEN"); }
        internal static string AboutClosed() { return Pick("ferme", "closed"); }
        internal static string AboutAddress() { return Pick("Adresse    : ", "Address     : "); }
        internal static string AboutPort() { return Pick("Port       : ", "Port        : "); }
        internal static string AboutPreferred() { return Pick(" (souhaite)", " (preferred)"); }
        internal static string AboutTools() { return Pick("Outils MCP : ", "MCP tools   : "); }
        internal static string AboutPackage() { return Pick("Paquet   : ", "Package : "); }
        internal static string AboutLog() { return Pick("Journal  : ", "Log     : "); }

        internal static string AboutLastError()
        {
            return Pick("Derniere erreur d'ouverture :", "Last open error:");
        }

        internal static string AboutFallback(int preferredPort)
        {
            return Pick("Le port " + preferredPort + " etait occupe par un autre programme.",
                        "Port " + preferredPort + " was held by another program.") + Environment.NewLine +
                   Pick("Reportez l'adresse ci-dessus dans la configuration de Claude.",
                        "Copy the address above into your Claude configuration.");
        }

        internal static string AboutToken() { return Pick("Jeton    : ", "Token   : "); }
        internal static string AboutModifyPolicy() { return Pick("Ecriture : ", "Writes  : "); }
        internal static string AboutRuns() { return Pick("Appels   : ", "Runs    : "); }

        internal static string AboutChoices()
        {
            return Pick("Oui : copier la commande de configuration de Claude Code (jeton compris)" + Environment.NewLine +
                        "Non : ouvrir le dossier du journal",
                        "Yes: copy the Claude Code setup command (token included)" + Environment.NewLine +
                        "No: open the log folder");
        }

        internal static string ClientCommandCopied()
        {
            return Pick("Commande copiee. Collez-la dans un terminal pour declarer le serveur dans Claude Code." +
                        Environment.NewLine + Environment.NewLine +
                        "Pour un autre client, ajoutez l'en-tete Authorization: Bearer <jeton> a sa configuration.",
                        "Command copied. Paste it in a terminal to register the server in Claude Code." +
                        Environment.NewLine + Environment.NewLine +
                        "For another client, add the header Authorization: Bearer <token> to its configuration.");
        }
    }
}
