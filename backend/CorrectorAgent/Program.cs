//using System;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using System.Text.Json;
//using Microsoft.AspNetCore.Builder;
//using Microsoft.AspNetCore.Http;

//var builder = WebApplication.CreateBuilder(args);
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy(name: "AllowLocalUI",
//        policy =>
//        {
//            policy.WithOrigins("http://localhost:5173")   // UI origin - change if different
//                  .AllowAnyMethod()
//                  .AllowAnyHeader()
//                  .WithExposedHeaders("Content-Disposition"); // optional if sending files
//            // Allow credentials only if needed (cookies); for bearer tokens we don't need it
//            // .AllowCredentials();
//        });
//});
////builder.Services.AddCors(o => o.AddPolicy("AllowAll", p =>
////{
////    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
////}));
//var app = builder.Build();
//app.UseCors("AllowLocalUI");
////app.UseCors("AllowAll");

//var agentCard = JsonSerializer.Serialize(new {
//  a2a_version = "2025-06-18",
//  id = "agent://localhost:5091/corrector",
//  name = "LocalCorrectorAgent",
//  description = "Corrects text using an MCP text-corrector tool.",
//  capabilities = new[] { new { name = "correct_text", intents = new[] { "text.correct" }, input_schema = new { type = "object", properties = new { text = new { type = "string" } }, required = new[] { "text" } } } },
//  invoke = new { url = "http://localhost:5091/invoke", method = "POST", auth = new { type = "bearer", scopes = new[]{ "a2a.invoke" } } },
//  tools = new[]{ new { type = "mcp", name = "text_corrector" } }
//});

//app.MapGet("/agent-card", () => Results.Content(agentCard, "application/json"));

//// Start a persistent MCP process on startup and keep stdio open
//Process? mcpProcess = null;
//StreamWriter? mcpStdin = null;
//StreamReader? mcpStdout = null;

//void StartMcp()
//{
//    if (mcpProcess != null && !mcpProcess.HasExited) return;

//    var baseDir = AppContext.BaseDirectory;

//    string[] candidatePaths = new[] {
//        Path.Combine(baseDir, "..", "..", "..", "backend", "tools", "mcp-text-corrector.js"),
//        Path.Combine(baseDir, "..", "..", "backend", "tools", "mcp-text-corrector.js"),
//        Path.Combine(baseDir, "..", "backend", "tools", "mcp-text-corrector.js"),
//        Path.Combine(baseDir, "tools", "mcp-text-corrector.js"),
//        Path.Combine(Directory.GetCurrentDirectory(), "backend", "tools", "mcp-text-corrector.js"),
//        Path.Combine(Directory.GetCurrentDirectory(), "tools", "mcp-text-corrector.js")
//    };

//    //string script = candidatePaths.Select(p => Path.GetFullPath(p)).FirstOrDefault(p => File.Exists(p));
//    var script = @"C:\Users\srivi\Downloads\A2A-AgentA2A-Full\backend\tools\mcp-text-corrector.bak.js";
//    if (script == null)
//    {
//        Console.Error.WriteLine("mcp-text-corrector.js not found. Tried:\n" + string.Join("\n", candidatePaths));
//        throw new FileNotFoundException("mcp-text-corrector.js not found. Ensure backend/tools/mcp-text-corrector.js exists.");
//    }

//    Console.Error.WriteLine($"Starting MCP script at: {script}");

//    var psi = new ProcessStartInfo
//    {
//        FileName = "node",
//        Arguments = $"\"{script}\"",
//        RedirectStandardInput = true,
//        RedirectStandardOutput = true,
//        RedirectStandardError = true,
//        UseShellExecute = false,
//        CreateNoWindow = true
//    };

//    mcpProcess = Process.Start(psi);
//    if (mcpProcess == null) throw new Exception("Failed to start MCP process");
//    mcpStdin = mcpProcess.StandardInput;
//    mcpStdout = mcpProcess.StandardOutput;

//    _ = Task.Run(async () =>
//    {
//        var r = mcpProcess.StandardError;
//        while (!r.EndOfStream)
//        {
//            var l = await r.ReadLineAsync();
//            if (!string.IsNullOrEmpty(l)) Console.Error.WriteLine($"[mcp] {l}");
//        }
//    });
//}

//app.MapPost("/invoke", async (HttpRequest req) =>
//{
//    if (!req.Headers.TryGetValue("Authorization", out var auth) || !auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
//        return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

//    using var doc = await JsonDocument.ParseAsync(req.Body);
//    var root = doc.RootElement;
//    var text = root.GetProperty("input").GetProperty("text").GetString() ?? "";

//    try
//    {
//        StartMcp();
//    }
//    catch (Exception ex)
//    {
//        Console.Error.WriteLine("Failed to start MCP: " + ex);
//        return Results.Json(new { error = "MCP start failed", details = ex.Message }, statusCode: 502);
//    }

//    var id = Guid.NewGuid().ToString();
//    var msg = new { type = "invoke", id = id, tool = "text_correct", args = new { text } };
//    var json = System.Text.Json.JsonSerializer.Serialize(msg);

//    try
//    {
//        await mcpStdin.WriteLineAsync(json);
//        await mcpStdin.FlushAsync();
//    }
//    catch (Exception ex)
//    {
//        Console.Error.WriteLine("Failed writing to MCP stdin: " + ex);
//        return Results.Json(new { error = "MCP write failed", details = ex.Message }, statusCode: 502);
//    }

//    string? line = null;
//    try
//    {
//        line = await mcpStdout.ReadLineAsync();
//    }
//    catch (Exception ex)
//    {
//        Console.Error.WriteLine("Failed reading MCP stdout: " + ex);
//        return Results.Json(new { error = "MCP read failed", details = ex.Message }, statusCode: 502);
//    }

//    if (string.IsNullOrWhiteSpace(line))
//    {
//        Console.Error.WriteLine("Empty response from MCP");
//        return Results.Json(new { error = "MCP no response" }, statusCode: 502);
//    }

//    try
//    {
//        using var respDoc = JsonDocument.Parse(line);
//        var corrected = respDoc.RootElement.GetProperty("result").GetProperty("corrected").GetString() ?? text;
//        return Results.Json(new { capability = "correct_text", output = new { corrected } }, statusCode: 200);
//    }
//    catch (Exception ex)
//    {
//        Console.Error.WriteLine("Failed parsing MCP response: " + ex);
//        return Results.Json(new { error = "Invalid MCP response", details = ex.Message, raw = line }, statusCode: 502);
//    }
//});

//app.Lifetime.ApplicationStopped.Register(() =>
//{
//    try { if (mcpProcess != null && !mcpProcess.HasExited) mcpProcess.Kill(); } catch { }
//});

//app.Run("http://localhost:5091");

// backend/CorrectorAgent/Program.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// CORS (allow local UI origin - adjust if needed)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalUI", p =>
    {
        p.WithOrigins("http://localhost:5173")
         .AllowAnyMethod()
         .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("AllowLocalUI");

// Agent card metadata
var agentCard = JsonSerializer.Serialize(new
{
    a2a_version = "2025-06-18",
    id = "agent://localhost:5091/corrector",
    name = "LocalCorrectorAgent",
    description = "Corrects text using an MCP text-corrector tool.",
    capabilities = new[] {
        new {
            name = "correct_text",
            intents = new[] { "text.correct" },
            input_schema = new { type = "object", properties = new { text = new { type = "string" } }, required = new[] { "text" } }
        }
    },
    invoke = new { url = "http://localhost:5091/invoke", method = "POST", auth = new { type = "bearer", scopes = new[] { "a2a.invoke" } } },
    tools = new[] { new { type = "mcp", name = "text_corrector" } }
});

app.MapGet("/agent-card", () => Results.Content(agentCard, "application/json"));

// --- MCP process & communication state ---
Process? mcpProcess = null;
StreamWriter? mcpStdin = null;
StreamReader? mcpStdout = null;

// Map of pending responses from MCP, keyed by id
var mcpResponseMap = new ConcurrentDictionary<string, TaskCompletionSource<string>>(StringComparer.Ordinal);

// Lock for start/stop races
object mcpLock = new object();

// Start Node process helper
//void StartNodeProcess(string scriptFullPath)
//{
//    var psi = new ProcessStartInfo
//    {
//        FileName = "node",
//        Arguments = $"\"{scriptFullPath}\"",
//        RedirectStandardInput = true,
//        RedirectStandardOutput = true,
//        RedirectStandardError = true,
//        UseShellExecute = false,
//        CreateNoWindow = true,
//        WorkingDirectory = Path.GetDirectoryName(scriptFullPath) ?? AppContext.BaseDirectory
//    };

//    // Start process
//    mcpProcess = Process.Start(psi) ?? throw new Exception("Failed to start MCP process");
//    mcpStdin = mcpProcess.StandardInput;
//    mcpStdout = mcpProcess.StandardOutput;

//    // Forward stderr to our console for visibility
//    _ = Task.Run(async () =>
//    {
//        try
//        {
//            var stderr = mcpProcess.StandardError;
//            while (!stderr.EndOfStream)
//            {
//                var line = await stderr.ReadLineAsync();
//                if (!string.IsNullOrEmpty(line))
//                    Console.Error.WriteLine("[mcp-stderr] " + line);
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.Error.WriteLine("[mcp-stderr] reader stopped: " + ex.Message);
//        }
//    });

//    // Stdout dispatcher: read JSON lines from MCP and satisfy waiting callers
//    _ = Task.Run(async () =>
//    {
//        try
//        {
//            var reader = mcpStdout;
//            while (!reader.EndOfStream)
//            {
//                var line = await reader.ReadLineAsync();
//                if (string.IsNullOrWhiteSpace(line)) continue;

//                try
//                {
//                    using var doc = JsonDocument.Parse(line);
//                    if (doc.RootElement.TryGetProperty("id", out var idElem))
//                    {
//                        var id = idElem.GetString() ?? "";
//                        if (!string.IsNullOrEmpty(id) && mcpResponseMap.TryRemove(id, out var tcs))
//                        {
//                            tcs.TrySetResult(line);
//                            continue;
//                        }
//                    }
//                    // Unmatched lines log
//                    Console.Error.WriteLine("[mcp-stdout-unmatched] " + line);
//                }
//                catch (Exception e)
//                {
//                    Console.Error.WriteLine("[mcp-stdout-parse-error] " + e.Message + " :: line=" + line);
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.Error.WriteLine("[mcp-stdout-reader] stopped: " + ex.Message);
//        }
//        finally
//        {
//            // If stdout closed unexpectedly, fail all pending waiters
//            foreach (var kv in mcpResponseMap)
//            {
//                if (mcpResponseMap.TryRemove(kv.Key, out var tcs))
//                    tcs.TrySetException(new IOException("MCP stdout closed unexpectedly"));
//            }
//        }
//    });
//}

// StartMcp: discover script and start process

void StartNodeProcess(string scriptFullPath)
{
    // ensure absolute path and file exists
    var scriptFull = Path.GetFullPath(scriptFullPath ?? string.Empty);
    if (!File.Exists(scriptFull))
    {
        Console.Error.WriteLine($"StartNodeProcess: script not found at '{scriptFull}'. Aborting start.");
        throw new FileNotFoundException("MCP script not found", scriptFull);
    }

    // determine working directory: prefer script folder, fall back to AppContext.BaseDirectory
    var scriptDir = Path.GetDirectoryName(scriptFull);
    string workingDir;
    if (!string.IsNullOrEmpty(scriptDir) && Directory.Exists(scriptDir))
    {
        workingDir = scriptDir;
    }
    else
    {
        workingDir = AppContext.BaseDirectory;
        Console.Error.WriteLine($"StartNodeProcess: script folder '{scriptDir}' invalid; falling back to '{workingDir}'.");
    }

    Console.Error.WriteLine($"StartNodeProcess: launching node with script='{scriptFull}' workingDirectory='{workingDir}'");

    var psi = new ProcessStartInfo
    {
        FileName = "node",
        Arguments = $"\"{scriptFull}\"",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = workingDir
    };

    try
    {
        mcpProcess = Process.Start(psi) ?? throw new Exception("Failed to start MCP process (Process.Start returned null).");
        mcpStdin = mcpProcess.StandardInput;
        mcpStdout = mcpProcess.StandardOutput;

        // stderr forward
        _ = Task.Run(async () =>
        {
            try
            {
                var stderr = mcpProcess.StandardError;
                while (!stderr.EndOfStream)
                {
                    var line = await stderr.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        Console.Error.WriteLine("[mcp-stderr] " + line);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[mcp-stderr] reader stopped: " + ex.Message);
            }
        });

        // stdout dispatcher (existing code assumed)
        _ = Task.Run(async () =>
        {
            try
            {
                var reader = mcpStdout;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("id", out var idElem))
                        {
                            var id = idElem.GetString() ?? "";
                            if (!string.IsNullOrEmpty(id) && mcpResponseMap.TryRemove(id, out var tcs))
                            {
                                tcs.TrySetResult(line);
                                continue;
                            }
                        }
                        Console.Error.WriteLine("[mcp-stdout-unmatched] " + line);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("[mcp-stdout-parse-error] " + e.Message + " :: line=" + line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[mcp-stdout-reader] stopped: " + ex.Message);
            }
            finally
            {
                foreach (var kv in mcpResponseMap)
                {
                    if (mcpResponseMap.TryRemove(kv.Key, out var tcs))
                        tcs.TrySetException(new IOException("MCP stdout closed unexpectedly"));
                }
            }
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"StartNodeProcess: failed to start node. Exception: {ex.GetType().Name} - {ex.Message}");
        throw;
    }
}

void StartMcp()
{
    lock (mcpLock)
    {
        if (mcpProcess != null && !mcpProcess.HasExited && mcpStdin != null && mcpStdout != null)
            return;

        // If existing process looks bad, clean it
        try
        {
            if (mcpProcess != null && !mcpProcess.HasExited)
            {
                try { mcpProcess.Kill(); } catch { }
                mcpProcess.Dispose();
            }
        }
        catch { /* ignore */ }

        // 1) explicit env var
        var envPath = Environment.GetEnvironmentVariable("MCP_SCRIPT_PATH");
        string? script = null;
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            script = Path.GetFullPath(envPath);
            Console.Error.WriteLine($"Using MCP_SCRIPT_PATH -> {script}");
            StartNodeProcess(script);
            return;
        }

        // 2) search upward from runtime base and current directory
        var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
        var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());

        var candidates = new[]
        {
            Path.Combine(baseDir, "..","..","..","backend","tools","mcp-text-corrector.js"),
            Path.Combine(baseDir, "..","..","backend","tools","mcp-text-corrector.js"),
            Path.Combine(baseDir, "..","backend","tools","mcp-text-corrector.js"),
            Path.Combine(baseDir, "tools","mcp-text-corrector.js"),
            Path.Combine(cwd, "backend","tools","mcp-text-corrector.js"),
            Path.Combine(cwd, "tools","mcp-text-corrector.js"),
            Path.Combine(cwd, "mcp-text-corrector.js")
        }.Select(p => Path.GetFullPath(p)).Distinct();

        foreach (var cand in candidates)
        {
            if (File.Exists(cand))
            {
                script = cand;
                break;
            }
        }
        script = @"C:\Users\srivi\Downloads\A2A-AgentA2A-Full-Patched\A2A-AgentA2A-Full-Patched\backend\tools\mcp-text-corrector.js";
        if (string.IsNullOrWhiteSpace(script))
        {
            Console.Error.WriteLine("mcp-text-corrector.js not found. Tried paths:");
            foreach (var t in candidates) Console.Error.WriteLine("  " + t);
            throw new FileNotFoundException("mcp-text-corrector.js not found. Place it under backend/tools or set MCP_SCRIPT_PATH.");
        }

        Console.Error.WriteLine($"Starting MCP script at: {script}");
        StartNodeProcess(script);
    }
}

// POST /invoke handler - writes to MCP and awaits the matching response (with one retry)
app.MapPost("/invoke", async (HttpRequest req) =>
{
    // simple bearer auth for demo
    if (!req.Headers.TryGetValue("Authorization", out var auth) || !auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;

    if (!root.TryGetProperty("input", out var inputElem) || !inputElem.TryGetProperty("text", out var textElem))
        return Results.Json(new { error = "Bad request", details = "input.text missing" }, statusCode: 400);

    var text = textElem.GetString() ?? "";

    // Build MCP message
    var id = Guid.NewGuid().ToString();
    var msgObj = new { type = "invoke", id = id, tool = "text_correct", args = new { text } };
    var json = JsonSerializer.Serialize(msgObj);

    // helper: perform single attempt of write+await via TaskCompletionSource + stdout dispatcher
    async Task<string> AttemptWriteAndAwaitAsync(int timeoutSeconds = 12)
    {
        // ensure MCP running
        StartMcp();

        if (mcpProcess == null || mcpProcess.HasExited || mcpStdin == null)
            throw new IOException("MCP process is not available");

        // create TCS waiting for id
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!mcpResponseMap.TryAdd(id, tcs))
            throw new InvalidOperationException("Failed to register response waiter");

        // try writing
        try
        {
            if (!mcpStdin.BaseStream.CanWrite)
                throw new IOException("MCP stdin not writable");

            await mcpStdin.WriteLineAsync(json);
            await mcpStdin.FlushAsync();
        }
        catch (Exception wex)
        {
            // cleanup
            mcpResponseMap.TryRemove(id, out var _);
            throw new IOException("Failed writing to MCP stdin", wex);
        }

        // wait with timeout
        using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using (ctsTimeout.Token.Register(() => tcs.TrySetCanceled()))
        {
            try
            {
                var line = await tcs.Task;
                return line;
            }
            catch (TaskCanceledException)
            {
                mcpResponseMap.TryRemove(id, out var _);
                throw new TimeoutException("MCP timeout");
            }
            catch (Exception ex)
            {
                mcpResponseMap.TryRemove(id, out var _);
                throw;
            }
        }
    }

    // Attempt + single retry logic
    string? responseLine = null;
    try
    {
        responseLine = await AttemptWriteAndAwaitAsync(12);
    }
    catch (Exception firstEx)
    {
        Console.Error.WriteLine($"[corrector-agent] First MCP attempt failed: {firstEx.GetType().Name} - {firstEx.Message}");
        // Restart MCP and retry once
        try
        {
            lock (mcpLock)
            {
                try { if (mcpProcess != null && !mcpProcess.HasExited) mcpProcess.Kill(); } catch { }
                mcpProcess = null; mcpStdin = null; mcpStdout = null;
            }
        }
        catch { /* ignore */ }

        await Task.Delay(250);
        try
        {
            responseLine = await AttemptWriteAndAwaitAsync(12);
        }
        catch (Exception secondEx)
        {
            Console.Error.WriteLine($"[corrector-agent] Second MCP attempt failed: {secondEx.GetType().Name} - {secondEx.Message}");
            return Results.Json(new { error = "MCP write failed", details = secondEx.Message }, statusCode: 502);
        }
    }

    if (string.IsNullOrWhiteSpace(responseLine))
        return Results.Json(new { error = "MCP no response" }, statusCode: 502);

    // parse response
    try
    {
        using var respDoc = JsonDocument.Parse(responseLine);
        var corrected = respDoc.RootElement.GetProperty("result").GetProperty("corrected").GetString() ?? text;
        return Results.Json(new { capability = "correct_text", output = new { corrected } }, statusCode: 200);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Invalid MCP response", details = ex.Message, raw = responseLine }, statusCode: 502);
    }
});

// graceful shutdown cleanup
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { if (mcpProcess != null && !mcpProcess.HasExited) mcpProcess.Kill(); } catch { }
});

app.Run("http://localhost:5091");

