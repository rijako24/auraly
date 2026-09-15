SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Orders',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Customers',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PartySites',N'U') IS NOT NULL
BEGIN
    UPDATE orders
    SET PartySiteId=site.PartySiteId,
        UpdatedAt=COALESCE(orders.UpdatedAt,SYSUTCDATETIME())
    FROM dbo.Orders orders
    INNER JOIN dbo.Customers customer
      ON customer.CustomerId=orders.CustomerId
     AND customer.BusinessId=orders.BusinessId
    CROSS APPLY(
      SELECT TOP(1) candidate.PartySiteId
      FROM dbo.PartySites candidate
      WHERE candidate.PartyId=customer.PartyId AND candidate.IsActive=1
      ORDER BY candidate.IsPrimary DESC,
               candidate.CreatedAt,candidate.PartySiteId) site
    WHERE orders.CustomerId IS NOT NULL AND orders.PartySiteId IS NULL;

    IF EXISTS(
      SELECT 1
      FROM dbo.Orders orders
      WHERE (orders.CustomerId IS NULL AND orders.PartySiteId IS NOT NULL)
         OR (orders.CustomerId IS NOT NULL AND orders.PartySiteId IS NULL))
      THROW 51320,'No se puede exigir la sede: existe un pedido con cliente y sede incompletos.',1;

    IF EXISTS(
      SELECT 1
      FROM dbo.Orders orders
      LEFT JOIN dbo.Customers customer
        ON customer.CustomerId=orders.CustomerId
       AND customer.BusinessId=orders.BusinessId
      LEFT JOIN dbo.PartySites site
        ON site.PartySiteId=orders.PartySiteId
       AND site.PartyId=customer.PartyId
       AND site.IsActive=1
      WHERE orders.CustomerId IS NOT NULL
        AND (customer.CustomerId IS NULL OR site.PartySiteId IS NULL))
      THROW 51321,'No se puede exigir la sede: existe un pedido cuya sede no pertenece al cliente.',1;

    IF OBJECT_ID(N'reporting.SalesReportingJobs',N'U') IS NOT NULL
    BEGIN
        -- SQL Server 2019+ can hash the same UTF-8 representation used by the
        -- application. Supported local SQL Server 2017 instances do not expose
        -- UTF-8 collations, so hash the patched Unicode payload bytes there.
        -- The reporting processor treats this job payload and hash as one
        -- immutable pair; no comparison with a separately encoded payload occurs.
        IF EXISTS(
          SELECT 1 FROM sys.fn_helpcollations()
          WHERE name=N'Latin1_General_100_BIN2_UTF8')
          EXEC sys.sp_executesql N'
            UPDATE job
            SET SourcePayloadJson=patched.PayloadJson,
                SourcePayloadHash=HASHBYTES(''SHA2_256'',CONVERT(varchar(max),
                  patched.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
            FROM reporting.SalesReportingJobs job
            JOIN dbo.Orders orders
              ON orders.OrderId=job.SourceDocumentId
             AND orders.BusinessId=job.BusinessId
            CROSS APPLY(
              SELECT JSON_MODIFY(job.SourcePayloadJson,''$.partySiteId'',
                       CONVERT(nvarchar(36),orders.PartySiteId)) PayloadJson) patched
            WHERE job.SourceDocumentType=N''SellerOrder''
              AND job.Status IN(N''Pending'',N''Failed'')
              AND orders.PartySiteId IS NOT NULL
              AND JSON_VALUE(job.SourcePayloadJson,''$.partySiteId'') IS NULL;';
        ELSE
          UPDATE job
          SET SourcePayloadJson=patched.PayloadJson,
              SourcePayloadHash=HASHBYTES('SHA2_256',CONVERT(varbinary(max),
                patched.PayloadJson))
          FROM reporting.SalesReportingJobs job
          JOIN dbo.Orders orders
            ON orders.OrderId=job.SourceDocumentId
           AND orders.BusinessId=job.BusinessId
          CROSS APPLY(
            SELECT JSON_MODIFY(job.SourcePayloadJson,'$.partySiteId',
                     CONVERT(nvarchar(36),orders.PartySiteId)) PayloadJson) patched
          WHERE job.SourceDocumentType=N'SellerOrder'
            AND job.Status IN(N'Pending',N'Failed')
            AND orders.PartySiteId IS NOT NULL
            AND JSON_VALUE(job.SourcePayloadJson,'$.partySiteId') IS NULL;
    END;
END;

-- This reviewed pre-DACPAC migration also creates the nullable columns when an
-- older environment has not received them yet.  The DACPAC remains the schema
-- owner and adds the FKs/check constraints after every historical row is valid.
IF OBJECT_ID(N'dbo.SalesDrafts',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDrafts',N'CustomerPartySiteId') IS NULL
    EXEC(N'ALTER TABLE dbo.SalesDrafts ADD CustomerPartySiteId UNIQUEIDENTIFIER NULL;');

IF OBJECT_ID(N'dbo.SalesDocuments',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SalesDocuments',N'CustomerPartySiteId') IS NULL
    EXEC(N'ALTER TABLE dbo.SalesDocuments ADD CustomerPartySiteId UNIQUEIDENTIFIER NULL;');

IF OBJECT_ID(N'dbo.Receivables',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Receivables',N'PartySiteId') IS NULL
    EXEC(N'ALTER TABLE dbo.Receivables ADD PartySiteId UNIQUEIDENTIFIER NULL;');

IF OBJECT_ID(N'dbo.SalesDrafts',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Customers',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PartySites',N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
      UPDATE draft
      SET CustomerPartySiteId=site.PartySiteId,
          UpdatedAt=COALESCE(draft.UpdatedAt,SYSUTCDATETIME())
      FROM dbo.SalesDrafts draft
      INNER JOIN dbo.Customers customer
        ON customer.CustomerId=draft.CustomerId
       AND customer.BusinessId=draft.BusinessId
      CROSS APPLY(
        SELECT TOP(1) candidate.PartySiteId
        FROM dbo.PartySites candidate
        WHERE candidate.PartyId=customer.PartyId AND candidate.IsActive=1
        ORDER BY candidate.IsPrimary DESC,
                 candidate.CreatedAt,candidate.PartySiteId) site
      WHERE draft.CustomerId IS NOT NULL
        AND draft.CustomerPartySiteId IS NULL;

      IF EXISTS(
        SELECT 1
        FROM dbo.SalesDrafts draft
        LEFT JOIN dbo.Customers customer
          ON customer.CustomerId=draft.CustomerId
         AND customer.BusinessId=draft.BusinessId
        LEFT JOIN dbo.PartySites site
          ON site.PartySiteId=draft.CustomerPartySiteId
         AND site.PartyId=customer.PartyId
         AND site.IsActive=1
        WHERE (draft.CustomerId IS NULL AND draft.CustomerPartySiteId IS NOT NULL)
           OR (draft.CustomerId IS NOT NULL
               AND (customer.CustomerId IS NULL OR site.PartySiteId IS NULL)))
        THROW 51325,''No se puede exigir la sede: existe un borrador con cliente y sede inválidos.'',1;';
END;

IF OBJECT_ID(N'dbo.SalesDocuments',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Customers',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PartySites',N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
      IF OBJECT_ID(N''dbo.OrderInvoiceLinks'',N''U'') IS NOT NULL
         AND OBJECT_ID(N''dbo.Orders'',N''U'') IS NOT NULL
      BEGIN
        UPDATE document
        SET CustomerPartySiteId=orders.PartySiteId
        FROM dbo.SalesDocuments document
        INNER JOIN dbo.OrderInvoiceLinks link
          ON link.DocumentId=document.DocumentId
         AND link.BusinessId=document.BusinessId
        INNER JOIN dbo.Orders orders
          ON orders.OrderId=link.OrderId
         AND orders.BusinessId=document.BusinessId
         AND orders.CustomerId=document.CustomerId
        WHERE document.CustomerPartySiteId IS NULL
          AND orders.PartySiteId IS NOT NULL;
      END;

      UPDATE document
      SET CustomerPartySiteId=site.PartySiteId
      FROM dbo.SalesDocuments document
      INNER JOIN dbo.Customers customer
        ON customer.CustomerId=document.CustomerId
       AND customer.BusinessId=document.BusinessId
      CROSS APPLY(
        SELECT TOP(1) candidate.PartySiteId
        FROM dbo.PartySites candidate
        WHERE candidate.PartyId=customer.PartyId AND candidate.IsActive=1
        ORDER BY candidate.IsPrimary DESC,
                 candidate.CreatedAt,candidate.PartySiteId) site
      WHERE document.CustomerId IS NOT NULL
        AND document.CustomerPartySiteId IS NULL;

      IF EXISTS(
        SELECT 1
        FROM dbo.SalesDocuments document
        LEFT JOIN dbo.Customers customer
          ON customer.CustomerId=document.CustomerId
         AND customer.BusinessId=document.BusinessId
        LEFT JOIN dbo.PartySites site
          ON site.PartySiteId=document.CustomerPartySiteId
         AND site.PartyId=customer.PartyId
         AND site.IsActive=1
        WHERE (document.CustomerId IS NULL AND document.CustomerPartySiteId IS NOT NULL)
           OR (document.CustomerId IS NOT NULL
               AND (customer.CustomerId IS NULL OR site.PartySiteId IS NULL)))
        THROW 51326,''No se puede exigir la sede: existe una factura con cliente y sede inválidos.'',1;';
END;

IF OBJECT_ID(N'dbo.Receivables',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Customers',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PartySites',N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
      IF OBJECT_ID(N''dbo.SalesDocuments'',N''U'') IS NOT NULL
      BEGIN
        UPDATE receivable
        SET PartySiteId=document.CustomerPartySiteId
        FROM dbo.Receivables receivable
        INNER JOIN dbo.SalesDocuments document
          ON document.DocumentId=receivable.SourceDocumentId
         AND document.BusinessId=receivable.BusinessId
         AND document.CustomerId=receivable.CustomerId
        WHERE receivable.PartySiteId IS NULL
          AND document.CustomerPartySiteId IS NOT NULL;
      END;

      UPDATE receivable
      SET PartySiteId=site.PartySiteId
      FROM dbo.Receivables receivable
      INNER JOIN dbo.Customers customer
        ON customer.CustomerId=receivable.CustomerId
       AND customer.BusinessId=receivable.BusinessId
      CROSS APPLY(
        SELECT TOP(1) candidate.PartySiteId
        FROM dbo.PartySites candidate
        WHERE candidate.PartyId=customer.PartyId AND candidate.IsActive=1
        ORDER BY candidate.IsPrimary DESC,
                 candidate.CreatedAt,candidate.PartySiteId) site
      WHERE receivable.PartySiteId IS NULL;

      IF EXISTS(
        SELECT 1
        FROM dbo.Receivables receivable
        LEFT JOIN dbo.Customers customer
          ON customer.CustomerId=receivable.CustomerId
         AND customer.BusinessId=receivable.BusinessId
        LEFT JOIN dbo.PartySites site
          ON site.PartySiteId=receivable.PartySiteId
         AND site.PartyId=customer.PartyId
         AND site.IsActive=1
        WHERE customer.CustomerId IS NULL OR site.PartySiteId IS NULL)
        THROW 51327,''No se puede exigir la sede: existe una cartera con cliente y sede inválidos.'',1;';
END;

COMMIT TRANSACTION;
GO
