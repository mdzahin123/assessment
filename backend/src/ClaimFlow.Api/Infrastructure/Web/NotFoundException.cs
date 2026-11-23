namespace ClaimFlow.Api.Infrastructure.Web;

/// <summary>
/// The thing does not exist — or the caller is not allowed to know it exists
/// (e.g. a claimant asking for someone else's claim). Mapped to HTTP 404.
/// </summary>
public class NotFoundException(string message) : Exception(message);
