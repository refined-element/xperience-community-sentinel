using XperienceCommunity.Sentinel.Quoting;

namespace XperienceCommunity.Sentinel.Module.Contact;

/// <summary>
/// Submits a sanitized <see cref="QuoteSubmission"/> to the Refined Element quote intake endpoint
/// (hosted on KDaaS at <c>kentico-developer.com/api/scanner/submit</c> by default). The admin UI
/// or any other consumer hands this service a submission built from a scan run + findings and
/// gets back a success/failure result.
/// </summary>
public interface ISentinelContactService
{
    /// <summary>
    /// POSTs <paramref name="submission"/> as JSON to the configured contact endpoint.
    /// </summary>
    /// <param name="submission">Sanitized payload — caller is responsible for stripping
    /// sensitive context per <see cref="QuoteSubmission.IncludesContext"/>.</param>
    /// <param name="cancellationToken">Observed between the HTTP call and the response read;
    /// honors the caller's cancellation signal.</param>
    /// <returns>A <see cref="QuoteResult"/> containing the HTTP status and any server-returned
    /// body. Does not throw on non-success HTTP statuses, transport errors, or request timeouts
    /// — inspect <c>Success</c> on the returned result.
    /// <para>
    /// <b>Cancellation is the one exception.</b> If <paramref name="cancellationToken"/> is
    /// cancelled (caller navigated away, parent operation aborted), the implementation rethrows
    /// <see cref="OperationCanceledException"/> so async callers can unwind cleanly rather than
    /// receive a misleading "timed out" result. Callers should either let it propagate or catch
    /// it explicitly if they want to distinguish cancellation from other failures.
    /// </para></returns>
    Task<QuoteResult> SubmitAsync(QuoteSubmission submission, CancellationToken cancellationToken);
}
