namespace SmashCourt_BE.Integrations.AI;

public interface IFastApiClient
{
    Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default);

    Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default);
}
