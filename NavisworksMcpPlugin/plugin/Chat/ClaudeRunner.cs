using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Chat
{
    /// <summary>
    /// Raccordage Claude "headless" : lance le CLI `claude -p` en sous-processus et
    /// retourne la reponse texte (localisation du CLI, env d'abonnement, appel).
    ///
    /// - Utilise les credentials de l'ABONNEMENT (env scrubbe de toute cle API).
    /// - Expose le serveur MCP Navis (ce plugin, adresse en service et jeton d'acces) au
    ///   claude headless via --mcp-config, pour qu'il puisse piloter la maquette puis repondre.
    /// - A appeler HORS thread UI (le subprocess peut durer 30-180s).
    /// </summary>
    public static class ClaudeRunner
    {
        // Modele a contexte standard : evite les variantes 1M qui exigent "extra usage".
        private const string Model = "sonnet";
        private const int DefaultTimeoutMs = 180_000;            // 3 min

        public class Result
        {
            public bool Ok;
            public string Text;
            public string Error;
        }

        /// <summary>Localise le CLI claude. Prefere le .exe natif au wrapper .cmd sous Windows.</summary>
        public static string FindClaude()
        {
            foreach (var name in new[] { "claude.exe", "claude", "claude.cmd" })
            {
                var path = WhichOnPath(name);
                if (path != null) return path;
            }
            return null;
        }

        private static string WhichOnPath(string exe)
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var full = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(full)) return full;
                }
                catch { /* chemin PATH invalide, on ignore */ }
            }
            return null;
        }

        /// <summary>Lance `claude -p` avec le prompt et retourne la reponse texte.</summary>
        public static async Task<Result> AskAsync(string prompt, int timeoutMs = DefaultTimeoutMs)
        {
            var claude = FindClaude();
            if (claude == null)
                return new Result { Ok = false, Error = "Le CLI 'claude' est introuvable sur le PATH." };

            string mcpConfigPath = null;
            try
            {
                // --mcp-config accepte un fichier : evite l'enfer du quoting JSON en ligne de commande.
                mcpConfigPath = Path.Combine(Path.GetTempPath(), "navis_claude_mcp.json");
                // L'adresse EN SERVICE, pas une constante : le serveur a pu se rabattre sur un
                // autre port (McpEndpoint). Le jeton est exige par AccessGuard ; le fichier vit
                // dans le dossier temporaire de l'utilisateur et est supprime en fin d'appel.
                File.WriteAllText(mcpConfigPath,
                    "{\"mcpServers\":{\"navis\":{\"type\":\"http\",\"url\":\"" + McpEndpoint.CurrentMcpUrl +
                    "\",\"headers\":{\"Authorization\":\"" + AccessGuard.AuthorizationHeaderValue + "\"}}}}",
                    new UTF8Encoding(false));

                var psi = new ProcessStartInfo
                {
                    FileName = claude,
                    Arguments =
                        "-p --model " + Model +
                        " --output-format text" +
                        " --mcp-config \"" + mcpConfigPath + "\"" +
                        // Seuls les outils du serveur 'navis' sont pre-autorises. En -p aucune demande
                        // de permission n'est possible : tout outil qui en exige une (shell, ecriture,
                        // reseau) est refuse. Le claude headless lit du contenu de maquette, qui peut
                        // porter des instructions.
                        " --allowedTools mcp__navis",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                ScrubApiKeys(psi);

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();

                    // Lecture asynchrone pour eviter tout interblocage de buffer.
                    var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                    var stderrTask = proc.StandardError.ReadToEndAsync();

                    // Prompt via stdin, puis fermeture.
                    await proc.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
                    proc.StandardInput.Close();

                    var exited = await Task.Run(() => proc.WaitForExit(timeoutMs)).ConfigureAwait(false);
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        return new Result { Ok = false,
                            Error = $"Claude n'a pas repondu en {timeoutMs / 1000}s." };
                    }

                    string stdout = await stdoutTask.ConfigureAwait(false);
                    string stderr = await stderrTask.ConfigureAwait(false);

                    if (proc.ExitCode != 0)
                    {
                        var diag = (stderr ?? "").Trim();
                        if (diag.Length > 600) diag = diag.Substring(0, 600);
                        return new Result { Ok = false,
                            Error = $"claude -p exit {proc.ExitCode}: {diag}" };
                    }

                    var text = (stdout ?? "").Trim();
                    if (text.Length == 0)
                        return new Result { Ok = false, Error = "Claude a renvoye une sortie vide." };

                    return new Result { Ok = true, Text = text };
                }
            }
            catch (Exception ex)
            {
                return new Result { Ok = false, Error = "subprocess: " + ex.Message };
            }
            finally
            {
                try { if (mcpConfigPath != null && File.Exists(mcpConfigPath)) File.Delete(mcpConfigPath); }
                catch { /* nettoyage best-effort */ }
            }
        }

        /// <summary>Retire toute cle API de l'env -> claude -p retombe sur l'abonnement Max/Pro.</summary>
        private static void ScrubApiKeys(ProcessStartInfo psi)
        {
            foreach (var key in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN",
                                        "ANTHROPIC_BASE_URL", "CLAUDE_API_KEY" })
            {
                if (psi.EnvironmentVariables.ContainsKey(key))
                    psi.EnvironmentVariables.Remove(key);
            }
        }
    }
}
