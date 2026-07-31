INSERT INTO dbo.DocumentProcessingPayloads
(
    DocumentId, DocumentType, BusinessId, ContractVersion,
    PayloadJson, PayloadHash, AcceptedAt
)
SELECT j.DocumentId, j.DocumentType, j.BusinessId, 1,
       s.SnapshotJson, s.PayloadHash, d.ReceivedAt
FROM dbo.DocumentProcessingJobs j
INNER JOIN dbo.SalesDocuments d ON d.DocumentId = j.DocumentId
INNER JOIN dbo.FiscalSnapshots s ON s.DocumentId = j.DocumentId
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.DocumentProcessingPayloads p
    WHERE p.DocumentId = j.DocumentId
      AND p.DocumentType = j.DocumentType
);

