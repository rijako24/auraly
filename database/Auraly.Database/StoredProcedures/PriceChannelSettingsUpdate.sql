CREATE PROCEDURE dbo.PriceChannelSettingsUpdate
    @BusinessId UNIQUEIDENTIFIER,
    @Id UNIQUEIDENTIFIER,
    @Name NVARCHAR(120),
    @Strategy NVARCHAR(48),
    @Value DECIMAL(19, 6) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId UNIQUEIDENTIFIER=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
    IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Id AND TenantId=@TenantId)
        THROW 51004, 'Segment not found', 1;
    UPDATE dbo.PriceChannels
    SET Name = @Name,
        Strategy = @Strategy,
        Value = @Value
    WHERE PriceChannelId = @Id
      AND TenantId = @TenantId;

    IF @@ROWCOUNT = 0
        THROW 51004, 'Segment not found', 1;
END
