using System.Net;
using Auraly.Contracts.Dispatching;
using Auraly.Pos.Printing;
using Microsoft.Playwright;

namespace Auraly.Foundation.Tests;

public sealed class DispatchClosureReceiptRendererTests
{
    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    public async Task Current_receipt_identifies_dispatch_and_vehicle_without_exposing_user_or_expected_amounts(int width)
    {
        var dispatch = Example(DispatchSettlementSummary.CurrentReceiptTemplateVersion);
        var html = DispatchClosureReceiptRenderer.RenderHtml(dispatch, TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"), "Empresa de prueba", width);
        Assert.Contains("data-auraly-report-version=\"2\"", html);
        Assert.Contains("Transportador: Ana &amp; Luis", html);
        Assert.Contains("Placa: ABC123", html);
        Assert.Contains("Fecha del despacho: 19/11/2026", html);
        Assert.Contains("Documentos: 0", html);
        Assert.Contains("Fecha de cierre: 20/11/2026 11:31 -05:00", html);
        Assert.DoesNotContain("Usuario:", html);
        Assert.DoesNotContain(dispatch.Settlement!.TransporterClosedBy!.Value.ToString(), html);
        Assert.DoesNotContain("Efectivo esperado", html);
        Assert.DoesNotContain("99.999", html);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var requests = 0;
        page.Request += (_, _) => requests++;
        await page.SetContentAsync(html);
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        Assert.Contains("24.500", await page.Locator("table").InnerTextAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.body.scrollWidth <= document.body.clientWidth"));
        Assert.Equal(0, requests);
        var artifacts = Path.Combine(AppContext.BaseDirectory, "TestResults", "dispatch-closure-v2");
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, $"receipt-{width}.html"), html);
        await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"receipt-{width}.png"), FullPage = true });
    }

    [Fact]
    public void Historical_receipt_keeps_its_original_template_and_unknown_versions_fail_explicitly()
    {
        var dispatch = Example(1);
        var zone = TimeZoneInfo.Utc;
        var html = DispatchClosureReceiptRenderer.RenderHtml(dispatch, zone);
        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.Contains($"Usuario: {dispatch.Settlement!.TransporterClosedBy}", html);
        Assert.DoesNotContain("Fecha del despacho:", html);
        Assert.Throws<InvalidOperationException>(() => DispatchClosureReceiptRenderer.RenderHtml(
            dispatch with { Settlement = dispatch.Settlement with { ReceiptTemplateVersion = 999 } }, zone));
    }

    [Fact]
    public void Missing_vehicle_is_explicit_and_vehicle_text_cannot_inject_markup()
    {
        var dispatch = Example(2);
        Assert.Contains("Placa: Sin placa registrada", DispatchClosureReceiptRenderer.RenderHtml(dispatch with { VehiclePlate = null }, TimeZoneInfo.Utc));
        var plate = "<script>alert(1)</script>";
        var html = DispatchClosureReceiptRenderer.RenderHtml(dispatch with { VehiclePlate = plate }, TimeZoneInfo.Utc);
        Assert.Contains(WebUtility.HtmlEncode(plate), html);
        Assert.DoesNotContain(plate, html);
    }

    private static DispatchExecutionDetail Example(int version) => new(
        Guid.NewGuid(), "DSP-20261120-0001", new DateOnly(2026, 11, 19), "Ana & Luis", "ABC123", "PendingSettlement", [], [],
        new DispatchSettlementSummary(99_999, 0, 99_999, 24_500, -75_499, 0, 0, 0, 0, 0, 99_999, 0, "PendingReview", null, null,
            DeclaredCard: 0, DeclaredTransfer: 0, TransporterClosedBy: Guid.NewGuid(),
            TransporterClosedAt: DateTimeOffset.Parse("2026-11-20T16:31:00Z"), ReceiptTemplateVersion: version));
}
