using Auraly.Contracts.Fiscal;

namespace Auraly.Application.Fiscal;

public interface IPosFiscalStatusStore
{
    Task<PosFiscalStatusPage> PageAsync(
        PosFiscalDeviceContext device,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed class PosFiscalStatusService(IPosFiscalStatusStore store)
{
    public Task<PosFiscalStatusPage> PageAsync(
        PosFiscalDeviceContext device,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.DeviceId == Guid.Empty ||
            device.BusinessId == Guid.Empty ||
            !device.Permissions.Contains(FiscalPermissionCodes.PosStatusSync))
            throw new FiscalForbiddenException(
                $"Permission '{FiscalPermissionCodes.PosStatusSync}' is required.");
        if (pageSize is < 1 or > 200)
            throw new FiscalOperationException("PageSize must be between 1 and 200.");
        ValidateCursor(cursor);
        return store.PageAsync(device, cursor, pageSize, cancellationToken);
    }

    public static byte[] DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return new byte[8];
        try
        {
            var value = Convert.FromBase64String(cursor);
            return value.Length == 8
                ? value
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new FiscalOperationException("The fiscal status cursor is invalid.");
        }
    }

    public static string EncodeCursor(byte[] cursor)
    {
        if (cursor.Length != 8)
            throw new ArgumentException("A SQL rowversion cursor must contain eight bytes.", nameof(cursor));
        return Convert.ToBase64String(cursor);
    }

    private static void ValidateCursor(string? cursor) => DecodeCursor(cursor);
}
