using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Timeliner;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Pilotage du 4D TimeLiner en API MANAGÉE (lecture et écriture des tâches).
    ///
    /// L'API vit dans une assembly SÉPARÉE, Autodesk.Navisworks.Timeliner.dll (même schéma que
    /// Autodesk.Navisworks.Clash.dll). Elle n'est PAS dans Autodesk.Navisworks.Api.dll, où
    /// IDocumentTimeliner n'est qu'une interface marqueur vide : c'est ce qui avait fait conclure
    /// à tort que le TimeLiner n'était pilotable que par COM ou par import CSV.
    ///
    /// Chaîne d'attachement d'une tâche à un jeu de sélection (équivalent du bouton « Attach Set ») :
    ///     doc.SelectionSets.CreateSelectionSource(set) -> SelectionSource
    ///     task.Selection.SelectionSources.Add(source)
    ///     tl.TaskAddCopy(task)
    /// TaskAddCopy insère une COPIE : la tâche doit être entièrement remplie AVANT l'ajout, et
    /// relue depuis tl.Tasks si on veut la modifier ensuite.
    /// </summary>
    [McpServerToolType]
    public static class TimelinerTools
    {
        // ------------------------------------------------------------------ helpers

        private static DocumentTimeliner Tl(Document doc) => TimelinerDocumentExtensions.GetTimeliner(doc);

        private static bool TryDate(string s, out DateTime d)
        {
            d = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        private static string Iso(DateTime? d) =>
            d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>Minuscules sans accents : les noms de types de tâche sont localisés selon l'installation.</summary>
        private static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var ch in s.Normalize(NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static List<SimulationTaskType> TaskTypes(DocumentTimeliner tl)
        {
            var list = new List<SimulationTaskType>();
            foreach (var it in tl.SimulationTaskTypes)
                if (it is SimulationTaskType t) list.Add(t);
            return list;
        }

        /// <summary>
        /// Résout un nom de type de tâche. Si <paramref name="explicitName"/> est fourni il doit exister
        /// tel quel. Sinon on cherche par racine tolérante aux accents et à la langue
        /// (Construct / Construire / Construction, Demolish / Démolir / Démolition).
        /// Retourne null si rien ne correspond : l'appelant doit alors remonter la liste disponible
        /// plutôt que de deviner, car un SimulationTaskTypeName inconnu donne une simulation muette.
        /// </summary>
        private static string ResolveTaskType(DocumentTimeliner tl, string explicitName, string[] needles)
        {
            var types = TaskTypes(tl);
            if (!string.IsNullOrWhiteSpace(explicitName))
                return types.Any(t => t.DisplayName == explicitName) ? explicitName : null;

            foreach (var t in types)
            {
                var folded = Fold(t.DisplayName);
                if (needles.Any(n => folded.Contains(n))) return t.DisplayName;
            }
            return null;
        }

        private static readonly string[] ConstructNeedles = { "construct", "constru" };
        private static readonly string[] DemolishNeedles = { "demol" };

        /// <summary>Parcourt l'arbre des tâches (elles peuvent être groupées) en produisant un chemin lisible.</summary>
        private static void WalkTasks(SavedItemCollection items, string prefix, Action<TimelinerTask, string> visit)
        {
            foreach (var it in items)
            {
                if (!(it is TimelinerTask t)) continue;
                var path = string.IsNullOrEmpty(prefix) ? t.DisplayName : prefix + " / " + t.DisplayName;
                visit(t, path);
                if (t.IsGroup) WalkTasks(t.Children, path, visit);
            }
        }

        private static TimelinerTask FindTask(DocumentTimeliner tl, string name)
        {
            TimelinerTask found = null;
            WalkTasks(tl.Tasks, "", (t, path) => { if (found == null && t.DisplayName == name) found = t; });
            return found;
        }

        /// <summary>Cherche un set par nom : d'abord dans le dossier visé, sinon partout dans l'arbre des Sets.</summary>
        private static SavedItem FindSetAnywhere(Document doc, string setName, string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var inFolder = TimelinerPrepTools.ResolveSetsInFolder(doc, folder);
                if (inFolder.TryGetValue(setName, out var hit)) return hit;
            }

            SavedItem found = null;
            void Walk(SavedItem item)
            {
                if (found != null) return;
                if (item is FolderItem f) { foreach (var c in f.Children) Walk(c); }
                else if (item is SelectionSet s && s.DisplayName == setName) found = s;
            }
            foreach (var child in doc.SelectionSets.RootItem.Children) Walk(child);
            return found;
        }

        /// <summary>
        /// Attache un jeu de sélection à une tâche.
        ///
        /// PIÈGE (constaté le 2026-07-30) : sur une tâche NEUVE, lire `task.Selection.SelectionSources`
        /// lève « Invalid operation when '!HasSelectionSources' ». La collection n'existe pas encore,
        /// donc on ne peut pas y faire `Add()`. Il faut construire une SelectionSourceCollection à part
        /// et la pousser d'un bloc par `CopyFrom`, qui est la surcharge prévue pour ça.
        ///
        /// <paramref name="method"/> dit ce qui a réellement été posé : "SelectionSources" = lien VIF
        /// vers le set (le set évolue, la tâche suit), "ExplicitSelection" = repli en liste FIGÉE.
        /// </summary>
        /// <summary>
        /// Attache UN OU PLUSIEURS jeux de sélection à une tâche. Le multi-jeux sert par exemple à
        /// faire tomber, dans la même tâche d'étage, le jeu par niveau ('NIV - Level 12') ET le jeu
        /// des éléments sans niveau rattachés par altitude ('NIVZ - Level 12').
        /// </summary>
        private static bool AttachSets(Document doc, TimelinerTask task, IList<SavedItem> sets, out string error, out string method)
        {
            error = null;
            method = null;
            if (task.Selection == null) { error = "task.Selection est null : impossible d'attacher un set."; return false; }
            if (sets == null || sets.Count == 0) { error = "Aucun set à attacher."; return false; }

            try
            {
                // CopyFrom REMPLACE la sélection : il faut donc pousser tous les sets d'un bloc.
                var sources = new SelectionSourceCollection();
                foreach (var s in sets) sources.Add(doc.SelectionSets.CreateSelectionSource(s));
                task.Selection.CopyFrom(sources);
                method = "SelectionSources";
                return true;
            }
            catch (Exception exLive)
            {
                // Repli : sélection explicite. On perd le lien vif au set mais la tâche pilote
                // quand même les bons éléments, ce qui vaut mieux qu'une tâche vide.
                try
                {
                    var items = new ModelItemCollection();
                    foreach (var s in sets)
                        if (s is SelectionSet ss) items.AddRange(ss.GetSelectedItems());
                    if (items.Count == 0)
                    {
                        error = "Attachement impossible : aucun set exploitable. Lien vif refusé: " + exLive.Message;
                        return false;
                    }
                    task.Selection.CopyFrom(items);
                    method = "ExplicitSelection";
                    return true;
                }
                catch (Exception exExplicit)
                {
                    error = "Attachement impossible. Lien vif: " + exLive.Message +
                            " | Repli explicite: " + exExplicit.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// PIÈGE (constaté le 2026-07-30) : sur une tâche NEUVE, lire `task.Selection.SelectionSources`
        /// lève « Invalid operation when '!HasSelectionSources' ». La collection n'existe pas encore,
        /// donc on ne peut pas y faire `Add()`. Il faut construire une SelectionSourceCollection à part
        /// et la pousser d'un bloc par `CopyFrom`, qui est la surcharge prévue pour ça.
        ///
        /// <paramref name="method"/> dit ce qui a réellement été posé : "SelectionSources" = lien VIF
        /// vers le set (le set évolue, la tâche suit), "ExplicitSelection" = repli en liste FIGÉE.
        /// </summary>
        private static bool AttachSet(Document doc, TimelinerTask task, SavedItem set, out string error, out string method) =>
            AttachSets(doc, task, new List<SavedItem> { set }, out error, out method);

        /// <summary>
        /// Tri naturel des noms de phase : « Position 2 » avant « Position 10 » (un tri
        /// alphabétique les inverserait). Le nom épinglé passe toujours en premier.
        /// </summary>
        private static IEnumerable<string> OrderPhases(IEnumerable<string> phases, string firstPhase)
        {
            (string head, long num) Key(string p)
            {
                int i = p.Length;
                while (i > 0 && char.IsDigit(p[i - 1])) i--;
                var head = p.Substring(0, i).TrimEnd();
                var tail = p.Substring(i);
                return (head, long.TryParse(tail, out var n) ? n : -1);
            }

            return phases
                .OrderBy(p => string.Equals(p, firstPhase, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => Key(p).head, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => Key(p).num);
        }

        // ------------------------------------------------------------------ lecture

        [McpServerTool(ReadOnly = true), Description(
            "Liste les types de tâche de simulation TimeLiner (Construct / Demolish / etc.) avec leur " +
            "apparence de début, de fin, de dépassement et de sous-dépassement. " +
            "À APPELER AVANT de créer des tâches : les noms sont LOCALISÉS selon l'installation " +
            "(« Construire », « Démolir »…) et un SimulationTaskTypeName inconnu donne une simulation muette. " +
            "Liste aussi les apparences disponibles.")]
        public static Task<TaskTypesResult> timeliner_list_task_types() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = Tl(doc);

                string Describe(SimulationStatus st) =>
                    st == null ? null : st.AppearanceMode + (string.IsNullOrEmpty(st.SimulationAppearanceName)
                        ? "" : " (" + st.SimulationAppearanceName + ")");

                var types = TaskTypes(tl).Select(t => new TaskTypeInfo
                {
                    Name = t.DisplayName,
                    StartStatus = Describe(t.StartStatus),
                    EndStatus = Describe(t.EndStatus),
                    OverrunStatus = Describe(t.OverrunStatus),
                    UnderrunStatus = Describe(t.UnderrunStatus)
                }).ToArray();

                var appearances = new List<string>();
                foreach (var a in tl.SimulationAppearances)
                    if (a is SimulationAppearance ap) appearances.Add(ap.DisplayName);

                return new TaskTypesResult
                {
                    Count = types.Length,
                    TaskTypes = types,
                    Appearances = appearances.ToArray(),
                    AutoDetectedConstruct = ResolveTaskType(tl, null, ConstructNeedles),
                    AutoDetectedDemolish = ResolveTaskType(tl, null, DemolishNeedles)
                };
            });

        [McpServerTool(ReadOnly = true), Description(
            "Liste les tâches TimeLiner (nom, chemin si groupées, type, dates planifiées et réelles, " +
            "avancement, ce à quoi la tâche est attachée). " +
            "resolveCounts=true résout en plus le nombre d'éléments réellement sélectionnés par tâche : " +
            "exact mais coûteux sur un gros modèle, à n'activer que pour une vérification finale.")]
        public static Task<TasksResult> timeliner_list_tasks(
            [Description("Résoudre le nombre d'éléments attachés par tâche (coûteux, def false)")] bool resolveCounts = false) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = Tl(doc);
                var rows = new List<TaskInfo>();

                WalkTasks(tl.Tasks, "", (t, path) =>
                {
                    // Les accesseurs de TimelinerSelection sont gardés : plusieurs lèvent
                    // « Invalid operation when '!HasSelectionSources' » quand rien n'est attaché.
                    // On lit donc chaque champ sous protection, et on expose les deux drapeaux :
                    // c'est ce qui permet de voir d'un coup d'œil qu'une tâche est VIDE.
                    bool hasSources = false, hasExplicit = false;
                    string attachment = null;
                    int attached = -1;

                    if (t.Selection != null)
                    {
                        try { hasSources = t.Selection.HasSelectionSources; } catch { }
                        try { hasExplicit = t.Selection.HasExplicitSelection; } catch { }
                        try { attachment = t.Selection.DisplayString; } catch { attachment = "(illisible)"; }
                        if (resolveCounts && (hasSources || hasExplicit))
                        {
                            try { attached = t.Selection.GetSelectedItems(doc).Count; } catch { attached = -1; }
                        }
                    }

                    rows.Add(new TaskInfo
                    {
                        Path = path,
                        Name = t.DisplayName,
                        TaskType = t.SimulationTaskTypeName,
                        PlannedStart = Iso(t.PlannedStartDate),
                        PlannedEnd = Iso(t.PlannedEndDate),
                        ActualStart = Iso(t.ActualStartDate),
                        ActualEnd = Iso(t.ActualEndDate),
                        ProgressPercent = t.ProgressPercent,
                        IsGroup = t.IsGroup,
                        IsEnabled = t.IsEnabled,
                        Attachment = attachment,
                        HasSelectionSources = hasSources,
                        HasExplicitSelection = hasExplicit,
                        AttachedCount = attached
                    });
                });

                return new TasksResult
                {
                    TotalTasks = (int)tl.TaskTotalTasks(),
                    Count = rows.Count,
                    Tasks = rows.ToArray()
                };
            });

        // ------------------------------------------------------------------ écriture

        [McpServerTool, Description(
            "Crée UNE tâche TimeLiner et l'attache à un jeu de sélection existant (équivalent " +
            "« Add Task » + « Attach Set » de l'UI). Les dates sont en ISO (2030-01-07, " +
            "optionnellement 2030-01-07 08:00). Laisser taskType vide pour l'auto-détection " +
            "Construct/Demolish tolérante à la langue. Pour bâtir toute une séquence d'un coup, " +
            "préférer timeliner_build_from_phases.")]
        public static Task<AddTaskResult> timeliner_add_task(
            [Description("Nom de la tâche")] string name,
            [Description("Nom du ou des sets à attacher, SÉPARÉS PAR ';' (vide = tâche sans attachement). " +
                         "Ex: 'NIV - Level 12;NIVZ - Level 12' pour prendre le niveau ET les éléments sans niveau")] string setName = "",
            [Description("Type: 'construct', 'demolish', ou le nom exact d'un type existant (vide = construct)")] string taskType = "",
            [Description("Date de début planifiée, ISO (ex 2030-01-07)")] string startDate = "",
            [Description("Date de fin planifiée, ISO")] string endDate = "",
            [Description("Dossier de Sets où chercher setName en priorité (def '4D')")] string folder = "4D") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = Tl(doc);

                var folded = Fold(taskType);
                string resolvedType;
                if (string.IsNullOrWhiteSpace(taskType) || folded == "construct")
                    resolvedType = ResolveTaskType(tl, null, ConstructNeedles);
                else if (folded == "demolish" || folded == "demolir" || folded == "demolition")
                    resolvedType = ResolveTaskType(tl, null, DemolishNeedles);
                else
                    resolvedType = ResolveTaskType(tl, taskType, null);

                if (resolvedType == null)
                    return new AddTaskResult
                    {
                        Error = "Type de tâche introuvable: '" + taskType + "'. Voir timeliner_list_task_types.",
                        AvailableTaskTypes = TaskTypes(tl).Select(t => t.DisplayName).ToArray()
                    };

                var task = new TimelinerTask { DisplayName = name, SimulationTaskTypeName = resolvedType };
                if (TryDate(startDate, out var sd)) task.PlannedStartDate = sd;
                if (TryDate(endDate, out var ed)) task.PlannedEndDate = ed;

                string attachedTo = null, attachMethod = null;
                if (!string.IsNullOrWhiteSpace(setName))
                {
                    var wanted = setName.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    var resolved = new List<SavedItem>();
                    var missing = new List<string>();
                    foreach (var w in wanted)
                    {
                        var set = FindSetAnywhere(doc, w, folder);
                        if (set == null) missing.Add(w); else resolved.Add(set);
                    }
                    if (missing.Count > 0)
                        return new AddTaskResult { Error = "Set(s) introuvable(s): '" + string.Join("', '", missing) + "'" };
                    if (!AttachSets(doc, task, resolved, out var attachError, out attachMethod))
                        return new AddTaskResult { Error = attachError };
                    attachedTo = string.Join(" + ", wanted);
                }

                tl.TaskAddCopy(task);

                return new AddTaskResult
                {
                    Name = name,
                    TaskType = resolvedType,
                    AttachedSet = attachedTo,
                    AttachMethod = attachMethod,
                    PlannedStart = Iso(task.PlannedStartDate),
                    PlannedEnd = Iso(task.PlannedEndDate),
                    TotalTasks = (int)tl.TaskTotalTasks()
                };
            });

        [McpServerTool, Description(
            "Supprime TOUTES les tâches TimeLiner du document (les jeux de sélection, eux, ne sont pas " +
            "touchés). Sert à régénérer une séquence proprement.")]
        public static Task<ClearTasksResult> timeliner_clear_tasks() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = Tl(doc);
                int before = (int)tl.TaskTotalTasks();
                tl.TasksClear();
                return new ClearTasksResult { Removed = before, Remaining = (int)tl.TaskTotalTasks() };
            });

        [McpServerTool, Description(
            "Change les dates planifiées d'une tâche existante, désignée par son nom exact. " +
            "Dates en ISO ; laisser un champ vide pour ne pas y toucher.")]
        public static Task<SetDatesResult> timeliner_set_task_dates(
            [Description("Nom exact de la tâche")] string taskName,
            [Description("Nouvelle date de début planifiée, ISO (vide = inchangée)")] string startDate = "",
            [Description("Nouvelle date de fin planifiée, ISO (vide = inchangée)")] string endDate = "") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = Tl(doc);

                var task = FindTask(tl, taskName);
                if (task == null) return new SetDatesResult { Error = "Tâche introuvable: '" + taskName + "'" };

                var hasStart = TryDate(startDate, out var sd);
                var hasEnd = TryDate(endDate, out var ed);
                if (!hasStart && !hasEnd)
                    return new SetDatesResult { Error = "Aucune date valide fournie (format ISO attendu, ex 2030-01-07)." };

                // Les tâches déjà dans le document peuvent être en lecture seule : dans ce cas il faut
                // passer par une copie et TaskEdit(parent, index, nouvellesValeurs).
                if (!task.IsReadOnly)
                {
                    if (hasStart) task.PlannedStartDate = sd;
                    if (hasEnd) task.PlannedEndDate = ed;
                    return new SetDatesResult
                    {
                        Name = taskName, Method = "direct",
                        PlannedStart = Iso(task.PlannedStartDate), PlannedEnd = Iso(task.PlannedEndDate)
                    };
                }

                var parent = task.Parent ?? tl.TasksRoot;
                int index = -1;
                for (int i = 0; i < parent.Children.Count(); i++)
                    if (ReferenceEquals(parent.Children.ElementAt(i), task) ||
                        (parent.Children.ElementAt(i) as TimelinerTask)?.DisplayName == taskName) { index = i; break; }
                if (index < 0) return new SetDatesResult { Error = "Index de la tâche introuvable dans son parent." };

                var copy = task.CreateCopy();
                if (hasStart) copy.PlannedStartDate = sd;
                if (hasEnd) copy.PlannedEndDate = ed;
                tl.TaskEdit(parent, index, copy);

                return new SetDatesResult
                {
                    Name = taskName, Method = "TaskEdit",
                    PlannedStart = Iso(copy.PlannedStartDate), PlannedEnd = Iso(copy.PlannedEndDate)
                };
            });

        [McpServerTool, Description(
            "GESTE PRINCIPAL, rejouable sur tout modèle Revit phasé : construit la séquence 4D complète " +
            "en un appel. Regroupe les éléments par phase, crée les sets 'CREE - <phase>' et " +
            "'DEMO - <phase>', puis une tâche TimeLiner par set, typée Construct ou Demolish, attachée " +
            "au set, et datée en paliers consécutifs de daysPerStep jours depuis startDate. " +
            "Ordre chronologique : la phase épinglée (def 'Existing') d'abord, puis les phases en ordre " +
            "naturel, et pour une même phase la création avant la démolition. " +
            "rebuildSets=false réutilise les sets déjà présents dans le dossier au lieu de les régénérer.")]
        public static Task<BuildFromPhasesResult> timeliner_build_from_phases(
            [Description("Date de début du 1er palier, ISO (ex 2030-01-07)")] string startDate,
            [Description("Durée d'un palier en jours (def 7)")] int daysPerStep = 7,
            [Description("Dossier des sets de phase (def '4D')")] string folder = "4D",
            [Description("Régénérer les sets de phase avant de bâtir les tâches (def true)")] bool rebuildSets = true,
            [Description("Supprimer les tâches existantes avant (def true)")] bool clearTasks = true,
            [Description("Nom exact du type Construct (vide = auto-détection)")] string constructTaskType = "",
            [Description("Nom exact du type Demolish (vide = auto-détection)")] string demolishTaskType = "",
            [Description("Phase épinglée en premier palier (def 'Existing')")] string firstPhase = "Existing",
            [Description("Préfixe des sets de création (def 'CREE - ')")] string creePrefix = "CREE - ",
            [Description("Préfixe des sets de démolition (def 'DEMO - ')")] string demoPrefix = "DEMO - ") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = Tl(doc);

                if (!TryDate(startDate, out var start))
                    return new BuildFromPhasesResult { Error = "startDate invalide: '" + startDate + "' (ISO attendu, ex 2030-01-07)" };
                if (daysPerStep <= 0)
                    return new BuildFromPhasesResult { Error = "daysPerStep doit être positif." };

                // 1. Les sets : régénérés, ou relus dans le dossier.
                var steps = new List<(string SetName, string Kind, string Phase, int Count, SavedItem Item)>();
                if (rebuildSets)
                {
                    TimelinerPrepTools.ClearSetsFolder(doc, folder, out _);
                    var refs = TimelinerPrepTools.BuildPhaseSets(doc, folder,
                        "Phase de création", "Nom", "Phase de démolition", "Nom",
                        creePrefix, demoPrefix, out var setError);
                    if (setError != null) return new BuildFromPhasesResult { Error = setError };
                    foreach (var r in refs)
                        steps.Add((r.Name, r.Kind, r.Phase, r.Count, r.Item));
                }
                else
                {
                    foreach (var kv in TimelinerPrepTools.ResolveSetsInFolder(doc, folder))
                    {
                        string kind = null, phase = null;
                        if (kv.Key.StartsWith(creePrefix, StringComparison.Ordinal))
                        { kind = "Construct"; phase = kv.Key.Substring(creePrefix.Length); }
                        else if (kv.Key.StartsWith(demoPrefix, StringComparison.Ordinal))
                        { kind = "Demolish"; phase = kv.Key.Substring(demoPrefix.Length); }
                        else continue; // set étranger à la nomenclature de phase : ignoré
                        steps.Add((kv.Key, kind, phase, -1, kv.Value));
                    }
                }

                if (steps.Count == 0)
                    return new BuildFromPhasesResult { Error = "Aucun set de phase dans le dossier '" + folder + "'." };

                // 2. Les types de tâche, seulement ceux dont on a besoin.
                string construct = null, demolish = null;
                if (steps.Any(s => s.Kind == "Construct"))
                {
                    construct = ResolveTaskType(tl, constructTaskType, ConstructNeedles);
                    if (construct == null)
                        return new BuildFromPhasesResult
                        {
                            Error = "Type de tâche Construct introuvable. Passez constructTaskType explicitement.",
                            AvailableTaskTypes = TaskTypes(tl).Select(t => t.DisplayName).ToArray()
                        };
                }
                if (steps.Any(s => s.Kind == "Demolish"))
                {
                    demolish = ResolveTaskType(tl, demolishTaskType, DemolishNeedles);
                    if (demolish == null)
                        return new BuildFromPhasesResult
                        {
                            Error = "Type de tâche Demolish introuvable. Passez demolishTaskType explicitement.",
                            AvailableTaskTypes = TaskTypes(tl).Select(t => t.DisplayName).ToArray()
                        };
                }

                // 3. Chronologie : phase épinglée, puis ordre naturel, création avant démolition.
                var phaseOrder = OrderPhases(steps.Select(s => s.Phase).Distinct(), firstPhase).ToList();
                var ordered = steps
                    .OrderBy(s => phaseOrder.IndexOf(s.Phase))
                    .ThenBy(s => s.Kind == "Construct" ? 0 : 1)
                    .ToList();

                // 4. Les tâches.
                if (clearTasks) tl.TasksClear();

                var made = new List<BuiltTask>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var s = ordered[i];
                    var from = start.AddDays((double)i * daysPerStep);
                    var to = from.AddDays(daysPerStep);

                    var task = new TimelinerTask
                    {
                        DisplayName = s.SetName,
                        SimulationTaskTypeName = s.Kind == "Construct" ? construct : demolish,
                        PlannedStartDate = from,
                        PlannedEndDate = to
                    };

                    string warn = null, attachMethod = null;
                    if (s.Item == null) warn = "set non résolu, tâche créée sans attachement";
                    else if (!AttachSet(doc, task, s.Item, out var attachError, out attachMethod)) warn = attachError;

                    tl.TaskAddCopy(task);

                    made.Add(new BuiltTask
                    {
                        Step = i + 1,
                        Name = s.SetName,
                        Kind = s.Kind,
                        Phase = s.Phase,
                        TaskType = task.SimulationTaskTypeName,
                        ElementCount = s.Count,
                        PlannedStart = Iso(from),
                        PlannedEnd = Iso(to),
                        AttachMethod = attachMethod,
                        Warning = warn
                    });
                }

                return new BuildFromPhasesResult
                {
                    Folder = folder,
                    SetsRebuilt = rebuildSets,
                    ConstructTaskType = construct,
                    DemolishTaskType = demolish,
                    TasksCreated = made.Count,
                    TotalTasks = (int)tl.TaskTotalTasks(),
                    TimelineStart = Iso(start),
                    TimelineEnd = Iso(start.AddDays((double)made.Count * daysPerStep)),
                    Tasks = made.ToArray()
                };
            });
    }

    // ---------------------------------------------------------------------- DTO

    public class TaskTypesResult
    {
        public int Count { get; set; }
        public TaskTypeInfo[] TaskTypes { get; set; }
        public string[] Appearances { get; set; }
        public string AutoDetectedConstruct { get; set; }
        public string AutoDetectedDemolish { get; set; }
    }

    public class TaskTypeInfo
    {
        public string Name { get; set; }
        public string StartStatus { get; set; }
        public string EndStatus { get; set; }
        public string OverrunStatus { get; set; }
        public string UnderrunStatus { get; set; }
    }

    public class TasksResult
    {
        public int TotalTasks { get; set; }
        public int Count { get; set; }
        public TaskInfo[] Tasks { get; set; }
    }

    public class TaskInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string TaskType { get; set; }
        public string PlannedStart { get; set; }
        public string PlannedEnd { get; set; }
        public string ActualStart { get; set; }
        public string ActualEnd { get; set; }
        public double? ProgressPercent { get; set; }
        public bool IsGroup { get; set; }
        public bool IsEnabled { get; set; }
        public string Attachment { get; set; }
        public bool HasSelectionSources { get; set; }
        public bool HasExplicitSelection { get; set; }
        public int AttachedCount { get; set; }
    }

    public class AddTaskResult
    {
        public string Name { get; set; }
        public string TaskType { get; set; }
        public string AttachedSet { get; set; }
        public string AttachMethod { get; set; }
        public string PlannedStart { get; set; }
        public string PlannedEnd { get; set; }
        public int TotalTasks { get; set; }
        public string Error { get; set; }
        public string[] AvailableTaskTypes { get; set; }
    }

    public class ClearTasksResult
    {
        public int Removed { get; set; }
        public int Remaining { get; set; }
    }

    public class SetDatesResult
    {
        public string Name { get; set; }
        public string Method { get; set; }
        public string PlannedStart { get; set; }
        public string PlannedEnd { get; set; }
        public string Error { get; set; }
    }

    public class BuildFromPhasesResult
    {
        public string Folder { get; set; }
        public bool SetsRebuilt { get; set; }
        public string ConstructTaskType { get; set; }
        public string DemolishTaskType { get; set; }
        public int TasksCreated { get; set; }
        public int TotalTasks { get; set; }
        public string TimelineStart { get; set; }
        public string TimelineEnd { get; set; }
        public BuiltTask[] Tasks { get; set; }
        public string Error { get; set; }
        public string[] AvailableTaskTypes { get; set; }
    }

    public class BuiltTask
    {
        public int Step { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Phase { get; set; }
        public string TaskType { get; set; }
        public int ElementCount { get; set; }
        public string PlannedStart { get; set; }
        public string PlannedEnd { get; set; }
        public string AttachMethod { get; set; }
        public string Warning { get; set; }
    }
}
