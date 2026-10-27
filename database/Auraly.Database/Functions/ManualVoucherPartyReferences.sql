CREATE FUNCTION accounting.ManualVoucherPartyReferences(@PayloadJson nvarchar(max))
RETURNS TABLE
AS RETURN
(
    SELECT PartyId FROM
    (
        SELECT CONVERT(uniqueidentifier,JSON_VALUE(@PayloadJson,'$.MainPartyId')) PartyId
        UNION
        SELECT PartyId FROM OPENJSON(@PayloadJson,'$.Lines')
            WITH(PartyId uniqueidentifier '$.PartyId')
    ) parties
    WHERE PartyId IS NOT NULL
);
GO
