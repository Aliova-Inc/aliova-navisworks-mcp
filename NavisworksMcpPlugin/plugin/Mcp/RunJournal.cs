using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Historique des appels d'outils, sur le modele de `pyrevit agent runs`.
    ///
    /// Chaque tools/call laisse une ligne JSON : outil, arguments (donc le code envoye a
    /// navis_query / navis_modify), resultat, duree, succes. On peut ainsi relire apres coup
    /// ce que l'agent a fait dans la maquette, y compris quand Navisworks est tombe en route :
    /// l'appel est ecrit AVANT l'execution, puis complete a la fin.
    ///
    /// Un fichier par jour sous LOCALAPPDATA\Aliova\NavisworksMcp\runs, purge au-dela de
    /// <see cref="RetentionDays"/> jours.
    /// </summary>
    public static class RunJournal
    {
        public const int RetentionDays = 14;

        private const int MaxArgumentChars = 20000;
        private const int MaxResultChars = 8000;

        private static readonly object Gate = new object();
        private static bool _pruned;

        /// <summary>Outils de lecture du journal : on ne les journalise pas eux-memes.</summary>
        private static readonly HashSet<string> Unlogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list_runs", "get_run" };

        public static string RunsDirectory
        {
            get { return Path.Combine(Diagnostics.LogDirectory, "runs"); }
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>
        /// Ouvre une entree avant l'execution. Renvoie l'identifiant, ou null si l'outil n'est
        /// pas journalise.
        /// </summary>
        public static string Begin(string tool, string argumentsJson)
        {
            if (Unlogged.Contains(tool ?? "")) return null;

            var now = DateTime.UtcNow;
            var id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                     Guid.NewGuid().ToString("N").Substring(0, 6);

            Append(now, new RunRecord
            {
                Id = id,
                Utc = now.ToString("o", CultureInfo.InvariantCulture),
                Phase = "start",
                Tool = tool,
                Arguments = Truncate(argumentsJson, MaxArgumentChars)
            });
            return id;
        }

        /// <summary>Complete l'entree ouverte par <see cref="Begin"/>.</summary>
        public static void End(string id, string tool, bool ok, long durationMs, string result, string error)
        {
            if (id == null) return;
            var now = DateTime.UtcNow;
            Append(now, new RunRecord
            {
                Id = id,
                Utc = now.ToString("o", CultureInfo.InvariantCulture),
                Phase = "end",
                Tool = tool,
                Ok = ok,
                DurationMs = durationMs,
                Result = Truncate(result, MaxResultChars),
                Error = error
            });
        }

        /// <summary>Les derniers appels, du plus recent au plus ancien, fusionnes start/end.</summary>
        public static List<RunRecord> List(int limit, string toolFilter)
        {
            var merged = ReadAll();
            IEnumerable<RunRecord> q = merged;
            if (!string.IsNullOrWhiteSpace(toolFilter))
                q = q.Where(r => r.Tool != null &&
                                 r.Tool.IndexOf(toolFilter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            return q.Take(Math.Max(1, limit)).ToList();
        }

        public static RunRecord Get(string id)
        {
            return ReadAll().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static List<RunRecord> ReadAll()
        {
            var byId = new Dictionary<string, RunRecord>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            string[] files;
            lock (Gate)
            {
                if (!System.IO.Directory.Exists(RunsDirectory)) return new List<RunRecord>();
                files = System.IO.Directory.GetFiles(RunsDirectory, "runs-*.jsonl");
            }

            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                string[] lines;
                try
                {
                    lock (Gate) lines = File.ReadAllLines(file, Encoding.UTF8);
                }
                catch (IOException) { continue; }

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    RunRecord rec;
                    try { rec = JsonSerializer.Deserialize<RunRecord>(line, JsonOpts); }
                    catch (JsonException) { continue; }
                    if (rec == null || rec.Id == null) continue;

                    RunRecord existing;
                    if (!byId.TryGetValue(rec.Id, out existing))
                    {
                        // Une entree sans "end" est un appel qui n'a jamais rendu la main :
                        // le plus souvent Navisworks est tombe pendant l'execution.
                        rec.Phase = rec.Phase == "start" ? "unfinished" : rec.Phase;
                        byId[rec.Id] = rec;
                        order.Add(rec.Id);
                    }
                    else if (rec.Phase == "end")
                    {
                        existing.Phase = "done";
                        existing.Ok = rec.Ok;
                        existing.DurationMs = rec.DurationMs;
                        existing.Result = rec.Result;
                        existing.Error = rec.Error;
                    }
                }
            }

            order.Reverse();
            return order.Select(id => byId[id]).ToList();
        }

        private static void Append(DateTime utc, RunRecord record)
        {
            var line = JsonSerializer.Serialize(record, JsonOpts);
            lock (Gate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(RunsDirectory);
                    if (!_pruned) { Prune(); _pruned = true; }
                    var path = Path.Combine(RunsDirectory,
                        "runs-" + utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                    File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    // Journaliser ne doit jamais empecher un outil de repondre.
                    Diagnostics.Warn("Journal des appels indisponible : " + ex.Message);
                }
            }
        }

        private static void Prune()
        {
            var limit = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in System.IO.Directory.GetFiles(RunsDirectory, "runs-*.jsonl"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < limit) File.Delete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string Truncate(string s, int max)
        {
            if (s == null || s.Length <= max) return s;
            return s.Substring(0, max) + " ... (tronque, " + s.Length + " caracteres)";
        }
    }

    public class RunRecord
    {
        public string Id { get; set; }
        public string Utc { get; set; }
        /// <summary>start, end, done (fusionne) ou unfinished (jamais termine).</summary>
        public string Phase { get; set; }
        public string Tool { get; set; }
        public string Arguments { get; set; }
        public bool? Ok { get; set; }
        public long? DurationMs { get; set; }
        public string Result { get; set; }
        public string Error { get; set; }
    }
}
