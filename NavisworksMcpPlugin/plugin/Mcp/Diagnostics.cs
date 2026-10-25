using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Journal du plugin, dans un fichier que l'utilisateur peut ouvrir.
    ///
    /// Pourquoi : Trace.TraceError part dans le vide sur un poste ordinaire. Le plugin a
    /// echoue a demarrer son serveur sans laisser la moindre trace consultable, et personne
    /// ne pouvait savoir pourquoi. Un plugin qui ne sait pas dire ce qui lui arrive est
    /// inutilisable des qu'il sort du poste de son auteur.
    ///
    /// Le fichier est sous LOCALAPPDATA, donc accessible sans droits, et il est tronque quand
    /// il grossit pour ne jamais devenir un probleme a lui tout seul.
    /// </summary>
    public static class Diagnostics
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();

        public static string LogDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Aliova", "NavisworksMcp");
            }
        }

        public static string LogPath
        {
            get { return Path.Combine(LogDirectory, "journal.txt"); }
        }

        public static void Info(string message) { Write("INFO ", message); }
        public static void Warn(string message) { Write("ATTN ", message); }

        public static void Error(string message, Exception ex)
        {
            // On ecrit la chaine complete des exceptions internes : la cause reelle est
            // presque toujours dans la derniere, pas dans celle qui a ete levee.
            var text = new StringBuilder(message);
            var current = ex;
            var depth = 0;
            while (current != null && depth < 8)
            {
                text.Append(Environment.NewLine)
                    .Append("        ")
                    .Append(current.GetType().Name)
                    .Append(" : ")
                    .Append(current.Message);
                current = current.InnerException;
                depth++;
            }
            Write("ERR  ", text.ToString());
        }

        // Identifiant du processus, calcule une fois. Le journal est partage par toutes les
        // instances de Navisworks ouvertes : sans cette colonne, on ne peut pas distinguer un
        // processus qui demarre deux fois de deux processus qui demarrent une fois chacun.
        // L'ambiguite a deja coute un faux diagnostic.
        private static readonly string ProcessTag = BuildProcessTag();

        private static string BuildProcessTag()
        {
            try
            {
                return "pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture).PadLeft(6);
            }
            catch (Exception)
            {
                return "pid      ?";
            }
        }

        private static void Write(string level, string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                       "  " + ProcessTag + "  " + level + "  " + message;

            Trace.WriteLine("[NavisworksMCP] " + message);

            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);

                    var path = LogPath;
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        // Un seul fichier de reprise : on garde la derniere tranche et on
                        // repart, plutot que d'accumuler des rotations indefiniment.
                        var previous = path + ".1";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }

                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception)
                {
                    // Journaliser ne doit jamais casser le plugin. Trace a deja recu la ligne.
                }
            }
        }
    }
}
