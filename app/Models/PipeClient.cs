using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TestingApp.Models;

// The mod's console link: \\.\pipe\abhyu-input-buffer-testing, JSON lines in both directions.
// The mod has ONE pipe instance, so only one client can be connected at a time (this app OR tools/pipe-test.ps1).
public sealed class PipeClient : IDisposable
{
    public const string PipeName = "abhyu-input-buffer-testing";

    NamedPipeClientStream? _pipe;
    StreamWriter? _writer;
    CancellationTokenSource? _cts;
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly ConcurrentDictionary<int, TaskCompletionSource<(bool Ok, string Error)>> _acks = new();
    int _nextId = 1;

    public event Action<JsonElement>? Message;   // raised on a background thread
    public event Action? Closed;                 // raised on a background thread

    public bool Connected => _pipe is { IsConnected: true };

    public async Task<string?> ConnectAsync()
    {
        Disconnect();
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var cts = new CancellationTokenSource(2500);
            await pipe.ConnectAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            pipe.Dispose();
            return "Couldn't connect. Another client (tools/pipe-test.ps1?) may be connected, or the console link is off in the game (Ctrl+F5 switches it).";
        }
        catch (Exception ex)
        {
            pipe.Dispose();
            return "Couldn't connect: " + ex.Message;
        }
        _pipe = pipe;
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = false };
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => ReadLoop(pipe, token));
        return null;
    }

    async Task ReadLoop(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 65536, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (line.Length == 0) continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch { continue; }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    if (root.TryGetProperty("type", out var t) && t.GetString() == "ack" &&
                        root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id) && _acks.TryRemove(id, out var tcs))
                    {
                        bool ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                        string err = root.TryGetProperty("error", out var ee) ? ee.GetString() ?? "" : "";
                        tcs.TrySetResult((ok, err));
                    }
                    Message?.Invoke(root.Clone());
                }
            }
        }
        catch { }
        finally
        {
            if (ReferenceEquals(_pipe, pipe)) Cleanup();
            Closed?.Invoke();
        }
    }

    void Cleanup()
    {
        try { _cts?.Cancel(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _writer = null;
        _pipe = null;
        foreach (var kv in _acks)
            if (_acks.TryRemove(kv.Key, out var tcs)) tcs.TrySetResult((false, "connection closed"));
    }

    public void Disconnect() => Cleanup();

    // Sends {"cmd":..., ...fields, "id":N} and waits (3 s) for the matching ack.
    public async Task<(bool Ok, string Error)> RequestAsync(string cmd, params (string Name, object Value)[] fields)
    {
        var writer = _writer;
        if (writer == null || !Connected) return (false, "not connected");
        int id = Interlocked.Increment(ref _nextId);
        var dict = new Dictionary<string, object> { ["cmd"] = cmd, ["id"] = id };
        foreach (var (name, value) in fields) dict[name] = value;
        var tcs = new TaskCompletionSource<(bool Ok, string Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _acks[id] = tcs;
        try
        {
            await _sendLock.WaitAsync();
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(dict));
                await writer.FlushAsync();
            }
            finally { _sendLock.Release(); }
        }
        catch (Exception ex)
        {
            _acks.TryRemove(id, out _);
            return (false, ex.Message);
        }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        if (done != tcs.Task)
        {
            _acks.TryRemove(id, out _);
            return (false, "no answer from the game");
        }
        return await tcs.Task;
    }

    public void Dispose() => Cleanup();
}
