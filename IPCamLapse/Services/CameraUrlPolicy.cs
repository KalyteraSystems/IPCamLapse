using System.Net;
using IPCamLapse.Options;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Services;

public sealed record CameraUrlValidationResult(Uri? Uri, string? Error)
{
    public bool IsValid => Uri is not null;

    public static CameraUrlValidationResult Success(Uri uri) => new(uri, null);
    public static CameraUrlValidationResult Failure(string error) => new(null, error);
}

public interface ICameraUrlPolicy
{
    Task<CameraUrlValidationResult> ValidateAsync(string candidate, CancellationToken cancellationToken = default);
}

public sealed class CameraUrlPolicy : ICameraUrlPolicy
{
    private readonly CameraAccessOptions _options;

    public CameraUrlPolicy(IOptions<CameraAccessOptions> options)
    {
        _options = options.Value;
    }

    public async Task<CameraUrlValidationResult> ValidateAsync(
        string candidate,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return CameraUrlValidationResult.Failure("Enter an absolute camera URL.");

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return CameraUrlValidationResult.Failure("Only HTTP and HTTPS camera URLs are supported.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return CameraUrlValidationResult.Failure("Do not embed credentials in the camera URL.");

        if (!string.IsNullOrEmpty(uri.Fragment))
            return CameraUrlValidationResult.Failure("Camera URLs cannot contain fragments.");

        if (IPAddress.TryParse(uri.Host, out var literalAddress))
            return ValidateAddress(uri, literalAddress);

        if (!_options.AllowHostnames)
            return CameraUrlValidationResult.Failure(
                "Hostnames are disabled by default. Use a private IP address or explicitly enable CameraAccess:AllowHostnames.");

        IPAddress[] resolvedAddresses;
        try
        {
            resolvedAddresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch
        {
            return CameraUrlValidationResult.Failure("The camera hostname could not be resolved.");
        }

        if (resolvedAddresses.Length == 0)
            return CameraUrlValidationResult.Failure("The camera hostname did not resolve to an address.");

        if (!_options.AllowPublicAddresses && resolvedAddresses.Any(address => !IsPrivateOrLoopback(address)))
            return CameraUrlValidationResult.Failure("The camera hostname resolves outside the private network.");

        return CameraUrlValidationResult.Success(uri);
    }

    private CameraUrlValidationResult ValidateAddress(Uri uri, IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.None))
        {
            return CameraUrlValidationResult.Failure("The camera URL uses an unspecified or broadcast address.");
        }

        if (!_options.AllowPublicAddresses && !IsPrivateOrLoopback(address))
            return CameraUrlValidationResult.Failure("Public camera addresses are disabled by default.");

        return CameraUrlValidationResult.Success(uri);
    }

    internal static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
                return true;

            var bytes = address.GetAddressBytes();
            return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
        }

        var octets = address.GetAddressBytes();
        return octets.Length == 4 &&
               (octets[0] == 10 ||
                (octets[0] == 172 && octets[1] is >= 16 and <= 31) ||
                (octets[0] == 192 && octets[1] == 168) ||
                (octets[0] == 169 && octets[1] == 254));
    }
}
