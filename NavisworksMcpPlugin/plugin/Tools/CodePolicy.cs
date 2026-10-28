using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>Mode d'execution d'un snippet C#.</summary>
    public enum RunMode
    {
        /// <summary>Lecture seule : tout appel qui peut modifier le document est refuse.</summary>
        Query,
        /// <summary>Ecriture : les modifications sont listees et soumises a l'utilisateur.</summary>
        Modify
    }

    /// <summary>
    /// Verifie le code compile AVANT de l'executer, en lisant son IL.
    ///
    /// Pourquoi l'IL et pas le texte source : un filtre sur le texte se contourne par un alias,
    /// une variable, un `using`. L'IL, lui, nomme exactement chaque methode appelee, apres
    /// resolution des surcharges et des types. On parcourt toutes les methodes de l'assembly
    /// compilee (lambdas et classes generees comprises) et, transitivement, celles de ce plugin
    /// qu'elles appellent : appeler un outil d'isolation depuis navis_query ne passe pas.
    ///
    /// Ce que la verification garantit, et ce qu'elle ne garantit pas :
    /// - Les appels directs a l'API Navisworks sont classes : lecture (accesseurs get, methodes
    ///   Get/Find/Is/Has/..., types valeur comme Point3D ou ModelItemCollection) ou ecriture
    ///   (tout le reste). En Query, une ecriture est refusee. En Modify, elle est listee pour
    ///   l'approbation.
    /// - Les portes derobees connues sont fermees en Query : invocation par reflexion,
    ///   delegues crees par reflexion, Emit, `dynamic`, appel indirect (calli), ecriture
    ///   memoire brute, ecriture de fichiers.
    /// - Ce n'est PAS un bac a sable. Un accesseur "get" de l'API qui aurait un effet de bord,
    ///   ou une methode nommee Get... qui ecrit, passerait. Le contrat couvre les erreurs d'un
    ///   agent de bonne foi, pas un code concu pour tromper la verification.
    /// </summary>
    public static class CodePolicy
    {
        public sealed class Report
        {
            /// <summary>Appels refuses : le code ne sera pas execute.</summary>
            public List<string> Violations = new List<string>();

            /// <summary>Appels qui modifient le document (Modify seulement), pour l'approbation.</summary>
            public List<string> Mutations = new List<string>();

            public bool Allowed { get { return Violations.Count == 0; } }
        }

        // ------------------------------------------------------------------ point d'entree

        public static Report Inspect(Assembly snippet, RunMode mode)
        {
            Type[] types;
            try { types = snippet.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            return InspectCore(snippet, types.SelectMany(DeclaredMethods), mode);
        }

        /// <summary>Verifie un seul type et ce qu'il appelle (tests de la politique).</summary>
        internal static Report InspectType(Type type, RunMode mode)
        {
            return InspectCore(type.Assembly, DeclaredMethods(type), mode);
        }

        private static IEnumerable<MethodBase> DeclaredMethods(Type t)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            return t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all));
        }

        private static Report InspectCore(Assembly snippet, IEnumerable<MethodBase> seeds, RunMode mode)
        {
            var report = new Report();
            var violations = new SortedSet<string>(StringComparer.Ordinal);
            var mutations = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<MethodBase>();
            var queue = new Queue<MethodBase>(seeds);

            while (queue.Count > 0)
            {
                var method = queue.Dequeue();
                if (!visited.Add(method)) continue;

                foreach (var site in ReadCallSites(method))
                {
                    if (site.Unverifiable != null)
                    {
                        if (mode == RunMode.Query) violations.Add(site.Unverifiable);
                        continue;
                    }

                    var target = site.Method;
                    if (target != null)
                    {
                        var asm = target.DeclaringType == null ? target.Module.Assembly : target.DeclaringType.Assembly;
                        // Code du snippet (lambdas, methodes auxiliaires) ou de ce plugin : on le
                        // suit, il peut lui-meme ecrire dans le document.
                        if (asm == snippet || asm == typeof(CodePolicy).Assembly)
                        {
                            queue.Enqueue(target);
                            continue;
                        }

                        Classify(target, mode, violations, mutations);
                        continue;
                    }

                    var field = site.Field;
                    if (field != null && site.IsStore && IsNavisworks(field.DeclaringType))
                    {
                        var label = "ecriture du champ " + Describe(field.DeclaringType) + "." + field.Name;
                        if (mode == RunMode.Query) violations.Add(label + " (lecture seule : utiliser navis_modify)");
                        else mutations.Add(Describe(field.DeclaringType) + "." + field.Name);
                    }
                }
            }

            report.Violations.AddRange(violations);
            report.Mutations.AddRange(mutations);
            return report;
        }

        // ------------------------------------------------------------------ classification

        private static void Classify(MethodBase m, RunMode mode, ISet<string> violations, ISet<string> mutations)
        {
            var type = m.DeclaringType;
            var name = m.Name;
            var label = Describe(type) + "." + name;

            // 1. Interdit dans TOUS les modes : sortir du document ou faire reentrer Navisworks.
            var always = AlwaysForbidden(type, name);
            if (always != null)
            {
                violations.Add(label + " : " + always);
                return;
            }

            if (IsNavisworks(type))
            {
                if (IsNavisworksRead(type, name)) return;

                if (mode == RunMode.Query)
                    violations.Add(label + " : modifie l'etat de Navisworks (lecture seule : utiliser navis_modify)");
                else
                    mutations.Add(label);
                return;
            }

            // 2. Bibliotheque .NET : seules les portes derobees sont refusees, en lecture seule.
            if (mode == RunMode.Query)
            {
                var reason = QueryForbiddenFramework(type, name);
                if (reason != null) violations.Add(label + " : " + reason);
            }
        }

        /// <summary>Le point 3 : ce qu'aucun run ne doit faire, approuve ou non.</summary>
        private static string AlwaysForbidden(Type type, string name)
        {
            var full = type == null ? "" : type.FullName ?? "";

            if (IsNavisworks(type))
            {
                if (name == "SaveFile" || name == "SaveAs" || name == "Save" ||
                    name.StartsWith("Publish", StringComparison.Ordinal))
                    return "enregistrer ou publier est bloque pendant un run, l'utilisateur garde la main sur le fichier";
                if (name == "OpenFile" || name == "AppendFile" || name == "MergeFile" || name == "NewFile" ||
                    name == "Close" || name == "CloseFile" || name == "Exit" || name == "Quit" || name == "Shutdown" ||
                    (type.Name == "Document" && name == "Clear"))
                    return "ouvrir, fermer ou vider un document est bloque pendant un run";
            }

            if (full == "System.Windows.Forms.Application" &&
                (name == "DoEvents" || name == "Exit" || name == "ExitThread" || name == "Restart"))
                return name == "DoEvents"
                    ? "pomper la boucle de messages fait reentrer Navisworks dans lui-meme (plantage du 2026-08-06)"
                    : "quitter l'application est bloque";
            if (full == "System.Windows.Threading.Dispatcher" && name == "PushFrame")
                return "pomper la boucle de messages fait reentrer Navisworks dans lui-meme";
            if (full == "System.Environment" && (name == "Exit" || name == "FailFast"))
                return "terminer le processus est bloque";
            if (full == "System.Diagnostics.Process" && (name == "Start" || name == "Kill"))
                return "lancer ou tuer un processus est bloque";

            return null;
        }

        /// <summary>Lecture seule : les portes derobees qui contourneraient la verification.</summary>
        private static string QueryForbiddenFramework(Type type, string name)
        {
            if (type == null) return null;
            var ns = type.Namespace ?? "";
            var full = type.FullName ?? "";

            // Reflexion : lire les metadonnees est libre (recettes du skill 14), INVOQUER ne l'est pas.
            if (typeof(MethodBase).IsAssignableFrom(type) && name == "Invoke")
                return "invocation par reflexion, non verifiable";
            if ((typeof(PropertyInfo).IsAssignableFrom(type) || typeof(FieldInfo).IsAssignableFrom(type)) &&
                name == "SetValue")
                return "ecriture par reflexion";
            if (full == "System.Type" && name == "InvokeMember") return "invocation par reflexion, non verifiable";
            if (full == "System.Activator") return "instanciation par reflexion, non verifiable";
            if (typeof(Delegate).IsAssignableFrom(type) && (name == "DynamicInvoke" || name == "CreateDelegate"))
                return "delegue construit par reflexion, non verifiable";
            if (typeof(MethodInfo).IsAssignableFrom(type) && name == "CreateDelegate")
                return "delegue construit par reflexion, non verifiable";
            if (ns.StartsWith("System.Reflection.Emit", StringComparison.Ordinal))
                return "generation de code a l'execution, non verifiable";
            if (ns.StartsWith("Microsoft.CSharp.RuntimeBinder", StringComparison.Ordinal))
                return "`dynamic`, non verifiable";
            if (full == "System.Runtime.InteropServices.Marshal" &&
                !(name.StartsWith("Read", StringComparison.Ordinal) || name.StartsWith("PtrTo", StringComparison.Ordinal) ||
                  name == "SizeOf" || name.StartsWith("Get", StringComparison.Ordinal)))
                return "acces memoire brut";
            if (full == "System.Diagnostics.Process" && name != "GetCurrentProcess" && !name.StartsWith("get_", StringComparison.Ordinal))
                return "pilotage de processus";

            if (ns == "System.IO") return FileWriteReason(type, name);

            return null;
        }

        private static readonly HashSet<string> InMemoryIoTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Path", "StringWriter", "StringReader", "MemoryStream", "StreamReader", "BinaryReader",
            "TextReader", "TextWriter", "Stream", "IOException", "FileNotFoundException",
            "DirectoryNotFoundException", "SearchOption", "FileAttributes", "FileMode", "FileAccess"
        };

        private static string FileWriteReason(Type type, string name)
        {
            if (InMemoryIoTypes.Contains(type.Name)) return null;
            if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("Get", StringComparison.Ordinal) ||
                name.StartsWith("Read", StringComparison.Ordinal) || name.StartsWith("Enumerate", StringComparison.Ordinal) ||
                name == "Exists" || name == "OpenRead" || name == "OpenText" ||
                name == "ToString" || name == "Equals" || name == "GetHashCode" || name == "Dispose" || name == "Close")
                return null;
            // FileInfo / DirectoryInfo en lecture : le constructeur ne touche pas au disque.
            if (name == ".ctor" && (type.Name == "FileInfo" || type.Name == "DirectoryInfo")) return null;
            return "ecriture sur disque (lecture seule)";
        }

        // ------------------------------------------------------------------ API Navisworks

        internal static bool IsNavisworks(Type type)
        {
            if (type == null) return false;
            var asm = type.Assembly.GetName().Name ?? "";
            return asm.IndexOf("navisworks", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   asm.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase) ||
                   asm.StartsWith("AdWindows", StringComparison.OrdinalIgnoreCase) ||
                   asm.StartsWith("lcod", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Types de l'API qui sont des VALEURS ou des objets construits localement : les
        /// modifier ne touche pas au document tant qu'on ne les y insere pas, et l'insertion
        /// passe toujours par une partie du document (DocumentModels, CurrentSelection...),
        /// elle, classee en ecriture. Selection : celle d'un Search local est modifiable, celle
        /// que tient le document (CurrentSelection.Value) est en lecture seule (IsReadOnly).
        /// </summary>
        private static readonly HashSet<string> LocalValueTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ModelItemCollection", "ModelItemEnumerableCollection", "Search", "SearchCondition", "Selection",
            "SearchConditionCollection", "VariantData", "NamedConstant", "Point3D", "Point2D",
            "Vector3D", "Vector2D", "UnitVector3D", "Rotation3D", "Matrix3", "Transform3D",
            "Transform3DComponents", "BoundingBox3D", "BoundingBox2D", "Color", "Units",
            "SelectionSourceCollection", "DataPropertyCollection", "PropertyCategoryCollection"
        };

        private static readonly string[] ReadPrefixes =
        {
            "get_", "op_", "Get", "Find", "Is", "Has", "Can", "Contains", "To", "Compare",
            "Count", "Equals", "Intersects", "Distance", "Try", "Lookup", "Enumerate", "Query",
            "Describe", "Format", "Parse", "Resolve", "BoundingBox"
        };

        private static bool IsNavisworksRead(Type type, string name)
        {
            if (name == ".ctor" || name == ".cctor") return true;   // allouer n'ecrit pas dans le document
            if (name == "Dispose" || name == "Finalize" || name == "GetHashCode" || name == "GetType") return true;

            if (type != null && LocalValueTypes.Contains(type.Name) &&
                !name.StartsWith("add_", StringComparison.Ordinal) && !name.StartsWith("remove_", StringComparison.Ordinal))
                return true;

            foreach (var p in ReadPrefixes)
                if (StartsWithWord(name, p)) return true;

            return false;
        }

        /// <summary>
        /// Le prefixe doit etre un MOT entier du nom en PascalCase : "IsHidden" est une lecture,
        /// "Isolate" n'en est pas une, pas plus que "Cancel" ou "Toggle".
        /// </summary>
        private static bool StartsWithWord(string name, string prefix)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (prefix.EndsWith("_", StringComparison.Ordinal) || name.Length == prefix.Length) return true;
            var next = name[prefix.Length];
            return char.IsUpper(next) || char.IsDigit(next) || next == '_';
        }

        // ------------------------------------------------------------------ lecture de l'IL

        private sealed class CallSite
        {
            public MethodBase Method;
            public FieldInfo Field;
            public bool IsStore;
            public string Unverifiable;
        }

        private static readonly OpCode[] OneByte = new OpCode[0x100];
        private static readonly OpCode[] TwoByte = new OpCode[0x100];

        static CodePolicy()
        {
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (OpCode)f.GetValue(null);
                var v = (ushort)op.Value;
                if (v < 0x100) OneByte[v] = op;
                else if ((v & 0xff00) == 0xfe00) TwoByte[v & 0xff] = op;
            }
        }

        private static IEnumerable<CallSite> ReadCallSites(MethodBase method)
        {
            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch (Exception) { yield break; }
            if (body == null) yield break;                       // abstraite, extern, P/Invoke

            var il = body.GetILAsByteArray();
            if (il == null) yield break;

            var module = method.Module;
            Type[] typeArgs = null, methodArgs = null;
            try
            {
                if (method.DeclaringType != null && method.DeclaringType.IsGenericType)
                    typeArgs = method.DeclaringType.GetGenericArguments();
                if (method.IsGenericMethod) methodArgs = method.GetGenericArguments();
            }
            catch (Exception) { }

            var i = 0;
            while (i < il.Length)
            {
                OpCode op;
                var b = il[i++];
                if (b == 0xfe)
                {
                    if (i >= il.Length) yield break;
                    op = TwoByte[il[i++]];
                }
                else
                {
                    op = OneByte[b];
                }

                if (op.Size == 0)
                {
                    yield return new CallSite { Unverifiable = "IL illisible dans " + Describe(method.DeclaringType) + "." + method.Name };
                    yield break;
                }

                switch (op.OperandType)
                {
                    case OperandType.InlineMethod:
                    {
                        var token = BitConverter.ToInt32(il, i);
                        i += 4;
                        MethodBase target = null;
                        string failure = null;
                        try { target = module.ResolveMethod(token, typeArgs, methodArgs); }
                        catch (Exception ex) { failure = ex.GetType().Name; }
                        if (target == null)
                            yield return new CallSite { Unverifiable = "appel non resolu (" + failure + ") dans " + method.Name };
                        else
                            yield return new CallSite { Method = target };
                        break;
                    }
                    case OperandType.InlineField:
                    {
                        var token = BitConverter.ToInt32(il, i);
                        i += 4;
                        var isStore = op == OpCodes.Stfld || op == OpCodes.Stsfld ||
                                      op == OpCodes.Ldflda || op == OpCodes.Ldsflda;
                        if (!isStore) break;
                        FieldInfo field = null;
                        try { field = module.ResolveField(token, typeArgs, methodArgs); }
                        catch (Exception) { }
                        if (field != null) yield return new CallSite { Field = field, IsStore = true };
                        break;
                    }
                    case OperandType.InlineSig:
                        i += 4;
                        if (op == OpCodes.Calli)
                            yield return new CallSite { Unverifiable = "appel indirect (calli) dans " + method.Name + ", non verifiable" };
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                    {
                        var n = BitConverter.ToInt32(il, i);
                        i += 4 + 4 * n;
                        break;
                    }
                    default:
                        // InlineBrTarget, InlineI, InlineString, InlineTok, InlineType, ShortInlineR
                        i += 4;
                        break;
                }
            }
        }

        private static string Describe(Type t)
        {
            if (t == null) return "<module>";
            var n = t.FullName ?? t.Name;
            var tick = n.IndexOf('`');
            return tick > 0 ? n.Substring(0, tick) : n;
        }
    }
}
