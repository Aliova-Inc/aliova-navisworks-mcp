using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace NavisworksMcpPlugin.Chat
{
    /// <summary>
    /// Panneau dockable "Chat Claude" dans Navisworks. Heberge le ChatPaneControl
    /// qui lance claude -p a la demande. Activable depuis Add-Ins ou ouvert
    /// automatiquement au demarrage du serveur (voir ServerLifecycle.Start).
    /// </summary>
    [Plugin("NavisworksMcpPlugin.Chat.ChatDockPane", "NMCP",
        DisplayName = "Chat Claude",
        ToolTip = "Dialoguer avec Claude depuis Navisworks (claude -p)")]
    [DockPanePlugin(420, 600,
        FixedSize = false,
        AutoScroll = true,
        MinimumWidth = 320,
        MinimumHeight = 320)]
    public class ChatDockPane : DockPanePlugin
    {
        public override Control CreateControlPane()
        {
            var control = new ChatPaneControl { Dock = DockStyle.Fill };
            control.CreateControl();
            return control;
        }

        public override void DestroyControlPane(Control pane)
        {
            pane.Dispose();
        }
    }
}
