using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace RaftNode.Extensions;

public class PollyFactory
{
    private static StatusCode[] _gRpcErrors = new StatusCode[]
    {
        StatusCode.DeadlineExceeded,
        StatusCode.Internal,
        StatusCode.NotFound,
        StatusCode.ResourceExhausted,
        StatusCode.Unavailable,
        StatusCode.Unknown
    };

    public static IAsyncPolicy<StatusCode> GetBasicResiliencePolicy(int timeoutMilliseconds) => Policy.WrapAsync(RetryPolicy, TimeoutPolicy(timeoutMilliseconds));

    public static IAsyncPolicy<StatusCode> RetryPolicy
    {
        get
        {
            return Policy.HandleResult<StatusCode>(r => _gRpcErrors.Contains(r))
                .Or<BrokenCircuitException>()
                .Or<TimeoutRejectedException>()
                .WaitAndRetryForeverAsync((retryAttempt, context) => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }
    }

    public static IAsyncPolicy<StatusCode> CircuitBreakerPolicy
    {
        get
        {
            return Policy.HandleResult<StatusCode>(r => _gRpcErrors.Contains(r))
                .Or<TimeoutRejectedException>()
                .CircuitBreakerAsync(int.MaxValue, TimeSpan.MaxValue);
        }
    }

    public static IAsyncPolicy<StatusCode> TimeoutPolicy(int milliseconds) => Policy.TimeoutAsync<StatusCode>(TimeSpan.FromMilliseconds(milliseconds), TimeoutStrategy.Pessimistic);
}