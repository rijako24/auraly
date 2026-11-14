using Auraly.Commerce.Payroll.Domain;

namespace Auraly.Foundation.Tests;

public sealed class PayrollCalculatorTests
{
    [Fact]
    public void Fixed_amount_novelties_keep_zero_quantity_and_their_value_and_origin()
    {
        var novelty=new PayrollNoveltyInput(Guid.NewGuid(),Concept("FIXED",PayrollLineNature.Earning),
            PayrollNoveltyTypeCodes.Amount,0,null,125_000,null,true,false);
        var calculator=new PayrollCalculator();
        var input=Input(3_000_000) with {Novelties=[novelty]};
        var result=calculator.Calculate(input);
        var line=Assert.Single(result.Lines,item=>item.NoveltyId==novelty.NoveltyId);
        Assert.Equal(0,line.Quantity);Assert.Equal(125_000,line.Amount);
        Assert.Equal(result.CalculationHash,calculator.Calculate(input).CalculationHash);
        Assert.NotEqual(result.CalculationHash,calculator.Calculate(input with {Novelties=[novelty with {Quantity=1}]}).CalculationHash);
    }

    [Theory]
    [InlineData(PayrollCalculationMethodCodes.FixedAmount,PayrollNoveltyTypeCodes.Amount,-1)]
    [InlineData(PayrollCalculationMethodCodes.FixedAmount,PayrollNoveltyTypeCodes.Days,0)]
    [InlineData(PayrollCalculationMethodCodes.FixedAmount,PayrollNoveltyTypeCodes.Hours,0)]
    [InlineData(PayrollCalculationMethodCodes.QuantityByRate,PayrollNoveltyTypeCodes.Hours,0)]
    [InlineData(PayrollCalculationMethodCodes.PercentageOfBase,PayrollNoveltyTypeCodes.Amount,0)]
    [InlineData(PayrollCalculationMethodCodes.FixedAmount,PayrollNoveltyTypeCodes.Amount,0.0000001)]
    public void Novelty_quantity_requires_a_representable_positive_unit_except_for_fixed_amounts(string method,string type,double quantity)
    {
        var input=Input(3_000_000) with {Novelties=[new PayrollNoveltyInput(Guid.NewGuid(),
            Concept("INPUT",PayrollLineNature.Earning,method:method),type,(decimal)quantity,1,125_000,null,true,false)]};
        Assert.Throws<PayrollCalculationException>(()=>new PayrollCalculator().Calculate(input));
    }

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
    public void Editing_final_days_preserves_the_absence_input_and_recalculates_statutory_deductions()
    {
        var absence = new PayrollNoveltyInput(Guid.NewGuid(), Concept("ABSENCE", PayrollLineNature.Deduction),
            PayrollNoveltyTypeCodes.Days, 2m, null, 0m, null, true, false);
        var input = Input(3_000_000m) with { Novelties = [absence] };
        var calculator = new PayrollCalculator();
        var unchanged = calculator.WithFinalWorkedDays(input, 28, 30);
        Assert.Equal(30, unchanged.WorkedDays);
        var edited = calculator.WithFinalWorkedDays(input, 25, 30);
        Assert.Equal(27, edited.WorkedDays);
        var result = calculator.Calculate(edited);
        Assert.Equal(25, result.WorkedDays);
        Assert.Equal(2_500_000m, result.Lines.Single(line => line.ConceptCode == "BASIC").Amount);
        Assert.Equal(100_000m, result.Lines.Single(line => line.ConceptCode == "HEALTH_EMPLOYEE").Amount);
        var absenceLine = Assert.Single(result.Lines, line => line.NoveltyId == absence.NoveltyId);
        Assert.Equal(2, absenceLine.Quantity); Assert.Equal(0, absenceLine.Amount);
        Assert.Equal(result.CalculationHash, calculator.Calculate(edited).CalculationHash);
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(31, 30)]
    [InlineData(16, 15)]
    [InlineData(14, 15)]
    [InlineData(0.00001, 30)]
    [InlineData(0, 0)]
    public void Editing_days_rejects_out_of_period_values_and_excess_precision(decimal days, int periodDays)
    {
        var input = Input(3_000_000m) with
        {
            Novelties = [new(Guid.NewGuid(), Concept("ABSENCE", PayrollLineNature.Deduction),
                PayrollNoveltyTypeCodes.Days, 2m, null, 0m, null, true, false)]
        };
        Assert.Throws<PayrollCalculationException>(() => new PayrollCalculator().WithFinalWorkedDays(input, days, periodDays));
    }

    [Fact]
    public void Absences_cannot_also_charge_a_financial_deduction()
    {
        var input = Input(3_000_000m) with
        {
            Novelties = [new(Guid.NewGuid(), Concept("ABSENCE", PayrollLineNature.Deduction),
                PayrollNoveltyTypeCodes.Days, 2m, null, 200_000m, null, true, false)]
        };
        Assert.Contains("dos veces", Assert.Throws<PayrollCalculationException>(() => new PayrollCalculator().Calculate(input)).Message);
    }

    [Fact]
    public void Zero_monthly_divisor_is_a_validation_error()
    {
        var input = Input(3_000_000m);
        var rules = input.RuleParameters.ToDictionary(value => value.Key, value => value.Value);
        rules[PayrollRuleParameterCodes.MonthlyDays] = 0;
        Assert.Throws<PayrollCalculationException>(() => new PayrollCalculator().Calculate(input with { RuleParameters = rules }));
    }

    [Fact]
    public void Percentage_deductions_include_all_salary_earnings_regardless_of_novelty_identity_order()
    {
        var withholding=Concept("WITHHOLDING",PayrollLineNature.Deduction,method:PayrollCalculationMethodCodes.PercentageOfBase,isTaxWithholdingBase:true);
        var overtime=Concept("OVERTIME",PayrollLineNature.Earning,method:PayrollCalculationMethodCodes.QuantityByRate) with {IsSalaryBase=true};
        var input=Input(3_000_000m) with {Novelties=[
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"),withholding,PayrollNoveltyTypeCodes.Amount,1,.05m,0,null,true,false),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"),overtime,PayrollNoveltyTypeCodes.Hours,2,50_000m,0,null,true,false)]};
        var calculator=new PayrollCalculator();
        var result=calculator.Calculate(input);
        Assert.Equal(155_000m,result.Lines.Single(line=>line.ConceptCode=="WITHHOLDING").Amount);
        Assert.Equal(result.CalculationHash,calculator.Calculate(input with {Novelties=input.Novelties.Reverse().ToArray()}).CalculationHash);
    }

    [Fact]
    public void Percentage_earnings_do_not_compound_each_other()
    {
        var bonus=Concept("BONUS_PERCENT",PayrollLineNature.Earning,method:PayrollCalculationMethodCodes.PercentageOfBase);
        var input=Input(3_000_000m) with {Novelties=[
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"),bonus,PayrollNoveltyTypeCodes.Amount,1,.10m,0,null,true,false),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"),bonus,PayrollNoveltyTypeCodes.Amount,1,.20m,0,null,true,false)]};
        var lines=new PayrollCalculator().Calculate(input).Lines.Where(line=>line.ConceptCode=="BONUS_PERCENT").ToArray();
        Assert.Equal(new[]{300_000m,600_000m},lines.Select(line=>line.Amount));
    }

    [Fact]
    public void Protected_minimum_uses_the_calculated_percentage_deduction()
    {
        var loan=Concept("LOAN_PERCENT",PayrollLineNature.Deduction,requiresAgreement:true,method:PayrollCalculationMethodCodes.PercentageOfBase);
        var input=Input(1_500_000m) with {Novelties=[new(Guid.NewGuid(),loan,PayrollNoveltyTypeCodes.Amount,1,.10m,0,Guid.NewGuid(),true,true)]};
        Assert.Contains("mínimo protegido",Assert.Throws<PayrollCalculationException>(()=>new PayrollCalculator().Calculate(input)).Message);
    }

    [Fact]
    public void Editing_days_does_not_round_an_unrepresentable_base_silently()
    {
        var input=Input(3_000_000m) with {Novelties=[new(Guid.NewGuid(),Concept("ABSENCE",PayrollLineNature.Deduction),PayrollNoveltyTypeCodes.Days,.333333m,null,0,null,true,false)]};
        Assert.Contains("precisión",Assert.Throws<PayrollCalculationException>(()=>new PayrollCalculator().WithFinalWorkedDays(input,25,30)).Message);
        Assert.Contains("precisión",Assert.Throws<PayrollCalculationException>(()=>new PayrollCalculator().Calculate(input)).Message);
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
    [Theory]
    [InlineData(15, 81000)]
    [InlineData(30, 162000)]
    public void Contract_can_disable_transport_without_changing_contribution_base(int days, decimal expectedAllowance)
    {
        var input = Input(1_500_000m) with { WorkedDays = days, EmploymentVersion = new byte[8] };
        var enabled = new PayrollCalculator().Calculate(input);
        var disabled = new PayrollCalculator().Calculate(input with { AppliesTransportAllowance = false });
        Assert.Equal(expectedAllowance, enabled.Lines.Single(line => line.ConceptCode == "TRANSPORT").Amount);
        Assert.DoesNotContain(disabled.Lines, line => line.ConceptCode == "TRANSPORT");
        Assert.Equal(expectedAllowance, enabled.Earnings - disabled.Earnings);
        Assert.Equal(expectedAllowance, enabled.NetPayable - disabled.NetPayable);
        Assert.Equal(enabled.Deductions, disabled.Deductions);
        Assert.Equal(enabled.EmployerContributions, disabled.EmployerContributions);
        foreach (var concept in new[] { "SEVERANCE", "BONUS" })
            Assert.Equal(expectedAllowance, enabled.Lines.Single(line => line.ConceptCode == concept).BaseAmount
                - disabled.Lines.Single(line => line.ConceptCode == concept).BaseAmount);
        Assert.Equal(enabled.Lines.Single(line => line.ConceptCode == "VACATION").BaseAmount,
            disabled.Lines.Single(line => line.ConceptCode == "VACATION").BaseAmount);
        Assert.False(disabled.AppliesTransportAllowance);
        Assert.Equal(input.EmploymentVersion, disabled.EmploymentVersion);
        Assert.NotEqual(enabled.CalculationHash, disabled.CalculationHash);
    }

    [Theory]
    [InlineData(4_000_000, false)]
    [InlineData(1_500_000, true)]
    public void Enabling_transport_does_not_override_existing_salary_and_integral_conditions(decimal salary, bool integral)
    {
        var result = new PayrollCalculator().Calculate(Input(salary) with { AppliesTransportAllowance = true, IsIntegralSalary = integral });
        Assert.DoesNotContain(result.Lines, line => line.ConceptCode == "TRANSPORT");
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
