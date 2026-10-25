using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Outils pour l'agent lui-meme, sur le modele de pyRevit 7 : les skills (get_skill) et
    /// l'historique des appels (list_runs, get_run).
    ///
    /// Les skills sont les fichiers Skills/*.md du depot, EMBARQUES dans la DLL a la
    /// compilation. Avant, ils n'etaient lus que par Claude Code ouvert dans ce depot ; un
    /// autre client MCP (Claude Desktop, le chat integre, Cursor) travaillait sans eux et
    /// refaisait les erreurs qu'ils documentent.
    /// </summary>
    [McpServerToolType]
    public static class AgentTools
    {
        private const string SkillPrefix = "skills/";

        [McpServerTool(ReadOnly = true), Description(
            "Guides de travail pour piloter Navisworks : pièges du modèle, workflows (clash, isolation par " +
            "discipline, revue, 4D TimeLiner, code C#...). Sans argument : la liste des skills avec leur titre. " +
            "Avec `name` (numéro '00', nom de fichier, ou fragment) : le contenu complet en Markdown. " +
            "Lire '00' avant toute première tâche sur un modèle, et le skill correspondant avant un workflow.")]
        public static object get_skill(
            [Description("Numéro, nom de fichier ou fragment. Vide pour lister.")] string name = "")
        {
            var skills = LoadSkillNames();
            if (skills.Count == 0)
                return new { error = "Aucun skill embarqué dans cette version du plugin." };

            if (string.IsNullOrWhiteSpace(name))
            {
                return new
                {
                    skills = skills.Select(s => new { name = s, title = Title(s) }).ToList(),
                    hint = "get_skill('00') d'abord : pièges du modèle et règles de performance."
                };
            }

            var needle = name.Trim();
            if (needle.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) needle = needle.Substring(0, needle.Length - 3);

            var match = skills.Where(s => string.Equals(s, needle + ".md", StringComparison.OrdinalIgnoreCase)).ToList();
            if (match.Count == 0) match = skills.Where(s => s.StartsWith(needle + "_", StringComparison.OrdinalIgnoreCase)).ToList();
            if (match.Count == 0) match = skills.Where(s => s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (match.Count == 0)
                return new { error = "Aucun skill ne correspond à '" + name + "'.", skills };
            if (match.Count > 1)
                return new { error = "Plusieurs skills correspondent, préciser.", candidates = match };

            return new { name = match[0], content = Read(match[0]) };
        }

        [McpServerTool(ReadOnly = true), Description(
            "Historique des appels d'outils MCP (14 jours), du plus récent au plus ancien : id, heure UTC, outil, " +
            "succès, durée. `phase = unfinished` signale un appel qui n'a jamais rendu la main, le plus souvent " +
            "parce que Navisworks est tombé pendant l'exécution. `tool` filtre par nom d'outil (fragment). " +
            "get_run(id) donne les arguments (dont le code envoyé) et le résultat.")]
        public static object list_runs(
            [Description("Nombre d'appels à renvoyer")] int limit = 20,
            [Description("Filtre sur le nom d'outil (fragment), ex. 'navis_'")] string tool = "")
        {
            var runs = RunJournal.List(limit, tool);
            return new
            {
                directory = RunJournal.RunsDirectory,
                runs = runs.Select(r => new
                {
                    r.Id,
                    r.Utc,
                    r.Tool,
                    r.Phase,
                    r.Ok,
                    r.DurationMs,
                    error = r.Error
                }).ToList()
            };
        }

        [McpServerTool(ReadOnly = true), Description(
            "Détail d'un appel enregistré : arguments complets (dont le code C# de navis_query / navis_modify), " +
            "résultat (tronqué à 8000 caractères), erreur, durée. L'id vient de list_runs.")]
        public static object get_run(
            [Description("Identifiant renvoyé par list_runs")] string id)
        {
            var run = RunJournal.Get(id ?? "");
            if (run == null) return new { error = "Aucun appel enregistré avec l'id '" + id + "'." };
            return run;
        }

        // ------------------------------------------------------------------ ressources

        private static List<string> LoadSkillNames()
        {
            return typeof(AgentTools).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(SkillPrefix, StringComparison.Ordinal) &&
                            n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Substring(SkillPrefix.Length))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Read(string skill)
        {
            using (var stream = typeof(AgentTools).Assembly.GetManifestResourceStream(SkillPrefix + skill))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        private static string Title(string skill)
        {
            var content = Read(skill) ?? "";
            foreach (var line in content.Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith("# ", StringComparison.Ordinal)) return l.Substring(2).Trim();
            }
            return skill;
        }
    }
}
