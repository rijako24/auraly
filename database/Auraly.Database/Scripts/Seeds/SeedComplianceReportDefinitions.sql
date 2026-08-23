SET NOCOUNT ON;

DECLARE @SourceUrl NVARCHAR(1000)=N'https://www.dian.gov.co/normatividad/Normatividad/Anexo%20162%20-%20Resoluci%C3%B3n%20000162%20de%202023.zip';
DECLARE @Schema NVARCHAR(MAX)=N'{"identityFields":["documentType","identification","verificationDigit","firstSurname","secondSurname","firstName","otherNames","legalName","address","departmentCode","cityCode","countryCode"],"valueModel":"concept-and-target-field","artifact":"audit-csv-not-filing-xml"}';
DECLARE @Definitions TABLE(FormatCode NVARCHAR(24),FormatVersion SMALLINT,Name NVARCHAR(240),Annex NVARCHAR(80),Hash CHAR(64));
INSERT @Definitions VALUES
 (N'1001',10,N'Pagos o abonos en cuenta y retenciones practicadas',N'Anexo 18',N'A51795469AF0A7620FE75BBE0AFDF7A0F55B0630399EA7F7302CFDBAC8DA4EF0'),
 (N'1003',7,N'Retenciones en la fuente que le practicaron',N'Anexo 19',N'7591379070BF49CC018644002C4B38A57ECDC61E0FA51E75A9E57989EB3EFF85'),
 (N'1007',9,N'Ingresos recibidos',N'Anexo 20',N'28E90DD5CB976D4917AB9B286DF484442146BBD2C4AA827E27AA341F475788F2'),
 (N'1005',8,N'Impuesto a las ventas por pagar (descontable)',N'Anexo 21 modificado por Resolución 000188/2024',N'22529F661D8A4237620DB7BA792A53C7FC1F42D79C3217136A95758C9638E76C'),
 (N'1006',8,N'IVA generado e impuesto nacional al consumo',N'Anexo 22',N'817E651634C8ECA2FE77C9114F4C7C51EC31D3CAFD9811B5C81D8A5EFBDA6240'),
 (N'1009',7,N'Saldos de cuentas por pagar al 31 de diciembre',N'Anexo 23',N'BF688B618C1BEA6E27741B076C4CE5C8C33F9D350C641796D2E0B37A73BF5A7C'),
 (N'1008',7,N'Saldos de cuentas por cobrar al 31 de diciembre',N'Anexo 24',N'D767A4FFA8887E42807E9C27A5C9D828A7F2BDB304995CF8FE788F9FC1F6430B');

MERGE compliance.ComplianceReportDefinitions AS target
USING (SELECT N'DIAN' AuthorityCode,CAST(2025 AS SMALLINT) TaxYear,d.* FROM @Definitions d) AS source
ON target.AuthorityCode=source.AuthorityCode AND target.TaxYear=source.TaxYear
AND target.FormatCode=source.FormatCode AND target.FormatVersion=source.FormatVersion
WHEN MATCHED THEN UPDATE SET Name=source.Name,ReportKind=N'Exogenous',ResolutionNumber=N'000162/2023 + 000188/2024',ResolutionDate='2024-10-30',TechnicalAnnex=source.Annex,SourceUrl=@SourceUrl,SourceSha256=source.Hash,SchemaJson=@Schema,IsActive=1
WHEN NOT MATCHED THEN INSERT(AuthorityCode,TaxYear,FormatCode,FormatVersion,Name,ReportKind,ResolutionNumber,ResolutionDate,TechnicalAnnex,SourceUrl,SourceSha256,SchemaJson,IsActive,CreatedAt)
VALUES(source.AuthorityCode,source.TaxYear,source.FormatCode,source.FormatVersion,source.Name,N'Exogenous',N'000162/2023 + 000188/2024','2024-10-30',source.Annex,@SourceUrl,source.Hash,@Schema,1,SYSUTCDATETIME());

DECLARE @Definitions2026 TABLE(FormatCode NVARCHAR(24),FormatVersion SMALLINT,Name NVARCHAR(240),Annex NVARCHAR(80),SourceUrl NVARCHAR(1000),Hash CHAR(64));
INSERT @Definitions2026 VALUES
 (N'1001',11,N'Pagos o abonos en cuenta y retenciones practicadas',N'T3.18',N'https://normograma.com/documentospdf/PDF/R_DIAN_0233_2025_ANEXO_T3.18.pdf',N'068341070FE16716CC91CDCF9AE015DBB9476970C0B91E8911DDD231B64AFADD'),
 (N'1003',7,N'Retenciones en la fuente que le practicaron',N'T3.19',N'https://normograma.com/documentospdf/PDF/R_DIAN_0227_2025_ANEXOT3.19.pdf',N'432E4F6C18487B282E8F37713E8B72DAA984F1418ABE882BA51156A135179069'),
 (N'1007',9,N'Ingresos recibidos',N'T3.20',N'https://normograma.com/documentospdf/PDF/R_DIAN_0227_2025_ANEXOT3.20.pdf',N'0926ACEF5DDC980EC852276D9500CC23A14C72F6C4E0E401248792AC667EDAEA'),
 (N'1005',9,N'Impuesto a las ventas por pagar (descontable)',N'T3.21',N'https://normograma.com/documentospdf/PDF/R_DIAN_0233_2025_ANEXO_T3.21.pdf',N'254A6A83547E7A66C1D38FD46FB07A2C0EF84FC1F5B28E5F8441127E632F2E3C'),
 (N'1006',8,N'IVA generado e impuesto nacional al consumo',N'T3.22',N'https://normograma.com/documentospdf/PDF/R_DIAN_0227_2025_ANEXOT3.22.pdf',N'919A4D8CF30C71FE4F9940C38E80A5E7B1E2A80AE82B69FD930300888D7434B4'),
 (N'1009',7,N'Saldos de cuentas por pagar al 31 de diciembre',N'T3.23',N'https://normograma.com/documentospdf/PDF/R_DIAN_0227_2025_ANEXOT3.23.pdf',N'9B28FAB660783A973D27602B480F1793F156526034F112BDF2BC5FF806B9DEED'),
 (N'1008',7,N'Saldos de cuentas por cobrar al 31 de diciembre',N'T3.24',N'https://normograma.com/documentospdf/PDF/R_DIAN_0227_2025_ANEXOT3.24.pdf',N'73E01496B63AE506C274C224FFFD0EDF8E19DFEFF744796225F23C7772E46F03');
MERGE compliance.ComplianceReportDefinitions AS target
USING (SELECT N'DIAN' AuthorityCode,CAST(2026 AS SMALLINT) TaxYear,d.* FROM @Definitions2026 d) AS source
ON target.AuthorityCode=source.AuthorityCode AND target.TaxYear=source.TaxYear
AND target.FormatCode=source.FormatCode AND target.FormatVersion=source.FormatVersion
WHEN MATCHED THEN UPDATE SET Name=source.Name,ReportKind=N'Exogenous',ResolutionNumber=N'000227/2025 + 000233/2025 + 000237/2025',ResolutionDate='2025-12-03',TechnicalAnnex=source.Annex,SourceUrl=source.SourceUrl,SourceSha256=source.Hash,SchemaJson=@Schema,IsActive=1
WHEN NOT MATCHED THEN INSERT(AuthorityCode,TaxYear,FormatCode,FormatVersion,Name,ReportKind,ResolutionNumber,ResolutionDate,TechnicalAnnex,SourceUrl,SourceSha256,SchemaJson,IsActive,CreatedAt)
VALUES(source.AuthorityCode,source.TaxYear,source.FormatCode,source.FormatVersion,source.Name,N'Exogenous',N'000227/2025 + 000233/2025 + 000237/2025','2025-12-03',source.Annex,source.SourceUrl,source.Hash,@Schema,1,SYSUTCDATETIME());

DECLARE @Fiscal TABLE(FormatCode NVARCHAR(24),Name NVARCHAR(240));
INSERT @Fiscal VALUES
 (N'IVA',N'Libro fiscal de IVA por tarifa y tratamiento'),
 (N'RETENCIONES',N'Retenciones practicadas y sufridas'),
 (N'ICA',N'ICA y reteICA por jurisdicción'),
 (N'FORM-300',N'Borrador de conciliación para declaración de IVA'),
 (N'FORM-350',N'Borrador de conciliación para retenciones en la fuente'),
 (N'FORM-310',N'Borrador de conciliación para impuesto nacional al consumo'),
 (N'FORM-2516',N'Base de conciliación fiscal 2516'),
 (N'FORM-2517',N'Base de conciliación fiscal 2517');
DECLARE @FiscalYears TABLE(TaxYear SMALLINT,ResolutionNumber NVARCHAR(80),ResolutionDate DATE,SourceUrl NVARCHAR(1000),Hash CHAR(64));
INSERT @FiscalYears VALUES
 (2025,N'Base interna; marco 000162/2023 + 000188/2024','2024-10-30',@SourceUrl,N'83490FA2D8376D17EC0F3C01D90F9F15370EE7E4ED1B05A01388C9AAB6A4FD1C'),
 (2026,N'Base interna; marco 000227/2025 + 000233/2025','2025-10-30',N'https://normograma.dian.gov.co/dian/compilacion/docs/pdf/resolucion_dian_0227_2025.pdf',N'AE8B65A40CF31C1A6D427F4A880C46F5A048DF198C9487C86B22E081C264FEBE');
MERGE compliance.ComplianceReportDefinitions AS target
USING (SELECT N'DIAN' AuthorityCode,y.*,f.FormatCode,f.Name FROM @Fiscal f CROSS JOIN @FiscalYears y) AS source
ON target.AuthorityCode=source.AuthorityCode AND target.TaxYear=source.TaxYear
AND target.FormatCode=source.FormatCode AND target.FormatVersion=1
WHEN MATCHED THEN UPDATE SET Name=source.Name,ResolutionNumber=source.ResolutionNumber,ResolutionDate=source.ResolutionDate,SourceUrl=source.SourceUrl,SourceSha256=source.Hash,IsActive=1
WHEN NOT MATCHED THEN INSERT(AuthorityCode,TaxYear,FormatCode,FormatVersion,Name,ReportKind,ResolutionNumber,ResolutionDate,TechnicalAnnex,SourceUrl,SourceSha256,SchemaJson,IsActive,CreatedAt)
VALUES(source.AuthorityCode,source.TaxYear,source.FormatCode,1,source.Name,N'FiscalDraft',source.ResolutionNumber,source.ResolutionDate,N'No aplica',source.SourceUrl,source.Hash,@Schema,1,SYSUTCDATETIME());
