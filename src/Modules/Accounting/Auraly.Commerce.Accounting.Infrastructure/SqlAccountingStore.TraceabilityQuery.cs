namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    private const string TraceabilityDraftPostingSql = """
            SELECT DocumentId SourceDocumentId,DocumentType SourceDocumentType,OccurredAt,N'Created' Status,TenantId,BusinessId
            FROM accounting.VoucherDrafts WHERE SentAt IS NULL
            """;

    private const string TraceabilityUnfilteredPostingSql = """
            WITH posting AS (
              SELECT SourceDocumentId,SourceDocumentType,OccurredAt,Status,TenantId,BusinessId
              FROM dbo.AccountingPostingJobs
              UNION ALL
              SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
                     N'MissingAccountingJob',source.TenantId,source.BusinessId
              FROM #MissingPostingSources source
              WHERE
            """ + "\n" + TraceabilitySourceWithoutJobSql + "\n" + """
              UNION ALL
            """ + "\n" + TraceabilityDraftPostingSql + "\n)\n";

    private const string TraceabilitySourceWithoutJobSql = """
            NOT EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs job
              WHERE job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
                AND job.TenantId=source.TenantId AND job.BusinessId=source.BusinessId)
            """;

    // Classify the exceptional missing-job inputs once for the requested scope.
    // Both pagination and count reuse that bounded key set; neither rebuilds
    // the full source/job anti-join. Recheck only those exceptions in posting so
    // concurrent explicit legacy recovery cannot display the source twice.
    private const string TraceabilityMissingPostingSourcesSql = """
            SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,source.TenantId,source.BusinessId
            INTO #MissingPostingSources
            FROM dbo.AccountingSourceDocuments source
            WHERE source.AccountingPostingJobId IS NULL AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
              AND CAST(source.OccurredAt AS date) BETWEEN @From AND @To
              AND (@DocumentType IS NULL OR source.SourceDocumentType=@DocumentType)
              AND
            """ + "\n" + TraceabilitySourceWithoutJobSql + ";\n" + """
            CREATE CLUSTERED INDEX IX_MissingPostingSources_Date ON #MissingPostingSources
              (OccurredAt DESC,SourceDocumentId,SourceDocumentType);
            """;

    // Match parties at their source owners before joining posting state. The
    // non-manual fallback has a filtered source index, so it does not scan or
    // parse unrelated manual-voucher payloads. The measured 5/50005-row skew
    // requires compiling this candidate query for the selected party.
    private const string TraceabilityPartyMatchesSql = """
            CREATE TABLE #PartyDocumentKeys(SourceDocumentId uniqueidentifier NOT NULL,
              SourceDocumentType nvarchar(64) COLLATE DATABASE_DEFAULT NOT NULL,
              OccurredAt datetimeoffset(7) NULL,HasAcceptedJob bit NULL,
              PRIMARY KEY(SourceDocumentId,SourceDocumentType));
            INSERT #PartyDocumentKeys(SourceDocumentId,SourceDocumentType,OccurredAt,HasAcceptedJob)
              SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
                CONVERT(bit,CASE WHEN source.AccountingPostingJobId IS NOT NULL THEN 1 ELSE 0 END) FROM accounting.SourcePartyIndex indexedParty
                JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=indexedParty.SourceDocumentId
                  AND source.SourceDocumentType=indexedParty.SourceDocumentType
                WHERE indexedParty.PartyId=@PartyId AND source.SourceDocumentType=N'ManualAccountingVoucher'
                  AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
                  AND CAST(source.OccurredAt AS date) BETWEEN @From AND @To
                  AND (@DocumentType IS NULL OR source.SourceDocumentType=@DocumentType)
              OPTION(RECOMPILE);
            -- The immutable membership index is already unique. Keep its skewed
            -- history outside the native-source deduplication and compilation.
            INSERT #PartyDocumentKeys(SourceDocumentId,SourceDocumentType)
            SELECT matched.SourceDocumentId,matched.SourceDocumentType FROM (
              SELECT DocumentId SourceDocumentId,DocumentType SourceDocumentType FROM accounting.VoucherDrafts
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND MainPartyId=@PartyId
                  AND CAST(OccurredAt AS date) BETWEEN @From AND @To
              UNION
              SELECT draft.DocumentId,draft.DocumentType FROM accounting.VoucherDraftLines line
                JOIN accounting.VoucherDrafts draft ON draft.DocumentId=line.DocumentId
                WHERE line.PartyId=@PartyId AND draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId
                  AND CAST(draft.OccurredAt AS date) BETWEEN @From AND @To
              UNION
              SELECT entry.SourceDocumentId,entry.SourceDocumentType FROM dbo.AccountingEntryLines line
                JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                WHERE line.PartyId=@PartyId AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
                  AND CAST(entry.OccurredAt AS date) BETWEEN @From AND @To
              UNION
              SELECT sourceNumber.SourceDocumentId,sourceNumber.SourceDocumentType FROM (
            """ + "\n" + TraceabilitySourceMetadataSql + "\n" + """
              ) sourceNumber WHERE sourceNumber.PartyId=@PartyId AND
            """ + "\n" + TraceabilityNativeSourceScopeSql + "\n" + """
              UNION
              SELECT draft.DocumentId,draft.DocumentType FROM accounting.VoucherDrafts draft
            """ + "\n" + AdjustmentPrincipalPartySql + "\n" + """
              WHERE draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId
                AND draft.DocumentType=N'AccountAdjustment'
                AND CAST(draft.OccurredAt AS date) BETWEEN @From AND @To
                AND adjustmentParty.PartyId=@PartyId
            ) matched WHERE (@DocumentType IS NULL OR matched.SourceDocumentType=@DocumentType)
              AND NOT EXISTS(SELECT 1 FROM #PartyDocumentKeys existing
                WHERE existing.SourceDocumentId=matched.SourceDocumentId AND existing.SourceDocumentType=matched.SourceDocumentType)
            OPTION(RECOMPILE);
            -- Accepted direct adjustments have no draft. Keep their indexed
            -- position lookup separate from the native-source UNION so its
            -- extra joins do not inflate compilation of every candidate branch.
            INSERT #PartyDocumentKeys(SourceDocumentId,SourceDocumentType)
            SELECT source.SourceDocumentId,source.SourceDocumentType FROM (
            """ + AdjustmentPositionPartiesSql + """
            ) position
            JOIN dbo.AccountingSourceDocuments source ON source.AdjustmentObligationId=position.SubledgerId
              AND source.BusinessId=@BusinessId AND source.TenantId=@TenantId
              AND source.SourceDocumentType=N'AccountAdjustment'
            WHERE position.PartyId=@PartyId AND CAST(source.OccurredAt AS date) BETWEEN @From AND @To
              AND CASE WHEN ISJSON(source.PayloadJson)=1 THEN JSON_VALUE(source.PayloadJson,'$.SubledgerKind') END=position.SubledgerKind
              AND NOT EXISTS(SELECT 1 FROM #PartyDocumentKeys existing
                WHERE existing.SourceDocumentId=source.SourceDocumentId AND existing.SourceDocumentType=source.SourceDocumentType)
            OPTION(RECOMPILE);
            IF EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments source WHERE
            """ + "\n" + PendingLinePartySourceScopeSql + "\n" + """
            )
              INSERT #PartyDocumentKeys(SourceDocumentId,SourceDocumentType)
              SELECT source.SourceDocumentId,source.SourceDocumentType FROM dbo.AccountingSourceDocuments source
              WHERE
            """ + "\n" + PendingLinePartySourceScopeSql + "\n" + """
                AND NOT EXISTS(SELECT 1 FROM #PartyDocumentKeys matched
                  WHERE matched.SourceDocumentId=source.SourceDocumentId AND matched.SourceDocumentType=source.SourceDocumentType)
                AND EXISTS(SELECT 1 FROM OPENJSON(CASE WHEN source.SourceDocumentType=N'AccountingOpeningBalance'
                    THEN JSON_QUERY(source.PayloadJson,'$.Lines') ELSE JSON_QUERY(source.PayloadJson,'$.lines') END)
                  WITH(PartyId uniqueidentifier '$.PartyId',PayrollPartyId uniqueidentifier '$.partyId') line
                  JOIN dbo.Parties party ON party.PartyId=COALESCE(line.PartyId,line.PayrollPartyId) AND party.TenantId=@TenantId
                  WHERE party.PartyId=@PartyId);
            """;

    // Opening and payroll contracts are the non-manual sources that carry
    // posting-line parties. Payroll uses the web serializer's camel case.
    private const string PendingLinePartySourceScopeSql = """
            source.TenantId=@TenantId AND source.BusinessId=@BusinessId
              AND source.SourceDocumentType IN(N'AccountingOpeningBalance',N'PayrollAccrual',N'PayrollPayment',N'PayrollAdjustment')
              AND CAST(source.OccurredAt AS date) BETWEEN @From AND @To
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries entry
                WHERE entry.SourceDocumentId=source.SourceDocumentId AND entry.SourceDocumentType=source.SourceDocumentType
                  AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId)
              AND NOT EXISTS(SELECT 1 FROM accounting.VoucherDrafts draft
                WHERE draft.DocumentId=source.SourceDocumentId AND draft.DocumentType=source.SourceDocumentType
                  AND draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId)
            """;

    private const string TraceabilityScopeSql = """
            job.TenantId=@TenantId AND job.BusinessId=@BusinessId
              AND CAST(job.OccurredAt AS date) BETWEEN @From AND @To
              AND (@DocumentType IS NULL OR job.SourceDocumentType=@DocumentType)
              AND (@Status IS NULL OR job.Status=@Status)
            """;

    // Search each attribute at its owner before joining posting state. A miss
    // must not enrich every document or build the full missing-job anti-join.
    private static string TraceabilitySearchMatchesSql(bool filterParty) => """
            -- Literal letters outside the GUID alphabet cannot match its ID.
            -- Keep LIKE patterns and database collation behavior unchanged.
            DECLARE @SearchCanMatchDocumentId bit=CASE WHEN CHARINDEX(N'%',@Search)>0
              OR CHARINDEX(N'_',@Search)>0 OR CHARINDEX(N'[',@Search)>0
              OR @Search NOT LIKE N'%[GHIJKLMNOPQRSTUVWXYZghijklmnopqrstuvwxyz]%'
              THEN 1 ELSE 0 END;
            WITH searchable AS (
              SELECT SourceDocumentId,SourceDocumentType FROM dbo.AccountingPostingJobs
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND CAST(OccurredAt AS date) BETWEEN @From AND @To
                  AND @SearchCanMatchDocumentId=1 AND CONVERT(nvarchar(36),SourceDocumentId) LIKE N'%'+@Search+N'%'
              UNION ALL SELECT SourceDocumentId,SourceDocumentType FROM dbo.AccountingSourceDocuments
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND CAST(OccurredAt AS date) BETWEEN @From AND @To
                  AND @SearchCanMatchDocumentId=1 AND CONVERT(nvarchar(36),SourceDocumentId) LIKE N'%'+@Search+N'%'
              UNION ALL SELECT DocumentId,DocumentType FROM accounting.VoucherDrafts
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND CAST(OccurredAt AS date) BETWEEN @From AND @To
                  AND (Reference LIKE N'%'+@Search+N'%' OR Description LIKE N'%'+@Search+N'%'
                    OR (@SearchCanMatchDocumentId=1 AND CONVERT(nvarchar(36),DocumentId) LIKE N'%'+@Search+N'%'))
              UNION ALL SELECT SourceDocumentId,SourceDocumentType FROM dbo.AccountingEntries
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND CAST(OccurredAt AS date) BETWEEN @From AND @To
                  AND EntryNumber LIKE N'%'+@Search+N'%'
              UNION ALL SELECT SourceDocumentId,SourceDocumentType FROM (
            """ + "\n" + TraceabilitySourceMetadataSql + "\n" + """
              ) sourceNumber WHERE DocumentNumber LIKE N'%'+@Search+N'%'
                AND
            """ + "\n" + TraceabilityNativeSourceScopeSql + "\n" + """
            )
            SELECT DISTINCT searchable.SourceDocumentId,searchable.SourceDocumentType INTO #SearchDocumentKeys
            FROM searchable
            """ + (filterParty ? "\nJOIN #PartyDocumentKeys matched ON matched.SourceDocumentId=searchable.SourceDocumentId AND matched.SourceDocumentType=searchable.SourceDocumentType\n" : "\n") + """
            WHERE (@DocumentType IS NULL OR searchable.SourceDocumentType=@DocumentType) OPTION(RECOMPILE);
            CREATE UNIQUE CLUSTERED INDEX IX_SearchDocumentKeys ON #SearchDocumentKeys(SourceDocumentId,SourceDocumentType);
            """;

    private static string TraceabilityMatchPreparationSql(string? search, Guid? partyId) =>
            (partyId.HasValue ? TraceabilityPartyMatchesSql + "\n" : "") +
            (search is not null ? TraceabilitySearchMatchesSql(partyId.HasValue) + "\n" : "") +
            (TraceabilityMatchesTableSql(search, partyId) is null ? TraceabilityMissingPostingSourcesSql + "\n" : "");

    // Apply the same candidate keys to exceptions, page and count. A narrow
    // party or a missing search must not classify the entire history first.
    private static string? TraceabilityMatchesTableSql(string? search, Guid? partyId) =>
            search is not null ? "#SearchDocumentKeys" : partyId.HasValue ? "#PartyDocumentKeys" : null;

    private static string TraceabilityMatchesJoinSql(string? search, Guid? partyId, string alias) =>
            TraceabilityMatchesTableSql(search, partyId) is { } matches
                ? $"JOIN {matches} matched ON matched.SourceDocumentId={alias}.SourceDocumentId AND matched.SourceDocumentType={alias}.SourceDocumentType\n" : "";

    private static string TraceabilityFilteredPostingSql(string input, bool includeEntry, string? search, Guid? partyId) => $"FROM {input} job\n" +
            TraceabilityMatchesJoinSql(search, partyId, "job") +
            (includeEntry ? "LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId AND entry.SourceDocumentType=job.SourceDocumentType AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId\n" : "") +
            "WHERE " + TraceabilityScopeSql;

    // Freeze filtered metadata once: a newly accepted document cannot change
    // the count after pagination. Document details still load only for the page.
    private static string TraceabilityDocumentKeysSql(string? search, Guid? partyId, bool includeEntry = true,
        bool reuseAcceptedPartyMetadata = false)
    {
        var preparation = TraceabilityMatchPreparationSql(search, partyId);
        var selection = "SELECT job.SourceDocumentId,job.SourceDocumentType,job.OccurredAt,job.Status" +
            (includeEntry ? ",entry.EntryId" : "");
        if (TraceabilityMatchesTableSql(search, partyId) is not { } matches)
            return preparation + TraceabilityUnfilteredPostingSql + "\n" + selection + " INTO #DocumentKeys\n" +
                TraceabilityFilteredPostingSql("posting", includeEntry, null, null) + ";\n";

        // The source's scoped FK already proves its accepted job exists. When
        // status/entry filtering is unnecessary, reuse that narrow metadata and
        // load the authoritative job status only for the selected page. Legacy
        // unlinked sources and native matches still use the ordinary job lookup.
        var acceptedSelection = reuseAcceptedPartyMetadata && partyId.HasValue && search is null && !includeEntry
            ? """
              SELECT matched.SourceDocumentId,matched.SourceDocumentType,matched.OccurredAt,
                CONVERT(nvarchar(40),NULL) Status INTO #DocumentKeys
              FROM #PartyDocumentKeys matched WHERE matched.HasAcceptedJob=1;
              INSERT #DocumentKeys
              """ + "\n" + selection + "\n" +
                TraceabilityFilteredPostingSql("dbo.AccountingPostingJobs", false, null, partyId) +
                " AND (matched.HasAcceptedJob IS NULL OR matched.HasAcceptedJob=0) OPTION(RECOMPILE);\n"
            : selection + " INTO #DocumentKeys\n" +
                TraceabilityFilteredPostingSql("dbo.AccountingPostingJobs", includeEntry, search, partyId) + " OPTION(RECOMPILE);\n";

        // Candidate keys and posting jobs are each unique by source ID/type.
        // Equal counts prove every candidate has a matching job; no source
        // exception lookup is needed. Apply this before adding editable drafts.
        return preparation + acceptedSelection + $"""
            IF (@Status IS NULL OR @Status=N'MissingAccountingJob')
              AND (SELECT COUNT(*) FROM #DocumentKeys)<(SELECT COUNT(*) FROM {matches})
            BEGIN
              INSERT #DocumentKeys
            """ + "\n" + selection + "\n" + TraceabilityFilteredPostingSql("(" + """
            SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
              N'MissingAccountingJob' Status,source.TenantId,source.BusinessId
            FROM dbo.AccountingSourceDocuments source WHERE source.AccountingPostingJobId IS NULL AND
            """ + "\n" + TraceabilitySourceWithoutJobSql + "\n" + """
              AND NOT EXISTS(SELECT 1 FROM #DocumentKeys captured WHERE captured.SourceDocumentId=source.SourceDocumentId
                AND captured.SourceDocumentType=source.SourceDocumentType)
            )
            """, includeEntry, search, partyId) + " OPTION(RECOMPILE);\nEND;\nINSERT #DocumentKeys\n" +
            selection + "\n" + TraceabilityFilteredPostingSql("(" + TraceabilityDraftPostingSql + ")", includeEntry, search, partyId) +
            " OPTION(RECOMPILE);\n";
    }
    // Only selected document/line keys are enriched. The same filtered keys own the count.
    private const string TraceabilityDocumentDetailsSql = """
            SELECT job.SourceDocumentId,job.SourceDocumentType,COALESCE(sourceNumber.DocumentNumber,draft.Reference) DocumentNumber,
              job.OccurredAt,COALESCE(posting.Status,job.Status) Status,COALESCE(posting.AttemptCount,0) AttemptCount,
              COALESCE(posting.LastErrorCode,CASE WHEN job.Status=N'MissingAccountingJob' THEN N'MissingAccountingJob' END) LastErrorCode,
              COALESCE(posting.LastErrorMessage,CASE WHEN job.Status=N'MissingAccountingJob' THEN N'The immutable accounting source has no durable posting job.' END) LastErrorMessage,
              entry.EntryId,entry.EntryNumber,
              COALESCE(entry.DebitTotal,draftTotals.DebitTotal,draft.AdjustmentAmount) DebitTotal,
              COALESCE(entry.CreditTotal,draftTotals.CreditTotal,draft.AdjustmentAmount) CreditTotal,entry.PostedAt,
              fiscal.FiscalDocumentType,fiscal.FiscalNumber,fiscal.UniqueCodeType,fiscal.UniqueCode,fiscal.FiscalStatus,
              CONVERT(bit,CASE WHEN draft.DocumentId IS NOT NULL THEN 1 ELSE 0 END) HasManualDraft,
              mainParty.PartyId MainPartyId,
              mainParty.DisplayName MainPartyName,mainParty.Identification MainPartyIdentification,
              reversal.SourceDocumentId ReversalDocumentId,reversalJob.Status ReversalStatus
            INTO #Documents
            FROM #DocumentPage job
            LEFT JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=job.SourceDocumentId
              AND posting.SourceDocumentType=job.SourceDocumentType AND posting.TenantId=@TenantId AND posting.BusinessId=@BusinessId
            LEFT JOIN accounting.VoucherDrafts draft ON draft.DocumentId=job.SourceDocumentId
              AND draft.DocumentType=job.SourceDocumentType AND draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId
            """ + "\n" + DocumentPrincipalPartySql + "\n" + """
            OUTER APPLY(SELECT SUM(Debit) DebitTotal,SUM(Credit) CreditTotal
              FROM accounting.VoucherDraftLines WHERE DocumentId=draft.DocumentId) draftTotals
            LEFT JOIN dbo.AccountingEntries entry ON entry.EntryId=job.EntryId
            LEFT JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=job.SourceDocumentId AND fiscal.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingSourceDocuments reversal ON reversal.ReversesDocumentId=job.SourceDocumentId
              AND reversal.TenantId=@TenantId AND reversal.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingPostingJobs reversalJob ON reversalJob.SourceDocumentId=reversal.SourceDocumentId
              AND reversalJob.SourceDocumentType=reversal.SourceDocumentType
              AND reversalJob.TenantId=@TenantId AND reversalJob.BusinessId=@BusinessId
            """ + ";";

    // One relational source map owns native number and counterparty lookup.
    // Entity primary keys and technical document types make the join unambiguous.
    private const string TraceabilitySourceMetadataSql = """
            SELECT sale.DocumentId SourceDocumentId,sale.DocumentType SourceDocumentType,sale.DocumentNumber,customer.PartyId
              FROM dbo.SalesDocuments sale LEFT JOIN dbo.Customers customer ON customer.CustomerId=sale.CustomerId AND customer.TenantId=@TenantId
              WHERE sale.BusinessId=@BusinessId
            UNION ALL SELECT value.ReturnId,N'SalesReturn',value.DocumentNumber,customer.PartyId
              FROM dbo.SalesReturns value LEFT JOIN dbo.Customers customer ON customer.CustomerId=value.CustomerId AND customer.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.DebitNoteId,N'SalesDebitNote',value.DocumentNumber,customer.PartyId
              FROM dbo.SalesDebitNotes value LEFT JOIN dbo.Customers customer ON customer.CustomerId=value.CustomerId AND customer.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.GoodsReceiptId,N'GoodsReceipt',value.DocumentNumber,supplier.PartyId
              FROM dbo.GoodsReceipts value LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=value.SupplierId AND supplier.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.PurchaseReturnId,N'PurchaseReturn',value.DocumentNumber,supplier.PartyId
              FROM dbo.PurchaseReturns value LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=value.SupplierId AND supplier.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.ExpenseId,N'Expense',value.DocumentNumber,supplier.PartyId
              FROM dbo.Expenses value LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=value.SupplierId AND supplier.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.PaymentId,N'PayablePayment',value.DocumentNumber,supplier.PartyId
              FROM dbo.SupplierPayments value LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=value.SupplierId AND supplier.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId AND value.ParentSourceDocumentId IS NULL
            UNION ALL SELECT value.PaymentId,N'ReceivablePayment',value.DocumentNumber,customer.PartyId
              FROM dbo.CustomerPayments value LEFT JOIN dbo.Customers customer ON customer.CustomerId=value.CustomerId AND customer.TenantId=@TenantId
              WHERE value.BusinessId=@BusinessId AND value.ParentSourceDocumentId IS NULL
            UNION ALL SELECT value.DocumentId,value.DocumentType,value.DocumentNumber,CAST(NULL AS uniqueidentifier)
              FROM dbo.CashMovementDocuments value WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.InventoryOperationId,value.DocumentType,value.DocumentNumber,CAST(NULL AS uniqueidentifier)
              FROM dbo.InventoryOperations value WHERE value.BusinessId=@BusinessId
            UNION ALL SELECT value.CostDocumentId,N'GoodsReceiptCostDocument',value.DocumentNumber,supplier.PartyId
              FROM purchasing.GoodsReceiptCostDocuments value
              JOIN dbo.GoodsReceipts parent ON parent.GoodsReceiptId=value.GoodsReceiptId AND parent.BusinessId=@BusinessId
              LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=value.SupplierId AND supplier.TenantId=@TenantId
              WHERE value.SourceEntryId IS NULL
            """;

    private const string TraceabilitySourceMetadataJoinSql = "LEFT JOIN (" + TraceabilitySourceMetadataSql + """
            ) sourceNumber ON sourceNumber.SourceDocumentId=job.SourceDocumentId AND sourceNumber.SourceDocumentType=job.SourceDocumentType
            """;

    private static string TraceabilityDocumentsSql(string? search, Guid? partyId, bool filterStatus) =>
            TraceabilityDocumentKeysSql(search, partyId, includeEntry: false, reuseAcceptedPartyMetadata: !filterStatus) + "\n" + """
            SELECT job.SourceDocumentId,job.SourceDocumentType,job.OccurredAt,job.Status INTO #DocumentPageKeys
            FROM #DocumentKeys job
            ORDER BY job.OccurredAt DESC,job.SourceDocumentId,job.SourceDocumentType
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT page.*,entry.EntryId INTO #DocumentPage FROM #DocumentPageKeys page
            LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=page.SourceDocumentId
              AND entry.SourceDocumentType=page.SourceDocumentType AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId;
            """ + TraceabilityDocumentDetailsSql;

    private const string TraceabilityNativeSourceScopeSql = """
            (EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs job
              WHERE job.SourceDocumentId=sourceNumber.SourceDocumentId AND job.SourceDocumentType=sourceNumber.SourceDocumentType
                AND job.TenantId=@TenantId AND job.BusinessId=@BusinessId
                AND CAST(job.OccurredAt AS date) BETWEEN @From AND @To)
              OR EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments source
              WHERE source.SourceDocumentId=sourceNumber.SourceDocumentId AND source.SourceDocumentType=sourceNumber.SourceDocumentType
                AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
                AND CAST(source.OccurredAt AS date) BETWEEN @From AND @To))
            """;

    private const string AdjustmentPositionPartiesSql = """
              SELECT N'Receivable' SubledgerKind,balance.ReceivableId SubledgerId,party.PartyId FROM dbo.Receivables balance
                LEFT JOIN dbo.Customers customer ON customer.CustomerId=balance.CustomerId AND customer.TenantId=@TenantId
                JOIN dbo.Parties party ON party.PartyId=COALESCE(balance.PartyId,customer.PartyId) AND party.TenantId=@TenantId
                WHERE balance.BusinessId=@BusinessId
              UNION ALL SELECT N'Payable',balance.PayableId,supplier.PartyId FROM dbo.Payables balance JOIN dbo.Suppliers supplier ON supplier.SupplierId=balance.SupplierId AND supplier.TenantId=@TenantId
                WHERE balance.BusinessId=@BusinessId
            """;

    private const string AdjustmentPrincipalPartySql = """
            OUTER APPLY(SELECT position.PartyId FROM (
            """ + AdjustmentPositionPartiesSql + """
            ) position WHERE position.SubledgerId=draft.SubledgerId AND position.SubledgerKind=draft.SubledgerKind
              AND draft.DocumentType=N'AccountAdjustment'
            ) adjustmentParty
            """;

    private const string DocumentPrincipalPartySql = TraceabilitySourceMetadataJoinSql + "\n" + """
            LEFT JOIN dbo.AccountingSourceDocuments immutableSource ON immutableSource.SourceDocumentId=job.SourceDocumentId
              AND immutableSource.SourceDocumentType=job.SourceDocumentType AND immutableSource.TenantId=@TenantId AND immutableSource.BusinessId=@BusinessId
              AND job.SourceDocumentType IN(N'ManualAccountingVoucher',N'AccountAdjustment') AND draft.MainPartyId IS NULL
            OUTER APPLY(SELECT position.PartyId FROM (
            """ + AdjustmentPositionPartiesSql + """
            ) position WHERE position.SubledgerId=COALESCE(draft.SubledgerId,immutableSource.AdjustmentObligationId)
              AND position.SubledgerKind=COALESCE(draft.SubledgerKind,
                CASE WHEN ISJSON(immutableSource.PayloadJson)=1 THEN JSON_VALUE(immutableSource.PayloadJson,'$.SubledgerKind') END)
              AND job.SourceDocumentType=N'AccountAdjustment') adjustmentParty
            OUTER APPLY(SELECT COALESCE(sourceNumber.PartyId,adjustmentParty.PartyId) PartyId) sourceParty
            OUTER APPLY(SELECT COALESCE(draft.MainPartyId,
              CASE WHEN job.SourceDocumentType=N'ManualAccountingVoucher' THEN TRY_CONVERT(uniqueidentifier,JSON_VALUE(immutableSource.PayloadJson,'$.MainPartyId')) END,
              sourceParty.PartyId) PartyId) principalParty
            LEFT JOIN dbo.Parties mainParty ON mainParty.PartyId=principalParty.PartyId AND mainParty.TenantId=@TenantId
            """;

}
