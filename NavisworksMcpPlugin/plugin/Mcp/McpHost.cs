using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Lightweight MCP server over Streamable HTTP using HttpListener.
    /// Handles JSON-RPC 2.0 messages: initialize, tools/list, tools/call.
    /// </summary>
    public static class McpHost
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static ToolRegistry _registry;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static bool IsRunning => _listener?.IsListening == true;

        /// <summary>
        /// Consignes renvoyees a l'initialisation : le client MCP les donne a l'agent avant
        /// tout appel, quel que soit le client (pas seulement Claude Code dans ce depot).
        /// </summary>
        private const string ServerInstructions =
            "Navisworks MCP. Avant toute tache : get_skill sans argument pour lister les skills, " +
            "puis get_skill('00') pour les pieges du modele (explorer avant d'agir, performance, " +
            "noms FR/EN). Preferer les outils dedies. Pour ce qu'aucun outil ne couvre : " +
            "lookup_navis_api pour lire les signatures, navis_query pour lire (lecture seule " +
            "verifiee avant execution), navis_modify pour ecrire (soumis a l'approbation de " +
            "l'utilisateur dans Navisworks). Ne jamais lancer deux isolations en parallele. " +
            "list_runs / get_run relisent l'historique des appels.";

        public static void Start()
        {
            if (IsRunning) return;

            // Le jeton est cree des l'ouverture, pas a la premiere requete : le fichier doit
            // exister pour que l'utilisateur puisse configurer son client (bouton A propos).
            _ = AccessGuard.Token;

            _registry = new ToolRegistry();
            _registry.DiscoverTools(typeof(McpHost).Assembly);

            // Le port est resolu AVANT d'ouvrir le listener : voir McpEndpoint, qui sonde la
            // disponibilite et se rabat proprement plutot que de laisser HttpListener echouer
            // sur un message Win32 muet.
            var port = McpEndpoint.ResolveFreePort();

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(McpEndpoint.BaseUrl(port));

            _listener.Start();
            McpEndpoint.MarkActive(port);
            Trace.TraceInformation("[NavisworksMCP] Server listening on " + McpEndpoint.BaseUrl(port));

            Task.Run(() => ListenLoop(_cts.Token));
        }

        public static Task StopAsync()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[NavisworksMCP] Shutdown error: {ex.Message}");
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                McpEndpoint.MarkStopped();
            }
            return Task.CompletedTask;
        }

        private static async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }

                _ = Task.Run(() => HandleRequest(ctx));
            }
        }

        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            // AUCUN en-tete CORS, volontairement : voir AccessGuard. Un client MCP natif
            // (Claude Code, Claude Desktop, Cursor...) n'est pas un navigateur et n'en a pas
            // besoin ; une page web, elle, ne doit jamais pouvoir appeler ce serveur.
            try
            {
                // La racine "/" ne sert qu'a savoir si le serveur tourne : pas de jeton exige,
                // elle ne revele rien et n'execute rien. Tout le reste exige le jeton.
                var isHealthProbe = req.HttpMethod == "GET" && req.Url.AbsolutePath == "/";
                var rejection = AccessGuard.Check(req, McpEndpoint.ActivePort, requireToken: !isHealthProbe);
                if (rejection != null)
                {
                    Diagnostics.Warn("Requete refusee (" + rejection.StatusCode + ") : " + rejection.Message);
                    await WriteJsonResponse(resp, MakeError(null, -32001, rejection.Message), rejection.StatusCode)
                        .ConfigureAwait(false);
                    return;
                }

                if (isHealthProbe)
                {
                    await WriteJsonResponse(resp, new
                    {
                        name = "navisworks-mcp",
                        status = IsRunning ? "running" : "stopped",
                        endpoint = "/mcp"
                    }).ConfigureAwait(false);
                    return;
                }

                // GET on /mcp - SSE session endpoint for clients that still probe it.
                if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/mcp")
                {
                    resp.ContentType = "text/event-stream";
                    resp.Headers.Set("Cache-Control", "no-cache");
                    resp.StatusCode = 200;

                    var writer = new StreamWriter(resp.OutputStream, Encoding.UTF8);
                    await writer.WriteAsync("event: endpoint\n").ConfigureAwait(false);
                    await writer.WriteAsync("data: /mcp\n\n").ConfigureAwait(false);
                    await writer.WriteAsync(": heartbeat\n\n").ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);

                    try
                    {
                        await Task.Delay(Timeout.Infinite, _cts.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { }
                    finally
                    {
                        resp.Close();
                    }
                    return;
                }

                if (req.HttpMethod != "POST")
                {
                    resp.StatusCode = 405;
                    resp.Close();
                    return;
                }

                string body;
                // JSON-RPC bodies are UTF-8 by specification. Do NOT use req.ContentEncoding:
                // HttpListener falls back to Encoding.Default (ANSI, cp1252 here) when the
                // Content-Type carries no charset, which MCP clients do not send. UTF-8 bytes
                // would then be re-read as ANSI and accented arguments get mangled (é -> Ã©).
                using (var reader = new StreamReader(req.InputStream, new UTF8Encoding(false)))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Trace.TraceInformation($"[NavisworksMCP] << {body}");

                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Check if it's a batch (array) or single request
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var responses = new List<object>();
                    foreach (var element in root.EnumerateArray())
                    {
                        var result = await ProcessMessage(element).ConfigureAwait(false);
                        if (result != null) responses.Add(result);
                    }
                    await WriteJsonResponse(resp, responses).ConfigureAwait(false);
                }
                else
                {
                    var result = await ProcessMessage(root).ConfigureAwait(false);
                    if (result != null)
                        await WriteJsonResponse(resp, result).ConfigureAwait(false);
                    else
                    {
                        resp.StatusCode = 204;
                        resp.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[NavisworksMCP] Request error: {ex}");
                try
                {
                    var error = MakeError(null, -32603, ex.Message);
                    await WriteJsonResponse(resp, error).ConfigureAwait(false);
                }
                catch { resp.Close(); }
            }
        }

        private static async Task<object> ProcessMessage(JsonElement msg)
        {
            var method = msg.TryGetProperty("method", out var m) ? m.GetString() : null;
            var id = TryGetJsonRpcId(msg, out var idEl) ? (object)idEl : null;

            // Notifications (no id) — acknowledge silently
            if (id == null) return null;

            switch (method)
            {
                case "initialize":
                    return MakeResult(id, new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new
                        {
                            tools = new { listChanged = false }
                        },
                        serverInfo = new
                        {
                            name = "navisworks-mcp",
                            version = "1.1.0"
                        },
                        instructions = ServerInstructions
                    });

                case "tools/list":
                    return MakeResult(id, new { tools = _registry.ListTools() });

                case "tools/call":
                    return await HandleToolCall(msg, id).ConfigureAwait(false);

                case "ping":
                    return MakeResult(id, new { });

                default:
                    return MakeError(id, -32601, $"Method not found: {method}");
            }
        }

        private static async Task<object> HandleToolCall(JsonElement msg, object id)
        {
            string toolName = null;
            string runId = null;
            var sw = Stopwatch.StartNew();
            try
            {
                var paramsEl = msg.GetProperty("params");
                toolName = paramsEl.GetProperty("name").GetString();
                JsonElement? arguments = paramsEl.TryGetProperty("arguments", out var args) ? args : (JsonElement?)null;

                // Ecrit AVANT l'execution : si Navisworks tombe pendant l'appel, le journal
                // garde le code qui l'a fait tomber.
                runId = RunJournal.Begin(toolName, arguments.HasValue ? arguments.Value.GetRawText() : "{}");

                var result = await _registry.InvokeTool(toolName, arguments).ConfigureAwait(false);
                var resultJson = JsonSerializer.Serialize(result, JsonOpts);

                RunJournal.End(runId, toolName, ReportsSuccess(result), sw.ElapsedMilliseconds, resultJson, null);

                return MakeResult(id, new
                {
                    content = new[]
                    {
                        new { type = "text", text = resultJson }
                    }
                });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                RunJournal.End(runId, toolName, false, sw.ElapsedMilliseconds, null, inner.GetType().Name + " : " + inner.Message);
                return MakeResult(id, new
                {
                    content = new[]
                    {
                        new { type = "text", text = $"Error: {inner.Message}" }
                    },
                    isError = true
                });
            }
        }

        /// <summary>
        /// Un outil qui renvoie un objet portant "Ok = false" (navis_query, navis_modify) a
        /// echoue meme sans exception : le journal doit le dire.
        /// </summary>
        private static bool ReportsSuccess(object result)
        {
            if (result == null) return true;
            var ok = result.GetType().GetProperty("Ok");
            if (ok == null || ok.PropertyType != typeof(bool)) return true;
            return (bool)ok.GetValue(result);
        }

        private static object MakeResult(object id, object result) =>
            new { jsonrpc = "2.0", id, result };

        private static object MakeError(object id, int code, string message) =>
            new { jsonrpc = "2.0", id, error = new { code, message } };

        private static bool TryGetJsonRpcId(JsonElement msg, out JsonElement id)
        {
            if (msg.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
            {
                id = idEl.Clone();
                return true;
            }

            id = default;
            return false;
        }

        private static async Task WriteJsonResponse(HttpListenerResponse resp, object data, int statusCode = 200)
        {
            resp.ContentType = "application/json";
            resp.StatusCode = statusCode;
            var json = JsonSerializer.Serialize(data, JsonOpts);
            Trace.TraceInformation($"[NavisworksMCP] >> {json}");
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            resp.Close();
        }
    }
}
