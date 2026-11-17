using Auraly.Commerce.Payroll.Contracts;

namespace Auraly.Foundation.Tests;

public sealed class PayrollAccountingContractTests
{
    [Fact]
    public void Existing_flat_source_keeps_its_exact_serialized_hash_input()
    {
        const string original="""{"tenantId":"11111111-1111-1111-1111-111111111111","businessId":"22222222-2222-2222-2222-222222222222","payrollRunId":"33333333-3333-3333-3333-333333333333","runKind":"Regular","periodStart":"2026-10-01","periodEnd":"2026-10-31","paymentDate":"2026-10-31","description":"Nomina","lines":[{"category":"SalaryExpense","debit":100,"credit":0,"partyId":"44444444-4444-4444-4444-444444444444","description":"Salario"}]}""";
        var source=PayrollContractSerializer.DeserializeAccounting(original);
        Assert.Null(source.Employees);Assert.Null(source.Concepts);
        Assert.Equal(original,PayrollContractSerializer.Serialize(source));
    }

    [Fact]
    public void Classification_is_shared_without_reordering_or_losing_financial_lines()
    {
        var worker=Guid.NewGuid();var profile=Guid.NewGuid();var concept=Guid.NewGuid();var party=Guid.NewGuid();
        var source=new PayrollAccountingPayload(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Regular",
            new(2026,10,1),new(2026,10,31),new(2026,10,31),"Nomina",
            [new("SalaryExpense",100,0,party,null,0,0),new("NetPayrollPayable",0,100,party,null,0,1)])
        {
            Employees=[new(worker,profile,null)],Concepts=[new(concept,"Salario"),new(null,"Neto")]
        };
        var result=PayrollContractSerializer.DeserializeAccounting(PayrollContractSerializer.Serialize(source));
        Assert.Equal(worker,Assert.Single(result.Employees!).EmploymentId);
        Assert.Equal(profile,result.Employees![0].ProfileId);
        Assert.Equal(100,result.Lines[0].Debit);Assert.Equal(100,result.Lines[1].Credit);
        Assert.Equal(party,result.Lines[0].PartyId);Assert.Equal(party,result.Lines[1].PartyId);
        Assert.Equal("Salario",result.Concepts![result.Lines[0].ConceptIndex!.Value].Description);
        Assert.Equal("Neto",result.Concepts[result.Lines[1].ConceptIndex!.Value].Description);
    }
}
