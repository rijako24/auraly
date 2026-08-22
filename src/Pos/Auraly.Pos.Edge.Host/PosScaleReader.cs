using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace Auraly.Pos.Edge.Host;

public sealed record PosScaleReading(decimal Weight, string Unit, string PortName);

public sealed class PosScaleReader(PosPrinterConfigurationStore configuration)
{
    public async Task<PosScaleReading> ReadAsync(CancellationToken cancellationToken)
    {
        var settings = configuration.Load().Scale;
        if (settings is null || !settings.Enabled)
            throw new InvalidOperationException("La balanza no está configurada en esta caja.");

        using var port = new SerialPort(
            settings.PortName,
            settings.BaudRate,
            Enum.Parse<Parity>(settings.Parity, true),
            settings.DataBits,
            Enum.Parse<StopBits>(settings.StopBits, true))
        {
            Encoding = Encoding.ASCII,
            ReadTimeout = Math.Min(settings.TimeoutMilliseconds, 500),
            WriteTimeout = Math.Min(settings.TimeoutMilliseconds, 500),
            NewLine = "\r\n"
        };
        try
        {
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            if (settings.SendsRequest && !string.IsNullOrEmpty(settings.RequestText))
                port.Write(DecodeEscapes(settings.RequestText));

            var response = new StringBuilder();
            var until = DateTime.UtcNow.AddMilliseconds(settings.TimeoutMilliseconds);
            while (DateTime.UtcNow < until)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (port.BytesToRead > 0)
                {
                    response.Append(port.ReadExisting());
                    if (response.ToString().Contains('\n') || response.Length >= settings.StartIndex + Math.Max(1, settings.Length))
                        break;
                }
                await Task.Delay(50, cancellationToken);
            }
            var raw = response.ToString().Trim();
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("La balanza no respondió. Ingresa el peso manualmente.");
            var segment = settings.Length > 0
                ? raw.Substring(settings.StartIndex, settings.Length)
                : raw[settings.StartIndex..];
            if (settings.Reverse) segment = new string(segment.Reverse().ToArray());
            var numeric = new string(segment.Where(character => char.IsDigit(character) || character is '.' or ',' or '-').ToArray())
                .Replace(',', '.');
            if (!decimal.TryParse(numeric, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var weight))
                throw new InvalidOperationException("La respuesta de la balanza no contiene un peso válido.");
            if (settings.DivideBy1000) weight /= 1000m;
            if (weight <= 0) throw new InvalidOperationException("Coloca el producto en la balanza e inténtalo nuevamente.");
            return new PosScaleReading(weight, "kg", settings.PortName);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException("El puerto de la balanza está ocupado o no permite acceso.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("No fue posible comunicarse con la balanza.", exception);
        }
    }

    private static string DecodeEscapes(string value) => value
        .Replace("\\r", "\r", StringComparison.Ordinal)
        .Replace("\\n", "\n", StringComparison.Ordinal)
        .Replace("\\t", "\t", StringComparison.Ordinal);
}
