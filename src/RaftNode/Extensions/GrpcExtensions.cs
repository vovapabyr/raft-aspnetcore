using Grpc.Core;

namespace RaftNode.Extensions;

internal static class GrpcExtensions
{
    // Needs to wait for the response to read status code. 
    // Converts any http exceptions to status code. If http exception occurs grpc handles it and rethrow RpcException on ReposnseAsync, with status code info.
    public async static Task<StatusCode> WaitForStatusAsync<TResponse>(this AsyncUnaryCall<TResponse> call, ILogger logger)
    {
        try
        {
            await call.ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            logger.LogWarning("Grpc call failed with status code '{statusCode}': '{message}'", ex.StatusCode, ex.Message);
            return ex.StatusCode;
        } 
        catch (Exception ex)
        {
            logger.LogError(ex, "Grpc call failed unexepectedly.");
            return StatusCode.Unknown;
        }

        var statusCode = call.GetStatus().StatusCode;
        logger.LogDebug("Returning grpc call status code '{code}'.", statusCode);
        return statusCode;
    }
}