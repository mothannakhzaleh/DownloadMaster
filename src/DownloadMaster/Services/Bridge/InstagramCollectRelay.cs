using System.Security.Cryptography;
using System.Threading.Channels;

namespace DownloadMaster.Services.Bridge;

/// <summary>
/// Hands what the browser extension reads from Instagram's own page (posts, stories, highlights) to
/// the Instagram tab's Fetch. Instagram retired the endpoints the app used to call; the extension
/// watches the requests Instagram's web app makes in the user's real, logged-in browser instead.
/// Batches are accepted only for the nonce of the Fetch that is waiting.
/// </summary>
public static class InstagramCollectRelay
{
    /// <summary>First extension version that can read a profile for the app.</summary>
    public static readonly Version MinExtensionVersion = new(2, 1, 0);

    private static readonly object Gate = new();
    private static InstagramCollectSession? _active;

    public static bool ExtensionSupportsCollect() =>
        Version.TryParse(ExtensionCookieRelay.ExtensionVersion(), out var version) && version >= MinExtensionVersion;

    /// <summary>Starts a collection (replacing any older one) and returns its nonce and batches.</summary>
    public static InstagramCollectSession Begin()
    {
        var session = new InstagramCollectSession(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        InstagramCollectSession? previous;
        lock (Gate)
        {
            previous = _active;
            _active = session;
        }

        previous?.Complete();
        return session;
    }

    /// <summary>Called by the bridge; true when the batch belongs to the collection that is waiting.</summary>
    public static bool Deliver(BridgeInstagramCollectRequest request)
    {
        InstagramCollectSession? session;
        lock (Gate)
            session = _active;

        if (session is null || !string.Equals(session.Nonce, request.Nonce, StringComparison.Ordinal))
            return false;

        return session.Write(request);
    }

    internal static void End(InstagramCollectSession session)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_active, session))
                _active = null;
        }
    }
}

public sealed class InstagramCollectSession : IDisposable
{
    private readonly Channel<BridgeInstagramCollectRequest> _channel =
        Channel.CreateBounded<BridgeInstagramCollectRequest>(new BoundedChannelOptions(500)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    internal InstagramCollectSession(string nonce) => Nonce = nonce;

    public string Nonce { get; }

    public ChannelReader<BridgeInstagramCollectRequest> Batches => _channel.Reader;

    internal bool Write(BridgeInstagramCollectRequest request) => _channel.Writer.TryWrite(request);

    internal void Complete() => _channel.Writer.TryComplete();

    public void Dispose()
    {
        InstagramCollectRelay.End(this);
        Complete();
    }
}
