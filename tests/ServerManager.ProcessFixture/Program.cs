using System.Net;
using System.Net.Sockets;
using System.Text;

// Disposable process used by --runtime-qa. This is not the game server and never reads real server files.
var mode = args[0]; var port = int.Parse(args[1]); var delay = args.Length > 2 ? int.Parse(args[2]) : 0;
using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (mode == "ignore") return;
    File.WriteAllText("shutdown-marker.txt", "Ctrl+C received");
    lifetime.Cancel();
};
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("SIGNAL HANDLER READY");
if (mode == "early") { await Task.Delay(150); Console.Error.WriteLine("FINAL STARTUP ERROR"); return 42; }
if (mode == "burst")
{
    for (var i = 0; i < 30000; i++) Console.WriteLine("entry " + i + " password=burst-secret");
    Console.Write(new string('x', 300000)); Console.WriteLine();
    Console.Error.WriteLine("FINAL BURST MARKER");
}
await Task.Delay(delay);
using var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
try
{
    while (!lifetime.IsCancellationRequested)
    {
        using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        _ = await stream.ReadAsync(buffer, lifetime.Token);
        var body = mode == "invalid" ? "<html>wrong service</html>" : mode == "mismatch" ? "{\"track\":\"wrong-track\"}" : "{\"track\":\"fixture-layout\"}";
        var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(response, lifetime.Token);
    }
}
catch (OperationCanceledException) { }
Console.WriteLine("FINAL SHUTDOWN LINE");
return 0;
