using Akka.DependencyInjection;
using Akka.Hosting;
using RaftCore.Actors;
using RaftCore.Services;
using RaftNode.Extensions;
using RaftNode.Options;
using RaftNode.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IClusterInfoService, SimpleClusterInfoService>();
builder.Services.Configure<ClusterInfoOptions>(builder.Configuration.GetSection(ClusterInfoOptions.Key));
builder.Services.AddGrpc();
builder.Services.ConfigureGrpcClients(builder.Configuration);
builder.Services.AddAkka("raft", (builder, provider) =>
{
    builder
        .ConfigureLoggers((conf) =>
        {
            conf.LogLevel = Akka.Event.LogLevel.DebugLevel;
        })
        .WithActors((system, registry) =>
        {
            var rootRaftProps = DependencyResolver.For(system).Props<RaftActor>();
            var rootSupervisor = system.ActorOf(rootRaftProps, "raft-root-actor");
            registry.Register<RaftActor>(rootSupervisor);
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();

app.MapControllers();
app.MapGrpcService<RaftCore.Services.RaftMessagingService>();

app.Run();