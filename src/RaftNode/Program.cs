using RaftCore;
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
builder.Services.AddSingleton<RaftModule>();
builder.Services.AddSingleton<INodeRoleBehaviourService, FollowerBehaviourService>();
builder.Services.AddSingleton<INodeRoleBehaviourService, CandidateBehaviourService>();
builder.Services.AddSingleton<INodeRoleBehaviourService, LeaderBehaviourService>();
builder.Services.AddSingleton<NodeStateService>();
builder.Services.Configure<ClusterInfoOptions>(builder.Configuration.GetSection(ClusterInfoOptions.Key));
builder.Services.AddGrpc();
builder.Services.ConfigureGrpcClients(builder.Configuration);
builder.Services.AddHostedService<RaftNodeHostedService>();

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
app.MapGrpcService<DiscoveryService>();

app.Run();