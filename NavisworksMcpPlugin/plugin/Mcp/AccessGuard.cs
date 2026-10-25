using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Controle d'acces du serveur MCP : un jeton porteur obligatoire, et aucun CORS.
    ///
    /// Pourquoi : le serveur renvoyait "Access-Control-Allow-Origin: *" et n'exigeait rien.
    /// Ecouter sur 127.0.0.1 protege du reseau, pas du navigateur : n'importe quelle page web
    /// ouverte sur le poste pouvait envoyer un POST a /mcp et appeler l'execution de code C#,
    /// donc executer ce qu'elle voulait dans Navisworks avec les droits de l'utilisateur.
    ///
    /// Le jeton ferme cette porte : il vit dans un fichier sous LOCALAPPDATA, lisible par le
    /// seul compte Windows de l'utilisateur, et une page web n'a aucun moyen de le lire. Sans
    /// en-tete CORS, le navigateur refuse en plus d'envoyer l'en-tete Authorization en
    /// cross-origin (le preflight echoue). La verification de l'en-tete Host bloque le
    /// rebinding DNS, ou une page se fait passer pour 127.0.0.1.
    ///
    /// Le jeton est PERSISTANT : il survit au redemarrage de Navisworks pour que la
    /// configuration du client MCP reste valable. Supprimer le fichier en fait generer un
    /// nouveau au prochain demarrage du serveur (rotation).
    /// </summary>
    public static class AccessGuard
    {
        /// <summary>Variable d'environnement de surcharge du jeton, pour un deploiement scripte.</summary>
        public const string TokenEnvironmentVariable = "ALIOVA_NAVISWORKS_MCP_TOKEN";

        private static readonly object Gate = new object();
        private static string _token;

        /// <summary>Fichier du jeton, a cote du fichier d'adresse.</summary>
        public static string TokenFilePath
        {
            get { return Path.Combine(Diagnostics.LogDirectory, "token.txt"); }
        }

        /// <summary>Jeton en vigueur. Le cree au premier appel s'il n'existe pas encore.</summary>
        public static string Token
        {
            get
            {
                lock (Gate)
                {
                    if (_token == null) _token = LoadOrCreateToken();
                    return _token;
                }
            }
        }

        /// <summary>Valeur complete de l'en-tete Authorization attendu.</summary>
        public static string AuthorizationHeaderValue
        {
            get { return "Bearer " + Token; }
        }

        /// <summary>
        /// Commande prete a coller pour declarer le serveur dans Claude Code.
        /// </summary>
        public static string ClaudeCodeAddCommand(string mcpUrl)
        {
            return "claude mcp add --transport http navisworks " + mcpUrl +
                   " --header \"Authorization: " + AuthorizationHeaderValue + "\"";
        }

        /// <summary>
        /// Verifie une requete. Renvoie null si elle passe, sinon le code HTTP et le motif.
        /// </summary>
        internal static Rejection Check(HttpListenerRequest req, int activePort, bool requireToken)
        {
            if (!IsLoopbackHost(req.Headers["Host"], activePort))
                return new Rejection(403, "Host refuse : seul 127.0.0.1 ou localhost est accepte.");

            if (!requireToken) return null;

            var presented = ExtractBearer(req.Headers["Authorization"]);
            if (presented == null)
                return new Rejection(401,
                    "Jeton manquant. Ajouter l'en-tete 'Authorization: Bearer <jeton>'. " +
                    "Le jeton est dans " + TokenFilePath + " (bouton A propos du ruban Aliova).");

            if (!FixedTimeEquals(presented, Token))
                return new Rejection(401,
                    "Jeton invalide. Le jeton en vigueur est dans " + TokenFilePath + ".");

            return null;
        }

        internal sealed class Rejection
        {
            public readonly int StatusCode;
            public readonly string Message;

            public Rejection(int statusCode, string message)
            {
                StatusCode = statusCode;
                Message = message;
            }
        }

        private static bool IsLoopbackHost(string host, int port)
        {
            if (string.IsNullOrEmpty(host)) return false;
            var p = port.ToString(CultureInfo.InvariantCulture);
            return string.Equals(host, "127.0.0.1:" + p, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "localhost:" + p, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractBearer(string header)
        {
            if (string.IsNullOrEmpty(header)) return null;
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var value = header.Substring(prefix.Length).Trim();
            return value.Length == 0 ? null : value;
        }

        /// <summary>Comparaison en temps constant : la duree ne revele pas le prefixe correct.</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            var x = Encoding.UTF8.GetBytes(a);
            var y = Encoding.UTF8.GetBytes(b);
            var diff = x.Length ^ y.Length;
            for (var i = 0; i < x.Length && i < y.Length; i++) diff |= x[i] ^ y[i];
            return diff == 0;
        }

        private static string LoadOrCreateToken()
        {
            var fromEnv = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            var path = TokenFilePath;
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (existing.Length >= 32) return existing;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Lecture du jeton impossible, un nouveau est genere : " + ex.Message);
            }

            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, token + Environment.NewLine, new UTF8Encoding(false));
                Diagnostics.Info("Nouveau jeton MCP genere dans " + path);
            }
            catch (Exception ex)
            {
                // Sans fichier, le jeton vit le temps de la session : le serveur reste sur,
                // seul le client devra etre reconfigure au prochain demarrage.
                Diagnostics.Warn("Ecriture du jeton impossible, jeton valable pour cette session seulement : " + ex.Message);
            }

            return token;
        }
    }
}
