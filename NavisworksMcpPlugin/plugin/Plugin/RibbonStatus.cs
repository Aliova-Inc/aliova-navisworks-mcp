using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Windows;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Plugin
{
    /// <summary>
    /// Ecrit l'etat du canal MCP directement sur le bouton du ruban.
    ///
    /// Pourquoi deux mecanismes. CommandState.OverrideDisplayName, renvoye par
    /// CanExecuteCommand, est la voie documentee et ne coute rien - mais rien ne garantit que
    /// Navisworks l'applique aux boutons de ruban plutot qu'aux seules entrees de menu. Cette
    /// classe est la seconde voie : elle atteint l'element de ruban par l'API AdWindows et
    /// ecrit son texte et son infobulle. Les deux sont posees, et le journal dit laquelle a
    /// pris ; on n'a pas besoin de deviner.
    ///
    /// A n'appeler que depuis le fil d'interface. CanExecuteCommand y tourne deja.
    /// </summary>
    internal static class RibbonStatus
    {
        private static bool _idsLogged;
        private static bool _unreachableLogged;

        /// <summary>Met a jour le bouton du serveur : texte, infobulle, disponibilite.</summary>
        internal static void Refresh(string text, string tooltip)
        {
            try
            {
                var item = FindItem(AliovaRibbonPlugin.CommandServer);
                if (item == null)
                {
                    if (!_unreachableLogged)
                    {
                        _unreachableLogged = true;
                        Diagnostics.Warn(
                            "Bouton de ruban " + AliovaRibbonPlugin.CommandServer + " introuvable par l'API AdWindows. " +
                            "L'etat repose alors sur CommandState.OverrideDisplayName seul.");
                        LogAllIds();
                    }
                    return;
                }

                // Le texte du ruban attend un saut de ligne reel, pas la sequence XAML.
                var wanted = text.Replace("\\n", "\n");
                if (!string.Equals(item.Text, wanted, StringComparison.Ordinal))
                {
                    item.Text = wanted;
                }

                if (tooltip != null && !(item.ToolTip is string existing && existing == tooltip))
                {
                    item.ToolTip = tooltip;
                }
            }
            catch (Exception ex)
            {
                // Peindre le ruban ne doit jamais lever : au pire l'etat reste celui du XAML.
                Diagnostics.Warn("Mise a jour du bouton de ruban impossible : " + ex.Message);
            }
        }

        private static RibbonItem FindItem(string id)
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return null;

            foreach (var item in AllItems(ribbon))
            {
                if (item.Id == null) continue;
                // Egalite d'abord ; EndsWith ensuite, au cas ou Navisworks prefixe les
                // identifiants declares par un plugin.
                if (item.Id == id) return item;
            }

            foreach (var item in AllItems(ribbon))
            {
                if (item.Id != null && item.Id.EndsWith(id, StringComparison.Ordinal)) return item;
            }

            return null;
        }

        private static IEnumerable<RibbonItem> AllItems(RibbonControl ribbon)
        {
            foreach (var tab in ribbon.Tabs)
            {
                if (tab == null) continue;
                foreach (var panel in tab.Panels)
                {
                    var source = panel == null ? null : panel.Source;
                    if (source == null) continue;
                    foreach (var item in Flatten(source.Items))
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// Descend dans les boutons composites : un bouton scinde ou un menu porte ses
        /// enfants dans sa propre collection, et l'element cherche peut s'y trouver.
        /// </summary>
        private static IEnumerable<RibbonItem> Flatten(IEnumerable<RibbonItem> items)
        {
            if (items == null) yield break;

            foreach (var item in items)
            {
                if (item == null) continue;
                yield return item;

                var list = item as RibbonListButton;
                if (list == null) continue;
                foreach (var child in Flatten(list.Items))
                {
                    yield return child;
                }
            }
        }

        /// <summary>
        /// Journalise les identifiants du seul onglet Aliova. Sert a diagnostiquer un
        /// identifiant qui ne correspond pas, sans deverser tout le ruban de Navisworks.
        /// </summary>
        private static void LogAllIds()
        {
            if (_idsLogged) return;
            _idsLogged = true;

            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null)
                {
                    Diagnostics.Warn("ComponentManager.Ribbon est nul : le ruban n'est pas encore construit.");
                    return;
                }

                foreach (var tab in ribbon.Tabs)
                {
                    if (tab == null || tab.Id == null) continue;
                    if (!tab.Id.Contains("ALIOVA")) continue;

                    var ids = tab.Panels
                        .Where(p => p != null && p.Source != null)
                        .SelectMany(p => Flatten(p.Source.Items))
                        .Select(i => i.Id ?? "(sans id)")
                        .ToList();

                    Diagnostics.Info("Onglet " + tab.Id + " : elements = " + string.Join(", ", ids));
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Inventaire des identifiants du ruban impossible : " + ex.Message);
            }
        }
    }
}
