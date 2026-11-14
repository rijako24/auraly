using System.Security.Cryptography;
using System.Text;

namespace Auraly.Commerce.Payroll.Domain;

public enum PayrollRunStatus { Draft, Calculated, Approved, Voided }
public enum PayrollRunKind { Regular, Adjustment }
public enum PayrollLineNature { Earning, Deduction, EmployerContribution, Provision }

public static class PayrollCalculationMethodCodes
{
    public const string FixedAmount = "FixedAmount";
    public const string QuantityByRate = "QuantityByRate";
    public const string PercentageOfBase = "PercentageOfBase";
    public const string Statutory = "Statutory";
}

public static class PayrollNoveltyTypeCodes
{
    public const string Amount = "Amount";
    public const string Hours = "Hours";
    public const string Days = "Days";
}

public static class PayrollRuleParameterCodes
{
    public const string MonthlyDays = "MonthlyDays";
    public const string MinimumMonthlySalary = "MinimumMonthlySalary";
    public const string TransportAllowance = "TransportAllowance";
    public const string TransportAllowanceSalaryLimitMultiple = "TransportAllowanceSalaryLimitMultiple";
    public const string EmployeeHealthRate = "EmployeeHealthRate";
    public const string EmployeePensionRate = "EmployeePensionRate";
    public const string EmployerHealthRate = "EmployerHealthRate";
    public const string EmployerPensionRate = "EmployerPensionRate";
    public const string CompensationFundRate = "CompensationFundRate";
    public const string SenaRate = "SenaRate";
    public const string IcbfRate = "IcbfRate";
    public const string SeveranceRate = "SeveranceRate";
    public const string SeveranceInterestRate = "SeveranceInterestRate";
    public const string ServiceBonusRate = "ServiceBonusRate";
    public const string VacationRate = "VacationRate";
    public const string IntegralSalaryContributionBaseRate = "IntegralSalaryContributionBaseRate";
    public const string MaximumContributionBaseMinimumWages = "MaximumContributionBaseMinimumWages";
    public const string NonSalaryExclusionThresholdRate = "NonSalaryExclusionThresholdRate";
}

public static class PayrollSystemConceptRoles
{
    public const string BasicSalary = "BasicSalary";
    public const string TransportAllowance = "TransportAllowance";
    public const string EmployeeHealth = "EmployeeHealth";
    public const string EmployeePension = "EmployeePension";
    public const string EmployerHealth = "EmployerHealth";
    public const string EmployerPension = "EmployerPension";
    public const string OccupationalRisk = "OccupationalRisk";
    public const string CompensationFund = "CompensationFund";
    public const string Sena = "Sena";
    public const string Icbf = "Icbf";
    public const string SeveranceProvision = "SeveranceProvision";
    public const string SeveranceInterestProvision = "SeveranceInterestProvision";
    public const string ServiceBonusProvision = "ServiceBonusProvision";
    public const string VacationProvision = "VacationProvision";
}

public sealed record PayrollConceptDefinition(
    Guid ConceptId,
    string Code,
    string Name,
    PayrollLineNature Nature,
    string CalculationMethodCode,
    string AccountingCategoryCode,
    string? DianConceptCode,
    string? SystemRoleCode,
    bool IsSalaryBase,
    bool IsSocialSecurityBase,
    bool IsBenefitsBase,
    bool IsTaxWithholdingBase,
    bool RequiresDeductionAgreement);

public sealed record PayrollNoveltyInput(
    Guid NoveltyId,
    PayrollConceptDefinition Concept,
    string NoveltyTypeCode,
    decimal Quantity,
    decimal? UnitAmount,
    decimal TotalAmount,
    Guid? DeductionAgreementId,
    bool HasActiveDeductionAuthority,
    bool MustProtectMinimumNetPay)
{
    public static void ValidateAmounts(string calculationMethodCode,string noveltyTypeCode,
        decimal quantity,decimal? unitAmount,decimal totalAmount)
    {
        var fixedAmount=calculationMethodCode==PayrollCalculationMethodCodes.FixedAmount && noveltyTypeCode==PayrollNoveltyTypeCodes.Amount;
        if(quantity<0 || decimal.Round(quantity,6)!=quantity || quantity==0 && !fixedAmount || unitAmount<0 || totalAmount<0)
            throw new PayrollCalculationException("La cantidad debe ser positiva y tener hasta seis decimales. Una novedad de importe fijo puede tener cantidad cero; sus valores no pueden ser negativos.");
    }
}

public sealed record PayrollEmployeeCalculationInput(
    Guid EmploymentId,
    Guid PartyId,
    decimal MonthlySalary,
    decimal WorkedDays,
    bool IsIntegralSalary,
    bool IsEmployerExemptFromHealthSenaIcbf,
    decimal RiskRate,
    IReadOnlyDictionary<string, decimal> RuleParameters,
    IReadOnlyDictionary<string, PayrollConceptDefinition> SystemConcepts,
    IReadOnlyList<PayrollNoveltyInput> Novelties,
    bool AppliesTransportAllowance = true,
    byte[]? EmploymentVersion = null);

public sealed record PayrollCalculatedLine(
    Guid ConceptId,
    Guid? NoveltyId,
    Guid? DeductionAgreementId,
    string ConceptCode,
    string ConceptName,
    PayrollLineNature Nature,
    string AccountingCategoryCode,
    string? DianConceptCode,
    decimal Quantity,
    decimal? Rate,
    decimal? BaseAmount,
    decimal Amount,
    bool IsEmployerCost,
    bool IsSalaryBase);

public sealed record PayrollEmployeeCalculation(
    Guid EmploymentId,
    Guid PartyId,
    decimal WorkedDays,
    decimal Earnings,
    decimal Deductions,
    decimal EmployerContributions,
    decimal Provisions,
    decimal NetPayable,
    IReadOnlyList<PayrollCalculatedLine> Lines,
    byte[] CalculationHash)
{
    public bool AppliesTransportAllowance { get; init; } = true;
    public byte[]? EmploymentVersion { get; init; }
}

public sealed class PayrollCalculator
{
    private static readonly string[] RequiredRules =
    [
        PayrollRuleParameterCodes.MonthlyDays,
        PayrollRuleParameterCodes.MinimumMonthlySalary,
        PayrollRuleParameterCodes.TransportAllowance,
        PayrollRuleParameterCodes.TransportAllowanceSalaryLimitMultiple,
        PayrollRuleParameterCodes.EmployeeHealthRate,
        PayrollRuleParameterCodes.EmployeePensionRate,
        PayrollRuleParameterCodes.EmployerHealthRate,
        PayrollRuleParameterCodes.EmployerPensionRate,
        PayrollRuleParameterCodes.CompensationFundRate,
        PayrollRuleParameterCodes.SenaRate,
        PayrollRuleParameterCodes.IcbfRate,
        PayrollRuleParameterCodes.SeveranceRate,
        PayrollRuleParameterCodes.SeveranceInterestRate,
        PayrollRuleParameterCodes.ServiceBonusRate,
        PayrollRuleParameterCodes.VacationRate,
        PayrollRuleParameterCodes.IntegralSalaryContributionBaseRate,
        PayrollRuleParameterCodes.MaximumContributionBaseMinimumWages,
        PayrollRuleParameterCodes.NonSalaryExclusionThresholdRate
    ];

    private static readonly string[] RequiredConceptRoles =
    [
        PayrollSystemConceptRoles.BasicSalary,
        PayrollSystemConceptRoles.TransportAllowance,
        PayrollSystemConceptRoles.EmployeeHealth,
        PayrollSystemConceptRoles.EmployeePension,
        PayrollSystemConceptRoles.EmployerHealth,
        PayrollSystemConceptRoles.EmployerPension,
        PayrollSystemConceptRoles.OccupationalRisk,
        PayrollSystemConceptRoles.CompensationFund,
        PayrollSystemConceptRoles.Sena,
        PayrollSystemConceptRoles.Icbf,
        PayrollSystemConceptRoles.SeveranceProvision,
        PayrollSystemConceptRoles.SeveranceInterestProvision,
        PayrollSystemConceptRoles.ServiceBonusProvision,
        PayrollSystemConceptRoles.VacationProvision
    ];

    public PayrollEmployeeCalculation Calculate(PayrollEmployeeCalculationInput input)
    {
        Validate(input);
        var rules = input.RuleParameters;
        var concepts = input.SystemConcepts;
        var monthlyDays = Rule(rules, PayrollRuleParameterCodes.MonthlyDays);
        var lines = new List<PayrollCalculatedLine>();

        var absenceDays = AbsenceDays(input);
        var workedDays = input.WorkedDays - absenceDays;
        if (workedDays < 0)
            throw new PayrollCalculationException("Los días de ausencia superan los días reconocidos del período.");
        if (decimal.Round(workedDays,4) != workedDays)
            throw new PayrollCalculationException("Los días resultantes requieren ajustar su precisión a cuatro decimales antes de guardar.");

        var basicSalary = Money(input.MonthlySalary / monthlyDays * workedDays);
        Add(lines, concepts[PayrollSystemConceptRoles.BasicSalary], workedDays, null, input.MonthlySalary / monthlyDays, basicSalary);

        IReadOnlyCollection<PayrollCalculatedLine>? percentageEarningBase = null;
        foreach (var novelty in input.Novelties.OrderBy(NoveltyStage).ThenBy(x => x.NoveltyId))
        {
            PayrollNoveltyInput.ValidateAmounts(novelty.Concept.CalculationMethodCode,novelty.NoveltyTypeCode,
                novelty.Quantity,novelty.UnitAmount,novelty.TotalAmount);
            if (novelty.Concept.RequiresDeductionAgreement &&
                (novelty.DeductionAgreementId is null || !novelty.HasActiveDeductionAuthority))
                throw new PayrollCalculationException($"El concepto '{novelty.Concept.Code}' requiere un acuerdo de deducción vigente y con evidencia.");
            var percentageEarning = novelty.Concept.Nature == PayrollLineNature.Earning &&
                novelty.Concept.CalculationMethodCode == PayrollCalculationMethodCodes.PercentageOfBase;
            if (percentageEarning) percentageEarningBase ??= lines.ToArray();
            var amount = ResolveNoveltyAmount(novelty, percentageEarning ? percentageEarningBase! : lines);
            if (IsAbsence(novelty) && amount != 0)
                throw new PayrollCalculationException("La ausencia por días ya reduce el salario. Su valor debe ser cero para no descontarla dos veces.");
            Add(lines, novelty.Concept, novelty.Quantity, novelty.NoveltyId,
                novelty.Concept.CalculationMethodCode == PayrollCalculationMethodCodes.QuantityByRate
                    ? novelty.UnitAmount
                    : null,
                amount, novelty.DeductionAgreementId);
        }

        var salaryEarnings = lines.Where(x => x.Nature == PayrollLineNature.Earning &&
                                              (x.ConceptId == concepts[PayrollSystemConceptRoles.BasicSalary].ConceptId ||
                                               x.IsSalaryBase))
            .Sum(x => x.Amount);
        var nonSalaryEarnings = lines.Where(x => x.Nature == PayrollLineNature.Earning).Sum(x => x.Amount) - salaryEarnings;
        var nonSalaryThreshold = Money((salaryEarnings + nonSalaryEarnings) * Rule(rules, PayrollRuleParameterCodes.NonSalaryExclusionThresholdRate));
        var excessNonSalary = Math.Max(0m, nonSalaryEarnings - nonSalaryThreshold);
        var minimum = Rule(rules, PayrollRuleParameterCodes.MinimumMonthlySalary);
        var maximumBase = minimum * Rule(rules, PayrollRuleParameterCodes.MaximumContributionBaseMinimumWages);
        var contributionBase = input.IsIntegralSalary
            ? salaryEarnings * Rule(rules, PayrollRuleParameterCodes.IntegralSalaryContributionBaseRate)
            : salaryEarnings + excessNonSalary;
        contributionBase = Money(Math.Min(contributionBase, maximumBase));

        if (input.AppliesTransportAllowance && !input.IsIntegralSalary && input.MonthlySalary <= minimum * Rule(rules, PayrollRuleParameterCodes.TransportAllowanceSalaryLimitMultiple))
        {
            var allowance = Money(Rule(rules, PayrollRuleParameterCodes.TransportAllowance) / monthlyDays * workedDays);
            Add(lines, concepts[PayrollSystemConceptRoles.TransportAllowance], workedDays, null, null, allowance);
        }

        AddPercentage(lines, concepts[PayrollSystemConceptRoles.EmployeeHealth], contributionBase,
            Rule(rules, PayrollRuleParameterCodes.EmployeeHealthRate));
        AddPercentage(lines, concepts[PayrollSystemConceptRoles.EmployeePension], contributionBase,
            Rule(rules, PayrollRuleParameterCodes.EmployeePensionRate));
        if (!input.IsEmployerExemptFromHealthSenaIcbf)
            AddPercentage(lines, concepts[PayrollSystemConceptRoles.EmployerHealth], contributionBase,
                Rule(rules, PayrollRuleParameterCodes.EmployerHealthRate), true);
        AddPercentage(lines, concepts[PayrollSystemConceptRoles.EmployerPension], contributionBase,
            Rule(rules, PayrollRuleParameterCodes.EmployerPensionRate), true);
        AddPercentage(lines, concepts[PayrollSystemConceptRoles.OccupationalRisk], contributionBase, input.RiskRate, true);
        AddPercentage(lines, concepts[PayrollSystemConceptRoles.CompensationFund], contributionBase,
            Rule(rules, PayrollRuleParameterCodes.CompensationFundRate), true);
        if (!input.IsEmployerExemptFromHealthSenaIcbf)
        {
            AddPercentage(lines, concepts[PayrollSystemConceptRoles.Sena], contributionBase,
                Rule(rules, PayrollRuleParameterCodes.SenaRate), true);
            AddPercentage(lines, concepts[PayrollSystemConceptRoles.Icbf], contributionBase,
                Rule(rules, PayrollRuleParameterCodes.IcbfRate), true);
        }

        var benefitsBase = salaryEarnings + lines.Where(x => x.ConceptId == concepts[PayrollSystemConceptRoles.TransportAllowance].ConceptId).Sum(x => x.Amount);
        if (!input.IsIntegralSalary)
        {
            AddPercentage(lines, concepts[PayrollSystemConceptRoles.SeveranceProvision], benefitsBase,
                Rule(rules, PayrollRuleParameterCodes.SeveranceRate), true);
            var severance = lines.Last().Amount;
            AddPercentage(lines, concepts[PayrollSystemConceptRoles.SeveranceInterestProvision], severance,
                Rule(rules, PayrollRuleParameterCodes.SeveranceInterestRate), true);
            AddPercentage(lines, concepts[PayrollSystemConceptRoles.ServiceBonusProvision], benefitsBase,
                Rule(rules, PayrollRuleParameterCodes.ServiceBonusRate), true);
        }
        AddPercentage(lines, concepts[PayrollSystemConceptRoles.VacationProvision], salaryEarnings,
            Rule(rules, PayrollRuleParameterCodes.VacationRate), true);

        var earnings = Money(lines.Where(x => x.Nature == PayrollLineNature.Earning).Sum(x => x.Amount));
        var deductions = Money(lines.Where(x => x.Nature == PayrollLineNature.Deduction).Sum(x => x.Amount));
        if (deductions > earnings)
            throw new PayrollCalculationException("Las deducciones no pueden superar los devengados del período.");
        var protectedIds = input.Novelties.Where(novelty => novelty.MustProtectMinimumNetPay).Select(novelty => novelty.NoveltyId).ToHashSet();
        var protectedDeductions = Money(lines.Where(line => line.Nature == PayrollLineNature.Deduction &&
            line.NoveltyId.HasValue && protectedIds.Contains(line.NoveltyId.Value)).Sum(line => line.Amount));
        var statutoryDeductions = Money(lines
            .Where(x => x.Nature == PayrollLineNature.Deduction && x.NoveltyId is null)
            .Sum(x => x.Amount));
        var protectedMinimum = Money(minimum / monthlyDays * workedDays);
        if (protectedDeductions > 0 && earnings - statutoryDeductions - protectedDeductions < protectedMinimum)
            throw new PayrollCalculationException("Las deducciones autorizadas afectan el mínimo protegido del trabajador.");
        var employer = Money(lines.Where(x => x.Nature == PayrollLineNature.EmployerContribution).Sum(x => x.Amount));
        var provisions = Money(lines.Where(x => x.Nature == PayrollLineNature.Provision).Sum(x => x.Amount));
        var normalized = lines.Where(x => x.Amount != 0 || x.NoveltyId.HasValue).ToArray();
        return new PayrollEmployeeCalculation(input.EmploymentId, input.PartyId, workedDays,
            earnings, deductions, employer, provisions, Money(earnings - deductions), normalized, Hash(normalized)) { AppliesTransportAllowance = input.AppliesTransportAllowance, EmploymentVersion = input.EmploymentVersion };
    }

    public PayrollEmployeeCalculationInput WithFinalWorkedDays(PayrollEmployeeCalculationInput input,
        decimal workedDays, int periodDays)
    {
        if (periodDays <= 0 || workedDays < 0 || workedDays > periodDays || decimal.Round(workedDays, 4) != workedDays)
            throw new PayrollCalculationException("Los días trabajados deben estar dentro del período y admitir como máximo cuatro decimales.");
        var baseDays = workedDays + AbsenceDays(input);
        if (decimal.Round(baseDays, 4) != baseDays)
            throw new PayrollCalculationException("Las ausencias requieren ajustar su precisión antes de guardar días con cuatro decimales.");
        if (baseDays > periodDays)
            throw new PayrollCalculationException("Los días trabajados y las ausencias superan los días del período.");
        return input with { WorkedDays = baseDays };
    }

    private static bool IsAbsence(PayrollNoveltyInput novelty)
        => novelty.NoveltyTypeCode == PayrollNoveltyTypeCodes.Days && novelty.Concept.Nature == PayrollLineNature.Deduction;

    private static decimal AbsenceDays(PayrollEmployeeCalculationInput input)
        => input.Novelties.Where(IsAbsence).Sum(novelty => novelty.Quantity);

    private static int NoveltyStage(PayrollNoveltyInput novelty) => novelty.Concept.Nature != PayrollLineNature.Earning
        ? 2 : novelty.Concept.CalculationMethodCode == PayrollCalculationMethodCodes.PercentageOfBase ? 1 : 0;

    public static void ValidateRuleParameters(IReadOnlyDictionary<string, decimal> parameters)
    {
        var missing = RequiredRules.Where(code => !parameters.ContainsKey(code)).ToArray();
        if (missing.Length != 0)
            throw new PayrollCalculationException($"Faltan parámetros obligatorios: {string.Join(", ", missing)}.");
        if (RequiredRules.Any(code => parameters[code] < 0))
            throw new PayrollCalculationException("Los parámetros legales no pueden ser negativos.");
        if (parameters[PayrollRuleParameterCodes.MonthlyDays] <= 0)
            throw new PayrollCalculationException("El divisor de días del mes debe ser positivo.");
    }

    private static void Validate(PayrollEmployeeCalculationInput input)
    {
        if (input.EmploymentId == Guid.Empty || input.PartyId == Guid.Empty)
            throw new PayrollCalculationException("La relación laboral y la persona son obligatorias.");
        if (input.MonthlySalary <= 0 || input.WorkedDays < 0 || input.RiskRate < 0)
            throw new PayrollCalculationException("Salario, días o tarifa de riesgo no son válidos.");
        ValidateRuleParameters(input.RuleParameters);
        var missingConcepts = RequiredConceptRoles.Where(code => !input.SystemConcepts.ContainsKey(code)).ToArray();
        if (missingConcepts.Length != 0)
            throw new PayrollCalculationException($"Faltan conceptos de sistema: {string.Join(", ", missingConcepts)}.");
        if (input.WorkedDays > Rule(input.RuleParameters, PayrollRuleParameterCodes.MonthlyDays))
            throw new PayrollCalculationException("Los días reconocidos superan los días del período mensual.");
    }

    private static decimal Rule(IReadOnlyDictionary<string, decimal> rules, string code) => rules[code];

    private static decimal ResolveNoveltyAmount(PayrollNoveltyInput novelty,
        IReadOnlyCollection<PayrollCalculatedLine> currentLines) =>
        novelty.Concept.CalculationMethodCode switch
        {
            PayrollCalculationMethodCodes.FixedAmount => Money(novelty.TotalAmount),
            PayrollCalculationMethodCodes.QuantityByRate when novelty.UnitAmount is not null =>
                Money(novelty.Quantity * novelty.UnitAmount.Value),
            PayrollCalculationMethodCodes.PercentageOfBase when novelty.UnitAmount is not null =>
                Money(ResolvePercentageBase(novelty.Concept, currentLines) * novelty.UnitAmount.Value),
            PayrollCalculationMethodCodes.Statutory => Money(novelty.TotalAmount),
            PayrollCalculationMethodCodes.QuantityByRate => throw new PayrollCalculationException(
                $"El concepto '{novelty.Concept.Code}' requiere valor unitario."),
            PayrollCalculationMethodCodes.PercentageOfBase => throw new PayrollCalculationException(
                $"El concepto '{novelty.Concept.Code}' requiere una tarifa decimal."),
            _ => throw new PayrollCalculationException(
                $"El método de cálculo '{novelty.Concept.CalculationMethodCode}' no es compatible.")
        };

    private static decimal ResolvePercentageBase(PayrollConceptDefinition concept,
        IReadOnlyCollection<PayrollCalculatedLine> currentLines)
    {
        var eligible = currentLines.Where(x => x.Nature == PayrollLineNature.Earning);
        if (concept.IsTaxWithholdingBase)
            eligible = eligible.Where(x => x.IsSalaryBase);
        var basis = Money(eligible.Sum(x => x.Amount));
        if (basis <= 0)
            throw new PayrollCalculationException(
                $"El concepto '{concept.Code}' no tiene una base positiva para aplicar porcentaje.");
        return basis;
    }

    private static void AddPercentage(List<PayrollCalculatedLine> lines, PayrollConceptDefinition concept,
        decimal baseAmount, decimal rate, bool isEmployerCost = false) =>
        Add(lines, concept, 1m, null, rate, Money(baseAmount * rate), null, baseAmount, isEmployerCost);

    private static void Add(List<PayrollCalculatedLine> lines, PayrollConceptDefinition concept,
        decimal quantity, Guid? noveltyId, decimal? rate, decimal amount, Guid? agreementId = null,
        decimal? baseAmount = null, bool isEmployerCost = false) =>
        lines.Add(new PayrollCalculatedLine(concept.ConceptId, noveltyId, agreementId, concept.Code,
            concept.Name, concept.Nature, concept.AccountingCategoryCode, concept.DianConceptCode,
            quantity, rate, baseAmount is null ? null : Money(baseAmount.Value), amount,
            isEmployerCost, concept.IsSalaryBase));

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static byte[] Hash(IEnumerable<PayrollCalculatedLine> lines)
    {
        var canonical = string.Join("\n", lines.Select(x => string.Join("|", x.ConceptId.ToString("D"),
            x.NoveltyId?.ToString("D") ?? string.Empty, x.Nature, x.Quantity.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            x.Rate?.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            x.BaseAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            x.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            x.IsSalaryBase ? "1" : "0")));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}

public sealed class PayrollCalculationException(string message) : Exception(message);
