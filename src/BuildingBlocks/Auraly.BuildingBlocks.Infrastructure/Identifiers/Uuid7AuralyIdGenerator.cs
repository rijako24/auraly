using System.Security.Cryptography;
using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.BuildingBlocks.Infrastructure.Identifiers;

public sealed class Uuid7AuralyIdGenerator(TimeProvider timeProvider) : IAuralyIdGenerator
{
    public Guid NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var milliseconds = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (milliseconds is < 0 or > 0x0000FFFFFFFFFFFF)
        {
            throw new InvalidOperationException("The current timestamp cannot be represented as UUIDv7.");
        }

        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
