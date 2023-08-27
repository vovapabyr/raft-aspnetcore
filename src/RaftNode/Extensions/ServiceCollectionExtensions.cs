using RaftCore;
using RaftNode.Options;

namespace RaftNode.Extensions;

internal static class ServiceCollectionExtensions
{
        public static IServiceCollection ConfigureGrpcClients(this IServiceCollection services, IConfiguration configuration)
        {
            var clusterInfoOptions = configuration.GetSection(ClusterInfoOptions.Key).Get<ClusterInfoOptions>();
            foreach (var nodeName in clusterInfoOptions.GetNodesNames())
            {
                services.AddGrpcClient<RaftMessagingService.RaftMessagingServiceClient>(nodeName, o =>
                {
                    o.Address = new Uri($"https://{ nodeName }:443"); 
                }).ConfigureChannel(c =>
                {
                    c.HttpHandler = new HttpClientHandler() { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
                });
            }

            return services;
        }
}