using Auraly.Domain.Inventory;

namespace Auraly.Foundation.Tests;

public sealed class InventoryOriginAttributionTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026,10,25,12,0,0,TimeSpan.Zero);
    private static InventoryOriginPortion Origin(decimal quantity,int age=0,bool known=true) =>
        new(Guid.NewGuid(),known?Guid.NewGuid():null,RecordedAt.AddMinutes(age),quantity);
    private static InventoryOriginPortion Unknown => Origin(0,known:false);

    [Fact]
    public void Issues_consume_the_oldest_entries_without_changing_the_valuation_policy()
    {
        var older=Origin(5); var newer=Origin(8,1);
        var result=InventoryOriginAttribution.Apply(13,-7,[newer,older],null,Unknown);
        Assert.Equal(newer with { Quantity=6 },Assert.Single(result.Available));
        Assert.Equal([older with { Quantity=-5 },newer with { Quantity=-2 }],result.Movement);
    }

    [Fact]
    public void Entries_recorded_in_the_same_instant_follow_their_original_line_order()
    {
        var older=Origin(5) with { SourceLineNumber=1,OriginId=Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
        var newer=Origin(8) with { SourceLineNumber=2,OriginId=Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var result=InventoryOriginAttribution.Apply(13,-7,[newer,older],null,Unknown);
        Assert.Equal(newer with { Quantity=6 },Assert.Single(result.Available));
    }

    [Fact]
    public void Entries_in_the_same_instant_keep_document_sequence_before_line_number()
    {
        var older=Origin(5) with { SourceProcessingSequence=1,SourceLineNumber=2 };
        var newer=Origin(8) with { SourceProcessingSequence=2,SourceLineNumber=1 };
        var result=InventoryOriginAttribution.Apply(13,-7,[newer,older],null,Unknown);
        Assert.Equal(newer with { Quantity=6 },Assert.Single(result.Available));
    }

    [Fact]
    public void Historical_unattributed_stock_is_consumed_before_assigning_a_new_receipt()
    {
        var unknown=Origin(3,1,false); var receipt=Origin(5);
        var result=InventoryOriginAttribution.Apply(8,-4,[receipt,unknown],null,Unknown);
        Assert.Equal(receipt with { Quantity=4 },Assert.Single(result.Available));
        Assert.Null(result.Movement[0].SourceMovementId);
    }

    [Fact]
    public void A_receipt_covering_negative_stock_retains_only_the_currently_available_portion()
    {
        var receipt=Origin(10);
        var result=InventoryOriginAttribution.Apply(-6,10,[],[receipt],Unknown);
        Assert.Equal(receipt with { Quantity=4 },Assert.Single(result.Available));
        Assert.Equal(receipt,Assert.Single(result.Movement));
    }

    [Fact]
    public void A_receipt_that_only_covers_negative_stock_has_no_remaining_origin()
    {
        var result=InventoryOriginAttribution.Apply(-10,6,[],[Origin(6)],Unknown);
        Assert.Empty(result.Available);
        Assert.Equal(6,result.Movement.Sum(portion=>portion.Quantity));
    }

    [Fact]
    public void An_issue_crossing_zero_records_the_unbacked_quantity_without_inventing_a_receipt()
    {
        var origin=Origin(2);
        var result=InventoryOriginAttribution.Apply(2,-5,[origin],null,Unknown);
        Assert.Empty(result.Available);
        Assert.Equal(-2,result.Movement[0].Quantity);
        Assert.Equal(-3,result.Movement[1].Quantity);
        Assert.Null(result.Movement[1].SourceMovementId);
    }

    [Fact]
    public void A_transfer_preserves_the_source_origins_and_their_original_order()
    {
        var older=Origin(5); var newer=Origin(4,1);
        var issue=InventoryOriginAttribution.Apply(9,-7,[older,newer],null,Unknown);
        var receipt=InventoryOriginAttribution.Apply(0,7,[],
            issue.Movement.Select(portion=>portion with { Quantity=-portion.Quantity }).ToArray(),Unknown);
        var destinationIssue=InventoryOriginAttribution.Apply(7,-6,receipt.Available,null,Unknown);
        Assert.Equal(newer with { Quantity=1 },Assert.Single(destinationIssue.Available));
        Assert.Equal(older.OriginId,destinationIssue.Movement[0].OriginId);
    }

    [Fact]
    public void A_return_merges_the_same_origin_and_keeps_its_source_identity()
    {
        var origin=Origin(3);
        var result=InventoryOriginAttribution.Apply(3,2,[origin],[origin with { Quantity=2 }],Unknown);
        Assert.Equal(origin with { Quantity=5 },Assert.Single(result.Available));
    }

    [Fact]
    public void Production_consumption_does_not_leave_the_ingredient_available_for_a_later_charge()
    {
        var ingredient=Origin(100);
        var consumed=InventoryOriginAttribution.Apply(100,-60,[ingredient],null,Unknown);
        Assert.Equal(40,Assert.Single(consumed.Available).Quantity);
        var finishedProduct=Origin(30);
        var output=InventoryOriginAttribution.Apply(0,30,[],[finishedProduct],Unknown);
        Assert.DoesNotContain(output.Available,portion=>portion.OriginId==ingredient.OriginId);
    }

    [Fact]
    public void Quantity_zero_does_not_replace_the_origin_detail()
    {
        InventoryOriginPortion[] portions=[Origin(4)];
        var result=InventoryOriginAttribution.Apply(4,0,portions,null,Unknown);
        Assert.Same(portions,result.Available);
        Assert.Empty(result.Movement);
    }

    [Fact]
    public void Inconsistent_current_detail_is_an_error_instead_of_a_synthetic_stock_balance()
    {
        Assert.Throws<InvalidOperationException>(()=>InventoryOriginAttribution.Apply(6,-2,[Origin(5)],null,Unknown));
        Assert.Throws<InvalidOperationException>(()=>InventoryOriginAttribution.Apply(0,6,[],[Origin(5)],Unknown));
    }

    [Fact]
    public void Conflicting_source_facts_and_duplicate_origins_are_rejected()
    {
        var origin=Origin(3);
        Assert.Throws<InvalidOperationException>(()=>InventoryOriginAttribution.Apply(3,2,[origin],
            [origin with { Quantity=2,SourceMovementId=Guid.NewGuid() }],Unknown));
        Assert.Throws<ArgumentException>(()=>InventoryOriginAttribution.Apply(6,-1,[origin,origin],null,Unknown));
        Assert.Throws<ArgumentException>(()=>InventoryOriginAttribution.Apply(0,1.0000001m,[],[Origin(1.0000001m)],Unknown));
    }
}
