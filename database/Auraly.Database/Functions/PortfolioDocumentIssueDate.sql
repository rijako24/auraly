-- The immutable document carries its issue date separately from acceptance or
-- posting time. Older source contracts without an explicit issue date use the
-- source event date. This pure projection performs no reads or writes.
CREATE FUNCTION accounting.PortfolioDocumentIssueDate(
    @PayloadJson nvarchar(max),@OccurredAt datetimeoffset(7))
RETURNS TABLE
AS RETURN
(
    SELECT COALESCE(
      TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@PayloadJson,'$.CommercialSnapshot.IssuedAt')),
      TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@PayloadJson,'$.commercialSnapshot.issuedAt')),
      TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@PayloadJson,'$.IssuedAt')),
      TRY_CONVERT(datetimeoffset(7),JSON_VALUE(@PayloadJson,'$.issuedAt')),
      @OccurredAt) IssuedAt
);
GO
