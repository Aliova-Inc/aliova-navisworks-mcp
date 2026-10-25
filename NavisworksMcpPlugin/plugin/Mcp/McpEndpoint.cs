using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Choisit et publie l'adresse d'ecoute du serveur MCP.
    ///
    /// Pourquoi ce fichier existe : le port etait code en dur a 8765, et un autre
    /// programme de l'utilisateur ecoutait deja dessus. HttpListener echouait alors avec
    /// "Le processus ne peut pas acceder au fichier car ce fichier est utilise par un autre
    /// processus" - un message Win32 qui ne dit ni quel port ni quel programme. Desormais
    /// on sonde avant d'ouvrir, on se rabat sur le port suivant si besoin, et on ecrit
    /// l'adresse reellement retenue dans un fichier que le client peut lire.
    ///
    /// Le port par defaut reste FIXE et documente : le client MCP le porte dans sa
    /// configuration, il ne le decouvre pas. Le repli n'est qu'un filet de securite, et il
    /// se signale (A propos, journal, fichier d'adresse) pour que l'ecart soit visible au
    /// lieu de se traduire par un client qui ne se connecte plus sans explication.
    /// </summary>
    public static class McpEndpoint
    {
        /// <summary>Port par defaut du serveur. Doit correspondre a la configuration du client MCP.</summary>
        public const int DefaultPort = 8766;

        /// <summary>Variable d'environnement de surcharge, pour poste ou le port par defaut est pris.</summary>
        public const string PortEnvironmentVariable = "ALIOVA_NAVISWORKS_MCP_PORT";

        /// <summary>Nombre de ports consecutifs essayes avant d'abandonner.</summary>
        private const int FallbackAttempts = 10;

        private static int _activePort;

        /// <summary>Port effectivement ouvert, ou 0 si le serveur ne tourne pas.</summary>
        public static int ActivePort
        {
            get { return _activePort; }
        }

        /// <summary>Port souhaite : surcharge par variable d'environnement, sinon <see cref="DefaultPort"/>.</summary>
        public static int PreferredPort
        {
            get
            {
                var raw = Environment.GetEnvironmentVariable(PortEnvironmentVariable);
                int parsed;
                if (!string.IsNullOrWhiteSpace(raw) &&
                    int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
                    parsed > 0 && parsed <= 65535)
                {
                    return parsed;
                }

                return DefaultPort;
            }
        }

        /// <summary>Adresse HTTP de base pour un port donne.</summary>
        public static string BaseUrl(int port)
        {
            return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
        }

        /// <summary>Adresse a inscrire dans la configuration du client MCP.</summary>
        public static string McpUrl(int port)
        {
            return BaseUrl(port) + "mcp";
        }

        /// <summary>Adresse en service, ou celle du port souhaite si le serveur est arrete.</summary>
        public static string CurrentMcpUrl
        {
            get { return McpUrl(_activePort > 0 ? _activePort : PreferredPort); }
        }

        /// <summary>true quand le serveur a du se rabattre sur un autre port que le souhaite.</summary>
        public static bool IsOnFallbackPort
        {
            get { return _activePort > 0 && _activePort != PreferredPort; }
        }

        /// <summary>
        /// Trouve un port libre en partant du port souhaite. La sonde ouvre puis referme un
        /// TcpListener : c'est la seule facon fiable de savoir si le port est prenable, un
        /// simple parcours des ports en ecoute laisserait passer les reservations exclusives.
        /// </summary>
        public static int ResolveFreePort()
        {
            var preferred = PreferredPort;

            for (var offset = 0; offset < FallbackAttempts; offset++)
            {
                var candidate = preferred + offset;
                if (candidate > 65535) break;
                if (!IsPortFree(candidate)) continue;

                if (offset > 0)
                {
                    Trace.TraceWarning(
                        "[NavisworksMCP] Port " + preferred + " occupe par un autre programme, repli sur " +
                        candidate + ". Le client MCP configure sur " + McpUrl(preferred) +
                        " ne se connectera pas tant que sa configuration n'aura pas ete mise a jour.");
                }

                return candidate;
            }

            throw new IOException(
                "Aucun port libre entre " + preferred + " et " + (preferred + FallbackAttempts - 1) + ". " +
                "Fermez le programme qui occupe cette plage, ou fixez un autre port avec la variable " +
                "d'environnement " + PortEnvironmentVariable + ".");
        }

        private static bool IsPortFree(int port)
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    try { probe.Stop(); } catch (SocketException) { }
                }
            }
        }

        /// <summary>Enregistre le port retenu et publie l'adresse sur disque.</summary>
        internal static void MarkActive(int port)
        {
            _activePort = port;
            WriteEndpointFile(port);
        }

        /// <summary>Oublie le port et retire l'adresse publiee.</summary>
        internal static void MarkStopped()
        {
            _activePort = 0;
            try
            {
                var path = EndpointFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("[NavisworksMCP] Endpoint file cleanup failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Fichier d'adresse, sous LOCALAPPDATA pour rester accessible sans droits
        /// particuliers. Il permet de savoir de l'exterieur si le serveur tourne et sur
        /// quel port, sans interroger Navisworks.
        /// </summary>
        public static string EndpointFilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aliova", "NavisworksMcp", "endpoint.json");
            }
        }

        private static void WriteEndpointFile(int port)
        {
            try
            {
                var path = EndpointFilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // JSON ecrit a la main : ce fichier est produit tres tot au demarrage et on
                // ne veut pas dependre du chargement de System.Text.Json pour le publier.
                var json = new StringBuilder();
                json.Append("{\n");
                json.Append("  \"url\": \"").Append(McpUrl(port)).Append("\",\n");
                json.Append("  \"port\": ").Append(port.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"preferredPort\": ").Append(PreferredPort.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"processId\": ").Append(Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"startedUtc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\"\n");
                json.Append("}\n");

                File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // Publier l'adresse est un confort de diagnostic, jamais une condition de
                // demarrage : un echec ici ne doit pas empecher le serveur de servir.
                Trace.TraceWarning("[NavisworksMCP] Endpoint file write failed: " + ex.Message);
            }
        }
    }
}
