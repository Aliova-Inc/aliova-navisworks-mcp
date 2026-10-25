using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CSharp;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// EXÉCUTION DE CODE C# À CHAUD dans le processus Navisworks, en deux outils sur le modèle
    /// du runtime agent de pyRevit 7 (run_query / run_modify).
    ///
    /// Raison d'être : en phase d'exploration d'une API non documentée, chaque question demande un
    /// bout de code différent. Ajouter un outil MCP par question impose un cycle
    /// « compiler → réinstaller le plugin → relancer », insoutenable. Le code arrive en texte, est
    /// compilé en mémoire et exécuté sur le fil de l'interface.
    ///
    /// Pourquoi DEUX outils et plus un seul `navis_eval` : un outil unique laissait l'agent écrire
    /// dans la maquette sans que personne ne le voie. Désormais :
    ///   - `navis_query` lit. L'IL compilé est vérifié AVANT exécution (CodePolicy) : tout appel
    ///     qui peut modifier le document est refusé, le code ne tourne pas.
    ///   - `navis_modify` écrit. Les appels d'écriture sont listés depuis l'IL et montrés à
    ///     l'utilisateur dans Navisworks, qui approuve ou refuse AVANT exécution (ModifyApproval).
    ///     Les changements approuvés sont regroupés dans une transaction quand l'API en offre une.
    ///   - Dans les deux cas, enregistrer / publier / fermer le document et pomper la boucle de
    ///     messages sont refusés (CodePolicy.AlwaysForbidden).
    ///
    /// LANGAGE : le compilateur intégré au .NET Framework est utilisé (CodeDom), donc **C# 5**.
    /// Pas d'interpolation de chaînes ($"..."), pas de `?.`, pas de `nameof`, pas de membres à
    /// corps d'expression, pas d'initialiseur de propriété automatique. `var`, LINQ, lambdas et
    /// génériques fonctionnent normalement.
    /// </summary>
    [McpServerToolType]
    public static class EvalTools
    {
        private const string Prologue = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Timeliner;
using NavisworksMcpPlugin.Tools;

public static class __Snippet
{
    public static readonly StringBuilder __log = new StringBuilder();

    /// <summary>Journalise une ligne. Le journal est renvoyé même si Run lève ensuite.</summary>
    static void P(object o) { __log.AppendLine(o == null ? ""(null)"" : o.ToString()); }

    /// <summary>Journalise une ligne formatée, C# 5 n'ayant pas l'interpolation de chaînes.</summary>
    static void P(string fmt, params object[] a) { __log.AppendLine(string.Format(CultureInfo.InvariantCulture, fmt, a)); }

    public static object Run()
    {
        var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
#line 1 ""snippet""
";

        private const string Epilogue = @"
#line default
    }
}
";

        private const string ContractDescription =
            "\n\nCONTRAT par défaut : `code` est un CORPS DE MÉTHODE qui DOIT se terminer par un `return`. " +
            "Disponibles sans rien écrire : la variable `doc` (Document actif), les espaces de noms " +
            "Autodesk.Navisworks.Api / .Interop / .Timeliner, et `P(...)` pour journaliser une ligne " +
            "(le journal est renvoyé même si le code lève ensuite). " +
            "\n\nLANGAGE C# 5 : pas d'interpolation $\"...\", pas de `?.`, pas de `nameof`, pas de membre à " +
            "corps d'expression. Utiliser string.Format, des vérifications de null explicites, des méthodes " +
            "classiques. `var`, LINQ, lambdas et génériques fonctionnent. " +
            "\n\nfullSource=true : `code` est alors le FICHIER SOURCE COMPLET, qui doit déclarer " +
            "`public static class __Snippet` avec `public static object Run()`. " +
            "\n\nTOUJOURS REFUSÉ, dans les deux outils : enregistrer, publier, ouvrir, fermer ou vider un " +
            "document, Application.DoEvents, Process.Start, Environment.Exit. Détail : get_skill('14').";

        [McpServerTool(ReadOnly = true), Description(
            "Compile et exécute du C# EN LECTURE SEULE dans Navisworks, sur le fil de l'interface. " +
            "Pour explorer l'API, sonder le modèle, compter, lire des propriétés. " +
            "Le code compilé est vérifié AVANT exécution : tout appel qui peut modifier le document " +
            "(setter, méthode d'écriture de l'API, ajout d'événement, appel d'un outil d'isolation...) " +
            "est refusé et rien ne s'exécute ; la réponse liste les appels refusés dans `Violations`. " +
            "Sont aussi refusés : invocation par réflexion (lire les métadonnées reste libre), Emit, " +
            "`dynamic`, écriture de fichiers. Pour écrire : navis_modify." +
            ContractDescription)]
        public static Task<EvalResult> navis_query(
            [Description("Code C#. Corps de méthode terminé par 'return', ou fichier complet si fullSource=true.")] string code,
            [Description("true si 'code' est un fichier source complet déclarant __Snippet.Run()")] bool fullSource = false,
            [Description("Assemblies supplémentaires à référencer, chemins ou noms courts, séparés par ';'")] string extraReferences = "") =>
            Execute(RunMode.Query, null, code, fullSource, extraReferences);

        [McpServerTool, Description(
            "Compile et exécute du C# QUI MODIFIE la maquette Navisworks. " +
            "Avant exécution, les appels d'écriture sont lus dans le code compilé et montrés à " +
            "l'utilisateur dans Navisworks avec le titre et le code : RIEN ne s'exécute sans son " +
            "approbation (politique 'ask', réglable par ALIOVA_NAVISWORKS_MCP_MODIFY = ask | allow | deny). " +
            "Les changements approuvés sont regroupés dans une transaction « Agent: <title> » quand " +
            "l'API en offre une. Toujours sonder d'abord avec navis_query, et demander à l'utilisateur " +
            "d'enregistrer le .nwf avant la première écriture. Ne pas utiliser quand un outil dédié existe." +
            ContractDescription)]
        public static async Task<EvalResult> navis_modify(
            [Description("Titre court de la modification, montré à l'utilisateur (ex. 'Colorer les gaines en rouge').")] string title,
            [Description("Code C#. Corps de méthode terminé par 'return', ou fichier complet si fullSource=true.")] string code,
            [Description("true si 'code' est un fichier source complet déclarant __Snippet.Run()")] bool fullSource = false,
            [Description("Assemblies supplémentaires à référencer, chemins ou noms courts, séparés par ';'")] string extraReferences = "")
        {
            if (string.IsNullOrWhiteSpace(title))
                return new EvalResult { Mode = "modify", Error = "Le titre est obligatoire : il est montré à l'utilisateur." };
            return await Execute(RunMode.Modify, title.Trim(), code, fullSource, extraReferences).ConfigureAwait(false);
        }

        /// <summary>
        /// Un seul run de code à la fois. La boîte d'approbation est modale : pendant qu'elle est
        /// ouverte, le fil d'interface traite encore les messages, et un second run posté à ce
        /// moment s'exécuterait sous la boîte, avant même la réponse de l'utilisateur.
        /// </summary>
        private static readonly SemaphoreSlim RunGate = new SemaphoreSlim(1, 1);

        private static async Task<EvalResult> Execute(RunMode mode, string title, string code, bool fullSource, string extraReferences)
        {
            await RunGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await ExecuteExclusive(mode, title, code, fullSource, extraReferences).ConfigureAwait(false);
            }
            finally
            {
                RunGate.Release();
            }
        }

        private static async Task<EvalResult> ExecuteExclusive(RunMode mode, string title, string code, bool fullSource, string extraReferences)
        {
            var modeName = mode == RunMode.Query ? "query" : "modify";
            if (string.IsNullOrWhiteSpace(code))
                return new EvalResult { Mode = modeName, Error = "Le code est vide." };

            if (mode == RunMode.Modify && ModifyApproval.Current == ModifyApproval.Policy.Deny)
                return new EvalResult
                {
                    Mode = modeName,
                    Error = "Écriture désactivée sur ce poste (" + ModifyApproval.PolicyEnvironmentVariable + "=deny).",
                    Hint = "Utiliser navis_query ou un outil dédié."
                };

            var sw = Stopwatch.StartNew();

            // Compilation et vérification HORS du fil d'interface : csc.exe tourne dans un
            // processus à part, inutile de geler Navisworks pendant ce temps.
            var compiled = await Task.Run(() => Compile(code, fullSource, extraReferences)).ConfigureAwait(false);
            if (compiled.Failure != null)
            {
                compiled.Failure.Mode = modeName;
                return compiled.Failure;
            }

            var report = CodePolicy.Inspect(compiled.Assembly, mode);
            if (!report.Allowed)
            {
                return new EvalResult
                {
                    Mode = modeName,
                    Error = "Refusé avant exécution : " + report.Violations.Count + " appel(s) interdit(s) dans ce mode. Rien n'a été exécuté.",
                    Violations = report.Violations.ToArray(),
                    Hint = mode == RunMode.Query
                        ? "navis_query est en lecture seule. Pour modifier le document, utiliser navis_modify (soumis à l'approbation de l'utilisateur)."
                        : "Ces appels sont bloqués dans tous les modes : l'enregistrement et la fermeture restent à l'utilisateur."
                };
            }

            return await UiThread.Run(() =>
            {
                var approved = (bool?)null;
                if (mode == RunMode.Modify)
                {
                    approved = ModifyApproval.Current == ModifyApproval.Policy.Allow ||
                               ModifyApproval.Ask(title, report.Mutations, code);
                    if (approved == false)
                    {
                        return new EvalResult
                        {
                            Mode = modeName,
                            Approved = false,
                            Mutations = report.Mutations.ToArray(),
                            Error = "Refusé par l'utilisateur dans Navisworks. Rien n'a été exécuté.",
                            Hint = "Demander à l'utilisateur ce qu'il souhaite changer avant de reproposer."
                        };
                    }
                }

                var result = Run(compiled.Assembly, mode == RunMode.Modify ? "Agent: " + title : null);
                sw.Stop();
                result.Mode = modeName;
                result.Approved = approved;
                result.Mutations = mode == RunMode.Modify ? report.Mutations.ToArray() : null;
                result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }).ConfigureAwait(false);
        }

        private sealed class Compiled
        {
            public Assembly Assembly;
            public EvalResult Failure;
        }

        private static Compiled Compile(string code, bool fullSource, string extraReferences)
        {
            var source = fullSource ? code : Prologue + code + Epilogue;

            CompilerResults results;
            try
            {
                var provider = new CSharpCodeProvider();
                var parameters = new CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    TreatWarningsAsErrors = false,
                    CompilerOptions = "/optimize /nowarn:0168,0219,0414"
                };
                foreach (var r in GatherReferences(extraReferences))
                    parameters.ReferencedAssemblies.Add(r);

                results = provider.CompileAssemblyFromSource(parameters, source);
            }
            catch (Exception ex)
            {
                return new Compiled
                {
                    Failure = new EvalResult
                    {
                        Error = "Le compilateur n'a pas pu être lancé : " + ex.GetType().Name + " : " + ex.Message,
                        Hint = "Le compilateur du .NET Framework (csc.exe) est introuvable ou inaccessible."
                    }
                };
            }

            if (results.Errors.HasErrors)
            {
                var errs = results.Errors.Cast<CompilerError>()
                    .Where(e => !e.IsWarning)
                    .Select(e => string.Format(CultureInfo.InvariantCulture,
                        "ligne {0}, col {1} : {2} {3}", e.Line, e.Column, e.ErrorNumber, e.ErrorText))
                    .ToArray();
                return new Compiled
                {
                    Failure = new EvalResult
                    {
                        Error = "Échec de compilation (" + errs.Length + ")",
                        CompileErrors = errs,
                        Hint = "Rappel : C# 5. Pas de $\"...\", pas de ?., pas de nameof, pas de => sur un membre. " +
                               "Les numéros de ligne comptent à partir de la première ligne du code envoyé.",
                        SourceLineCount = source.Split('\n').Length
                    }
                };
            }

            return new Compiled { Assembly = results.CompiledAssembly };
        }

        /// <summary>
        /// Exécute __Snippet.Run sur le fil d'interface. Avec un nom de transaction, regroupe les
        /// changements en une seule entrée d'historique quand l'API expose BeginTransaction.
        /// </summary>
        private static EvalResult Run(Assembly assembly, string transactionName)
        {
            var type = assembly.GetType("__Snippet");
            if (type == null)
                return new EvalResult { Error = "Classe __Snippet introuvable dans l'assembly compilée." };
            var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (run == null)
                return new EvalResult { Error = "Méthode publique statique __Snippet.Run() introuvable." };

            object value = null;
            string error = null, stack = null;
            var transaction = transactionName == null ? null : BeginTransaction(transactionName);
            try
            {
                value = run.Invoke(null, null);
                if (transaction != null) CommitTransaction(transaction);
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                error = inner.GetType().Name + " : " + inner.Message;
                stack = inner.StackTrace;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + " : " + ex.Message;
                stack = ex.StackTrace;
            }
            finally
            {
                var disposable = transaction as IDisposable;
                if (disposable != null)
                {
                    try { disposable.Dispose(); }
                    catch (Exception ex) { Diagnostics.Warn("Fermeture de transaction : " + ex.Message); }
                }
            }

            // Le journal est relu MÊME en cas d'exception : sur une sonde, les lignes écrites
            // avant le plantage sont souvent l'information la plus utile.
            string log = null;
            try
            {
                var f = type.GetField("__log", BindingFlags.Public | BindingFlags.Static);
                if (f != null)
                {
                    var sb = f.GetValue(null) as StringBuilder;
                    if (sb != null && sb.Length > 0) log = sb.ToString().TrimEnd();
                }
            }
            catch { }

            return new EvalResult
            {
                Ok = error == null,
                Log = log,
                Result = Format(value),
                ResultType = value == null ? null : value.GetType().Name,
                Error = error,
                StackTrace = stack,
                UndoGroup = transaction != null
            };
        }

        /// <summary>
        /// Document.BeginTransaction par réflexion : la présence et le comportement exact de
        /// cette API varient selon les versions, et une transaction absente ne doit jamais
        /// empêcher une modification approuvée de s'appliquer.
        /// </summary>
        private static object BeginTransaction(string name)
        {
            try
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null) return null;
                var begin = doc.GetType().GetMethod("BeginTransaction", new[] { typeof(string) });
                return begin == null ? null : begin.Invoke(doc, new object[] { name });
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Transaction indisponible, exécution sans regroupement : " + ex.Message);
                return null;
            }
        }

        private static void CommitTransaction(object transaction)
        {
            var commit = transaction.GetType().GetMethod("Commit", Type.EmptyTypes);
            if (commit != null) commit.Invoke(transaction, null);
        }

        /// <summary>
        /// Références de compilation : le socle .NET, toutes les assemblies Autodesk/Navisworks
        /// CHARGÉES (donc exactement les versions en vigueur dans le processus), et ce plugin
        /// lui-même pour que le code envoyé puisse réutiliser les outils déjà écrits.
        /// Dédoublonnage par nom court : deux versions d'une même assembly font échouer csc.
        /// </summary>
        private static List<string> GatherReferences(string extra)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Action<Assembly> add = a =>
            {
                if (a == null || a.IsDynamic) return;
                string loc;
                try { loc = a.Location; } catch { return; }
                if (string.IsNullOrEmpty(loc)) return;
                var key = a.GetName().Name;
                if (!byName.ContainsKey(key)) byName[key] = loc;
            };

            add(typeof(object).Assembly);          // mscorlib
            add(typeof(Uri).Assembly);             // System
            add(typeof(Enumerable).Assembly);      // System.Core
            add(typeof(System.Drawing.Bitmap).Assembly);
            add(typeof(System.Xml.XmlDocument).Assembly);
            add(typeof(EvalTools).Assembly);       // ce plugin

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = a.GetName().Name ?? "";
                if (n.IndexOf("navisworks", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("System.Text.Json", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("System.Memory", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("System.Runtime.CompilerServices", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("System.Buffers", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("System.Numerics", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("System.Threading.Tasks.Extensions", StringComparison.OrdinalIgnoreCase))
                    add(a);
            }

            foreach (var e in (extra ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                if (e.IndexOf('\\') >= 0 || e.IndexOf('/') >= 0) { if (!byName.ContainsKey(e)) byName[e] = e; }
                else
                {
                    var hit = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, e, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) add(hit); else byName[e] = e + ".dll";
                }
            }

            return byName.Values.ToList();
        }

        /// <summary>Rend lisible n'importe quelle valeur de retour, y compris une séquence.</summary>
        private static string Format(object v)
        {
            if (v == null) return "(null)";
            var s = v as string;
            if (s != null) return s;
            var seq = v as IEnumerable;
            if (seq != null)
            {
                var lines = new List<string>();
                int n = 0;
                foreach (var o in seq)
                {
                    if (n++ >= 2000) { lines.Add("... (tronqué à 2000 éléments)"); break; }
                    lines.Add(o == null ? "(null)" : o.ToString());
                }
                return string.Join(Environment.NewLine, lines);
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    public class EvalResult
    {
        public bool Ok { get; set; }
        /// <summary>query ou modify.</summary>
        public string Mode { get; set; }
        /// <summary>modify : décision de l'utilisateur (null en query).</summary>
        public bool? Approved { get; set; }
        /// <summary>Appels refusés avant exécution.</summary>
        public string[] Violations { get; set; }
        /// <summary>modify : appels d'écriture détectés dans le code et montrés à l'utilisateur.</summary>
        public string[] Mutations { get; set; }
        /// <summary>modify : changements regroupés dans une transaction.</summary>
        public bool UndoGroup { get; set; }
        public long ElapsedMs { get; set; }
        public string Log { get; set; }
        public string Result { get; set; }
        public string ResultType { get; set; }
        public string[] CompileErrors { get; set; }
        public int SourceLineCount { get; set; }
        public string Hint { get; set; }
        public string StackTrace { get; set; }
        public string Error { get; set; }
    }
}
