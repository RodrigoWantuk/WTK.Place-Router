using System.Security.Cryptography;

namespace PlaceRouter.DesignExchange.Prdx;

internal static class Sha256
{
    public static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
