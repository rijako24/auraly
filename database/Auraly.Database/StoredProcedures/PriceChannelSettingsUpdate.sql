CREATE PROCEDURE dbo.PriceChannelSettingsUpdate
    @BusinessId UNIQUEIDENTIFIER,
    @Id UNIQUEIDENTIFIER,
    @Name NVARCHAR(120),
    @Strategy NVARCHAR(48),
    @Value DECIMAL(19, 6) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PriceChannels
    SET Name = @Name,
        Strategy = @Strategy,
        Value = @Value
    WHERE PriceChannelId = @Id
      AND BusinessId = @BusinessId;

    IF @@ROWCOUNT = 0
        THROW 51004, 'Segment not found', 1;
END
