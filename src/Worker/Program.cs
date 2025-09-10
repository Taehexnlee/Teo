// src/Worker/Program.cs
using Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<WorkerService>();

var host = builder.Build();
host.Run();