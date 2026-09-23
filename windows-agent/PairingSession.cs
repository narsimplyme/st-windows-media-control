using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace STMediaBridge;

// A short-lived human code exchanges the internal credentials only with the
// configured hub (source filtering is enforced by the HTTP middleware).
public sealed class PairingSession
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private string? code;
    private DateTimeOffset expires, window;
    private int attempts;
    public PairingSession(TimeProvider? clock = null) => this.clock = clock ?? TimeProvider.System;

    public string Generate()
    {
        lock (gate)
        {
            string next;
            do
            {
                next = RandomNumberGenerator.GetInt32(1, 10).ToString(CultureInfo.InvariantCulture)
                    + RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("D9", CultureInfo.InvariantCulture);
            } while (next == code);
            code = next;
            expires = clock.GetUtcNow().AddMinutes(10);
            return code;
        }
    }

    public TimeSpan Remaining { get { lock (gate) return expires - clock.GetUtcNow(); } }

    // 200: accepted, 401: absent/expired/wrong code, 429: attempt budget exhausted.
    // Permit response retries until expiry; the Edge driver then persists the
    // full credentials, so ordinary restarts need no new code.
    public int Exchange(string? supplied)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (now >= window) { window = now.AddMinutes(1); attempts = 0; }
            if (attempts >= 5) return 429;
            attempts++;
            if (code is null || now >= expires || supplied is null || supplied.Length != 10 ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.UTF8.GetBytes(supplied)))
                return 401;
            return 200;
        }
    }
}
