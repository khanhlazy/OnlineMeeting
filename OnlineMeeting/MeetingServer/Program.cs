using MeetingServer;
using Microsoft.Extensions.Configuration;
using System.Net;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional:false)
    .Build();
var host = config["Server:Host"] ?? "0.0.0.0";
var port = int.Parse(config["Server:Port"] ?? "5555");
var connStr = config.GetConnectionString("MeetingDb")!;

var db = new Db(connStr);
var server = new Server(IPAddress.Parse(host), port, db);

Console.WriteLine("=== MeetingServer khởi động ===");
Console.WriteLine($"Endpoint: {host}:{port}");
Console.WriteLine("Nhấn Ctrl+C để dừng.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await server.RunAsync(cts.Token);
