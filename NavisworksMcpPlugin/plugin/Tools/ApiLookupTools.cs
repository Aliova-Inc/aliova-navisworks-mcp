using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Signatures reelles de l'API Navisworks, lues par reflexion dans le processus en cours.
    /// Equivalent de `lookup_revit_api` dans pyRevit 7.
    ///
    /// Pourquoi : l'API Navisworks est mal documentee, et l'essentiel de l'exploration se
    /// faisait en ecrivant a la main des sondes de reflexion dans navis_eval (recettes 1 et 2
    /// du skill 14). Cet outil fait la meme chose sans compiler, sans toucher au fil
    /// d'interface, et en evitant d'office le piege des homonymes vides : NEUF assemblies
    /// declarent des structures vides portant le nom des vraies classes. Les candidats sont
    /// donc toujours rendus avec leur assembly et leur nombre de membres, le plus fourni en tete.
    /// </summary>
    [McpServerToolType]
    public static class ApiLookupTools
    {
        private const BindingFlags PublicMembers =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        [McpServerTool(ReadOnly = true), Description(
            "Lit les signatures réelles de l'API Navisworks chargée (réflexion, rien n'est exécuté). " +
            "`query` = nom de type court ou complet ('ModelItem', 'Autodesk.Navisworks.Api.DocumentModels'), " +
            "'Type.Membre' pour filtrer les membres ('DocumentModels.Override'), ou un fragment pour chercher " +
            "les types ('Clash'). Un type trouvé exactement est détaillé : constructeurs, propriétés (get/set), " +
            "méthodes, événements, valeurs d'énumération, type de base. Sinon la liste des types qui " +
            "contiennent le fragment, avec assembly et nombre de membres : les homonymes vides " +
            "(0 membre) sont des étiquettes natives à ignorer. includeNonPublic=true ajoute les membres internes.")]
        public static object lookup_navis_api(
            [Description("Type, Type.Membre ou fragment de nom")] string query,
            [Description("Nombre maximum de types listés en recherche")] int maxResults = 30,
            [Description("Inclure les membres non publics (exploration de l'interop)")] bool includeNonPublic = false)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new { error = "query est obligatoire." };

            query = query.Trim();
            var types = LoadedApiTypes(includeNonPublic);
            var flags = includeNonPublic ? PublicMembers | BindingFlags.NonPublic : PublicMembers;

            // 1. Type exact, puis Type.Membre.
            string memberFilter = null;
            var exact = FindExact(types, query);
            if (exact.Count == 0)
            {
                var dot = query.LastIndexOf('.');
                if (dot > 0 && dot < query.Length - 1)
                {
                    exact = FindExact(types, query.Substring(0, dot));
                    if (exact.Count > 0) memberFilter = query.Substring(dot + 1);
                }
            }

            if (exact.Count > 0)
            {
                // Les homonymes vides sont signales mais pas detailles.
                var ranked = exact.OrderByDescending(t => MemberCount(t, flags)).ToList();
                var real = ranked.Where(t => MemberCount(t, flags) > 0).Take(3).ToList();
                return new
                {
                    query,
                    memberFilter,
                    types = (real.Count > 0 ? real : ranked.Take(1)).Select(t => Detail(t, flags, memberFilter)).ToList(),
                    emptyHomonyms = ranked.Where(t => MemberCount(t, flags) == 0)
                        .Select(t => t.FullName + " [" + t.Assembly.GetName().Name + "]").ToList()
                };
            }

            // 2. Recherche par fragment.
            var hits = types
                .Where(t => (t.FullName ?? t.Name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => new { type = t, members = MemberCount(t, flags) })
                .OrderByDescending(x => x.members > 0)
                .ThenBy(x => x.type.Name.Length)
                .ThenBy(x => x.type.FullName, StringComparer.Ordinal)
                .ToList();

            return new
            {
                query,
                total = hits.Count,
                matches = hits.Take(Math.Max(1, maxResults)).Select(x => new
                {
                    fullName = x.type.FullName,
                    assembly = x.type.Assembly.GetName().Name,
                    kind = Kind(x.type),
                    members = x.members
                }).ToList(),
                hint = hits.Count == 0
                    ? "Aucun type. Vérifier l'orthographe, ou la fonctionnalité est dans une assembly satellite pas encore chargée (ouvrir l'onglet correspondant dans Navisworks)."
                    : "Rappeler avec le nom complet d'un type pour en lire les membres."
            };
        }

        private static List<Type> LoadedApiTypes(bool includeNonPublic)
        {
            var result = new List<Type>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                var n = a.GetName().Name ?? "";
                if (n.IndexOf("navisworks", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !n.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase)) continue;

                Type[] ts;
                try { ts = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { ts = ex.Types.Where(t => t != null).ToArray(); }
                catch (Exception) { continue; }

                foreach (var t in ts)
                {
                    if (t.IsNested && !t.IsNestedPublic && !includeNonPublic) continue;
                    if (!t.IsNested && !t.IsPublic && !includeNonPublic) continue;
                    if (t.Name.StartsWith("<", StringComparison.Ordinal)) continue;
                    result.Add(t);
                }
            }
            return result;
        }

        private static List<Type> FindExact(List<Type> types, string name)
        {
            return types.Where(t =>
                string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StripArity(t.Name), name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static int MemberCount(Type t, BindingFlags flags)
        {
            try { return t.GetMembers(flags).Length; }
            catch (Exception) { return 0; }
        }

        private static object Detail(Type t, BindingFlags flags, string memberFilter)
        {
            Func<string, bool> keep = name => memberFilter == null ||
                name.IndexOf(memberFilter, StringComparison.OrdinalIgnoreCase) >= 0;

            object enumValues = null;
            if (t.IsEnum)
            {
                enumValues = Enum.GetNames(t).Where(keep)
                    .Select(n => n + " = " + Convert.ToInt64(Enum.Parse(t, n))).ToList();
            }

            return new
            {
                fullName = t.FullName,
                assembly = t.Assembly.GetName().Name,
                kind = Kind(t),
                baseType = t.BaseType == null || t.BaseType == typeof(object) ? null : TypeName(t.BaseType),
                interfaces = t.GetInterfaces().Select(TypeName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                enumValues,
                constructors = memberFilter != null ? null : t.GetConstructors(flags)
                    .Select(c => Visibility(c) + "ctor(" + Parameters(c) + ")").ToList(),
                properties = t.GetProperties(flags).Where(p => keep(p.Name))
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => Static(p.GetGetMethod(true) ?? p.GetSetMethod(true)) + TypeName(p.PropertyType) + " " + p.Name +
                                 Indexer(p) + " { " + (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}")
                    .ToList(),
                methods = t.GetMethods(flags).Where(m => !m.IsSpecialName && keep(m.Name))
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .Select(m => Visibility(m) + Static(m) + TypeName(m.ReturnType) + " " + m.Name +
                                 GenericArgs(m) + "(" + Parameters(m) + ")")
                    .ToList(),
                events = t.GetEvents(flags).Where(e => keep(e.Name))
                    .Select(e => TypeName(e.EventHandlerType) + " " + e.Name).ToList()
            };
        }

        private static string Kind(Type t)
        {
            if (t.IsEnum) return "enum";
            if (t.IsInterface) return "interface";
            if (t.IsValueType) return "struct";
            if (t.IsAbstract && t.IsSealed) return "static class";
            return t.IsAbstract ? "abstract class" : "class";
        }

        private static string Visibility(MethodBase m)
        {
            if (m.IsPublic) return "";
            if (m.IsFamily) return "protected ";
            if (m.IsAssembly) return "internal ";
            return "private ";
        }

        private static string Static(MethodBase m)
        {
            return m != null && m.IsStatic ? "static " : "";
        }

        private static string Indexer(PropertyInfo p)
        {
            var idx = p.GetIndexParameters();
            return idx.Length == 0 ? "" : "[" + string.Join(", ", idx.Select(i => TypeName(i.ParameterType) + " " + i.Name)) + "]";
        }

        private static string GenericArgs(MethodInfo m)
        {
            return m.IsGenericMethodDefinition
                ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">"
                : "";
        }

        private static string Parameters(MethodBase m)
        {
            return string.Join(", ", m.GetParameters().Select(p =>
                (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") +
                TypeName(p.ParameterType) + " " + p.Name +
                (p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : "")));
        }

        /// <summary>Nom lisible : generiques developpes, espaces de noms System omis.</summary>
        internal static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (t.IsByRef) return TypeName(t.GetElementType());
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (t.IsGenericParameter) return t.Name;
            if (t.IsGenericType)
            {
                return StripArity(t.Name) + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
            }
            if (t.Namespace == "System" || t.Namespace == "System.Collections.Generic") return t.Name;
            return t.FullName ?? t.Name;
        }

        private static string StripArity(string name)
        {
            var tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }
    }
}
