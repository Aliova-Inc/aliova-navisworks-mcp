using System;
using Autodesk.Navisworks.Api.Plugins;
using NavisworksMcpPlugin.Mcp;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace NavisworksMcpPlugin.Plugin
{
    /// <summary>
    /// Opens and closes the MCP server. Plain static class, deliberately: it declares no
    /// Navisworks plugin at all.
    ///
    /// It replaces two former plugins that both had to go:
    ///   - BridgePlugin, an AddInPlugin that put a second entry point under the Add-Ins tab.
    ///     Two ways to start the same server, one of them without any state feedback, is one
    ///     too many. The Aliova ribbon tab is now the only surface.
    ///   - AutoStartPlugin, declared AddInLocation.None with LoadForCanExecute. With no
    ///     presence in the interface, Navisworks never asked it for CanExecute, so it never
    ///     ran. It was dead code that looked like a feature.
    ///
    /// Ouvre et ferme le serveur MCP. Classe statique ordinaire, volontairement : elle ne
    /// declare aucun plugin Navisworks. Elle remplace deux plugins qui devaient disparaitre,
    /// le bouton sous Complements d'outils et un demarrage automatique qui n'etait jamais
    /// appele.
    /// </summary>
    internal static class ServerLifecycle
    {
        private static bool _exitHookRegistered;

        internal static void Start()
        {
            // The UI thread must be captured before the first tool call: every Navisworks API
            // touch has to be marshalled onto it.
            UiThread.Capture();

            if (!_exitHookRegistered)
            {
                NavisApp.GuiDestroying += OnNavisworksExiting;
                _exitHookRegistered = true;
            }

            McpHost.Start();
            TryActivateChatPane();
        }

        /// <summary>
        /// Shows the Claude chat pane when the server opens. Best effort: a pane that fails to
        /// activate must never stop the server from serving.
        /// </summary>
        private static void TryActivateChatPane()
        {
            try
            {
                var record = NavisApp.Plugins.FindPlugin("NavisworksMcpPlugin.Chat.ChatDockPane.NMCP");
                if (record == null) return;
                if (!record.IsLoaded) record.LoadPlugin();
                var pane = record.LoadedPlugin as DockPanePlugin;
                if (pane != null) pane.Visible = true;
            }
            catch (Exception ex)
            {
                Diagnostics.Warn("Chat pane activation failed: " + ex.Message);
            }
        }

        private static void OnNavisworksExiting(object sender, EventArgs e)
        {
            McpHost.StopAsync().GetAwaiter().GetResult();
        }
    }
}
