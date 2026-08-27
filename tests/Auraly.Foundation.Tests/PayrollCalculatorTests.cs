using Auraly.Commerce.Payroll.Domain;

namespace Auraly.Foundation.Tests;

public sealed class PayrollCalculatorTests
{
    [Fact]
    public void Calculate_UsesVersionedRulesAndProducesBalancedEmployeeTotals()
    {
        var input = Input(monthlySalary: 3_000_000m);

        var result = new PayrollCalculator().Calculate(input);

        Assert.Equal(3_162_000m, result.Earnings);
        Assert.Equal(240_000m, result.Deductions);
        Assert.Equal(2_922_000m, result.NetPayable);
        Assert.Equal(result.Earnings - result.Deductions, result.NetPayable);
        Assert.Equal(30m, result.WorkedDays);
        Assert.NotEmpty(result.CalculationHash);
        Assert.Contains(result.Lines, line => line.ConceptCode == "BASIC" && line.Amount == 3_000_000m);
        Assert.Contains(result.Lines, line => line.ConceptCode == "HEALTH_EMPLOYEE" && line.Amount == 120_000m);
        Assert.Contains(result.Lines, line => line.ConceptCode == "ARL" && line.Amount == 15_660m);
    }

    [Fact]
    public void Calculate_RejectsDeductionWithoutCurrentAuthority()
    {
        var deduction = Concept("CUSTOM_DEDUCTION", PayrollLineNature.Deduction,
            requiresAgreement: true);
        var input = Input(3_000_000m) with
        {
            Novelties =
            [
                new PayrollNoveltyInput(Guid.NewGuid(), deduction, PayrollNoveltyTypeCodes.Amount,
                    1m, null, 100_000m,
                    Guid.NewGuid(), HasActiveDeductionAuthority: false,
                    MustProtectMinimumNetPay: true)
            ]
        };

        var error = Assert.Throws<PayrollCalculationException>(() =>
            new PayrollCalculator().Calculate(input));

        Assert.Contains("acuerdo de deducción vigente", error.Message);
    }

    [Fact]
    public void Calculate_RejectsAuthorizedDeductionThatAffectsProtectedMinimum()
    {
        var deduction = Concept("LOAN", PayrollLineNature.Deduction,
            requiresAgreement: true);
        var input = Input(1_500_000m) with
        {
            Novelties =
            [
                new PayrollNoveltyInput(Guid.NewGuid(), deduction, PayrollNoveltyTypeCodes.Amount,
                    1m, null, 400_000m,
                    Guid.NewGuid(), HasActiveDeductionAuthority: true,
                    MustProtectMinimumNetPay: true)
            ]
        };

        var error = Assert.Throws<PayrollCalculationException>(() =>
            new PayrollCalculator().Calculate(input));

        Assert.Contains("mínimo protegido", error.Message);
    }

    [Fact]
    public void Calculate_UsesQuantityByRateAndSubtractsDeductionDays()
    {
        var hourly = Concept("OVERTIME", PayrollLineNature.Earning,
            method: PayrollCalculationMethodCodes.QuantityByRate);
        var absence = Concept("UNPAID_LEAVE", PayrollLineNature.Deduction);
        var input = Input(3_000_000m) with
        {
            Novelties =
            [
                new(Guid.NewGuid(), hourly, PayrollNoveltyTypeCodes.Hours, 2m, 25_000m, 0m,
                    null, true, false),
                new(Guid.NewGuid(), absence, PayrollNoveltyTypeCodes.Days, 2m, null, 0m,
                    null, true, false)
            ]
        };

        var result = new PayrollCalculator().Calculate(input);

        Assert.Equal(28m, result.WorkedDays);
        Assert.Contains(result.Lines, line => line.ConceptCode == "OVERTIME" && line.Amount == 50_000m);
        Assert.Contains(result.Lines, line => line.ConceptCode == "BASIC" && line.Amount == 2_800_000m);
    }

    [Fact]
    public void Calculate_AppliesEmployeeWithholdingPercentageToTaxableSalaryBase()
    {
        var withholding = Concept("WITHHOLDING", PayrollLineNature.Deduction,
            method: PayrollCalculationMethodCodes.PercentageOfBase,
            isTaxWithholdingBase: true);
        var input = Input(3_000_000m) with
        {
            Novelties =
            [
                new(Guid.NewGuid(), withholding, PayrollNoveltyTypeCodes.Amount,
                    1m, .05m, 0m, null, true, false)
            ]
        };

        var result = new PayrollCalculator().Calculate(input);

        Assert.Contains(result.Lines,
            line => line.ConceptCode == "WITHHOLDING" && line.Amount == 150_000m);
    }

    private static PayrollEmployeeCalculationInput Input(decimal monthlySalary)
    {
        var concepts = new Dictionary<string, PayrollConceptDefinition>(StringComparer.Ordinal)
        {
            [PayrollSystemConceptRoles.BasicSalary] = Concept("BASIC", PayrollLineNature.Earning, PayrollSystemConceptRoles.BasicSalary),
            [PayrollSystemConceptRoles.TransportAllowance] = Concept("TRANSPORT", PayrollLineNature.Earning, PayrollSystemConceptRoles.TransportAllowance),
            [PayrollSystemConceptRoles.EmployeeHealth] = Concept("HEALTH_EMPLOYEE", PayrollLineNature.Deduction, PayrollSystemConceptRoles.EmployeeHealth),
            [PayrollSystemConceptRoles.EmployeePension] = Concept("PENSION_EMPLOYEE", PayrollLineNature.Deduction, PayrollSystemConceptRoles.EmployeePension),
            [PayrollSystemConceptRoles.EmployerHealth] = Concept("HEALTH_EMPLOYER", PayrollLineNature.EmployerContribution, PayrollSystemConceptRoles.EmployerHealth),
            [PayrollSystemConceptRoles.EmployerPension] = Concept("PENSION_EMPLOYER", PayrollLineNature.EmployerContribution, PayrollSystemConceptRoles.EmployerPension),
            [PayrollSystemConceptRoles.OccupationalRisk] = Concept("ARL", PayrollLineNature.EmployerContribution, PayrollSystemConceptRoles.OccupationalRisk),
            [PayrollSystemConceptRoles.CompensationFund] = Concept("CCF", PayrollLineNature.EmployerContribution, PayrollSystemConceptRoles.CompensationFund),
            [PayrollSystemConceptRoles.Sena] = Concept("SENA", PayrollLineNature.EmployerContribution, PayrollSystemConceptRoles.Sena),
            [PayrollSystemConceptRoles.Icbf] = Concept("ICBF", PayrollLineNature.EmployerContribution, PayrollSystemConceptRoles.Icbf),
            [PayrollSystemConceptRoles.SeveranceProvision] = Concept("SEVERANCE", PayrollLineNature.Provision, PayrollSystemConceptRoles.SeveranceProvision),
            [PayrollSystemConceptRoles.SeveranceInterestProvision] = Concept("SEVERANCE_INTEREST", PayrollLineNature.Provision, PayrollSystemConceptRoles.SeveranceInterestProvision),
            [PayrollSystemConceptRoles.ServiceBonusProvision] = Concept("BONUS", PayrollLineNature.Provision, PayrollSystemConceptRoles.ServiceBonusProvision),
            [PayrollSystemConceptRoles.VacationProvision] = Concept("VACATION", PayrollLineNature.Provision, PayrollSystemConceptRoles.VacationProvision)
        };
        return new PayrollEmployeeCalculationInput(Guid.NewGuid(), Guid.NewGuid(), monthlySalary, 30m,
            IsIntegralSalary: false, IsEmployerExemptFromHealthSenaIcbf: false, RiskRate: 0.00522m,
            Rules(), concepts, []);
    }

    private static IReadOnlyDictionary<string, decimal> Rules() => new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        [PayrollRuleParameterCodes.MonthlyDays] = 30m,
        [PayrollRuleParameterCodes.MinimumMonthlySalary] = 1_500_000m,
        [PayrollRuleParameterCodes.TransportAllowance] = 162_000m,
        [PayrollRuleParameterCodes.TransportAllowanceSalaryLimitMultiple] = 2m,
        [PayrollRuleParameterCodes.EmployeeHealthRate] = 0.04m,
        [PayrollRuleParameterCodes.EmployeePensionRate] = 0.04m,
        [PayrollRuleParameterCodes.EmployerHealthRate] = 0.085m,
        [PayrollRuleParameterCodes.EmployerPensionRate] = 0.12m,
        [PayrollRuleParameterCodes.CompensationFundRate] = 0.04m,
        [PayrollRuleParameterCodes.SenaRate] = 0.02m,
        [PayrollRuleParameterCodes.IcbfRate] = 0.03m,
        [PayrollRuleParameterCodes.SeveranceRate] = 0.08333333m,
        [PayrollRuleParameterCodes.SeveranceInterestRate] = 0.12m,
        [PayrollRuleParameterCodes.ServiceBonusRate] = 0.08333333m,
        [PayrollRuleParameterCodes.VacationRate] = 0.04166667m,
        [PayrollRuleParameterCodes.IntegralSalaryContributionBaseRate] = 0.70m,
        [PayrollRuleParameterCodes.MaximumContributionBaseMinimumWages] = 25m,
        [PayrollRuleParameterCodes.NonSalaryExclusionThresholdRate] = 0.40m
    };

    private static PayrollConceptDefinition Concept(string code, PayrollLineNature nature,
        string? role = null, bool requiresAgreement = false,
        string method = PayrollCalculationMethodCodes.FixedAmount,
        bool isTaxWithholdingBase = false) =>
        new(Guid.NewGuid(), code, code, nature, method, "TestCategory", null, role,
            IsSalaryBase: nature == PayrollLineNature.Earning && code == "BASIC",
            IsSocialSecurityBase: false, IsBenefitsBase: false,
            IsTaxWithholdingBase: isTaxWithholdingBase, RequiresDeductionAgreement: requiresAgreement);
}
