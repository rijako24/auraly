using MimosBabySpa.IntegrationTests.Reports;
using MimosBabySpa.IntegrationTests.Runner;
using MimosBabySpa.IntegrationTests.Scenarios.Definitions;

var scenarios = new List<MimosBabySpa.IntegrationTests.Scenarios.TestScenario>
{
    new SuccessfulReservationScenario(),
    new NoAvailabilityScenario(),
    new ConfirmationWithoutAvailabilityScenario(),
    new DoubleBookingScenario(),
    new BackendCalendarErrorScenario(),
    new UserChangesDateScenario(),
    new RepeatReservationAfterCompletionScenario(),
    new AddOnOfferingScenario(),
    // 20 conversaciones completas (desde inicio hasta reserva)
    new FullReservationStyle1FormalScenario(),
    new FullReservationStyle2ColloquialScenario(),
    new FullReservationStyle3AllInOneScenario(),
    new FullReservationStyle4ConversationalScenario(),
    new FullReservationStyle5DateCorrectionsScenario(),
    new FullReservationStyle6WithAddOnScenario(),
    new FullReservationStyle7NoAddOnScenario(),
    new FullReservationStyle8OneWordScenario(),
    new FullReservationStyle9WithEmailScenario(),
    new FullReservationStyle10FutureDateScenario(),
    new FullReservationStyle11LongMessageScenario(),
    new FullReservationStyle12ImpatientScenario(),
    new FullReservationStyle13ServiceChangeScenario(),
    new FullReservationStyle14TimeWithMinutesScenario(),
    new FullReservationStyle15AskFirstScenario(),
    new FullReservationStyle16DeluxeAddOnTwoStepsScenario(),
    new FullReservationStyle17ConfirmationSynonymsScenario(),
    new FullReservationStyle18CompoundNameScenario(),
    new FullReservationStyle19MinimalScenario(),
    new FullReservationStyle20FourStepsScenario(),
};

scenarios.AddRange(AdditionalReservationScenario.BuildAll());

Console.WriteLine("Starting MimosBabySpa Integration Tests...");
Console.WriteLine($"   Scenarios to run: {scenarios.Count}");
Console.WriteLine();

var runner  = new ScenarioRunner();
var results = await runner.RunAllAsync(scenarios);

var printer = new ConsoleReportPrinter();
printer.Print(results);

// -- Save JSON report -------------------------------------------------------
var reportPath = Path.Combine(
    AppContext.BaseDirectory, "test-reports",
    $"integration-report-{DateTime.UtcNow:yyyyMMddHHmmss}.json");

var jsonGen = new JsonReportGenerator();
await jsonGen.SaveAsync(results, reportPath);

Console.WriteLine($"JSON report saved at: {reportPath}");
Console.WriteLine();

// -- Exit code --------------------------------------------------------------
var allPassed = results.All(r => r.Passed);
return allPassed ? 0 : 1;
