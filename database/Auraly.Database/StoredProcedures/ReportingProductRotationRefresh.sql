CREATE PROCEDURE [reporting].[ProductRotationRefresh]
    @BusinessId UNIQUEIDENTIFIER,
    @SourceDocumentId UNIQUEIDENTIFIER,
    @SourceDocumentType NVARCHAR(32),
    @EndDate DATE,
    @ProjectionVersion SMALLINT,
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    WITH affected AS
    (
        SELECT DISTINCT fact.ProductId,fact.WarehouseId,
            CASE WHEN snapshot.WindowEndDate>@EndDate THEN snapshot.WindowEndDate ELSE @EndDate END WindowEndDate
        FROM reporting.SalesReportLineFacts fact
        LEFT JOIN reporting.ProductRotationSnapshots snapshot
          ON snapshot.BusinessId=fact.BusinessId AND snapshot.WarehouseId=fact.WarehouseId AND snapshot.ProductId=fact.ProductId
        WHERE fact.BusinessId=@BusinessId AND fact.SourceDocumentId=@SourceDocumentId
          AND fact.SourceDocumentType=@SourceDocumentType
    ), rotation AS
    (
        SELECT @BusinessId BusinessId,a.WarehouseId,a.ProductId,a.WindowEndDate,
            SUM(CASE WHEN f.BusinessLocalDate>=DATEADD(day,-29,a.WindowEndDate) AND f.Quantity>0 THEN f.Quantity ELSE 0 END) Gross30,
            SUM(CASE WHEN f.BusinessLocalDate>=DATEADD(day,-29,a.WindowEndDate) AND f.Quantity<0 THEN -f.Quantity ELSE 0 END) Returned30,
            SUM(CASE WHEN f.BusinessLocalDate>=DATEADD(day,-29,a.WindowEndDate) THEN f.Quantity ELSE 0 END) Net30,
            SUM(CASE WHEN f.Quantity>0 THEN f.Quantity ELSE 0 END) Gross90,
            SUM(CASE WHEN f.Quantity<0 THEN -f.Quantity ELSE 0 END) Returned90,
            SUM(f.Quantity) Net90
        FROM affected a
        LEFT JOIN reporting.SalesReportLineFacts f
          ON f.BusinessId=@BusinessId AND f.WarehouseId=a.WarehouseId AND f.ProductId=a.ProductId
         AND f.BusinessLocalDate BETWEEN DATEADD(day,-89,a.WindowEndDate) AND a.WindowEndDate
        GROUP BY a.WarehouseId,a.ProductId,a.WindowEndDate
    )
    MERGE reporting.ProductRotationSnapshots WITH(HOLDLOCK) AS target
    USING rotation source
      ON target.BusinessId=source.BusinessId AND target.WarehouseId=source.WarehouseId AND target.ProductId=source.ProductId
    WHEN MATCHED THEN UPDATE SET WindowEndDate=source.WindowEndDate,
      GrossUnitsSold30Days=source.Gross30,ReturnedUnits30Days=source.Returned30,NetUnitsSold30Days=source.Net30,
      GrossUnitsSold90Days=source.Gross90,ReturnedUnits90Days=source.Returned90,NetUnitsSold90Days=source.Net90,
      DailyDemand90Days=CASE WHEN source.Net90>0 THEN source.Net90/90 ELSE 0 END,
      ProjectionVersion=@ProjectionVersion,CalculatedAt=@Now
    WHEN NOT MATCHED THEN INSERT
      (BusinessId,WarehouseId,ProductId,WindowEndDate,GrossUnitsSold30Days,ReturnedUnits30Days,
       NetUnitsSold30Days,GrossUnitsSold90Days,ReturnedUnits90Days,NetUnitsSold90Days,DailyDemand90Days,
       ProjectionVersion,CalculatedAt)
    VALUES(source.BusinessId,source.WarehouseId,source.ProductId,source.WindowEndDate,source.Gross30,source.Returned30,
      source.Net30,source.Gross90,source.Returned90,source.Net90,CASE WHEN source.Net90>0 THEN source.Net90/90 ELSE 0 END,
      @ProjectionVersion,@Now);
END;
