using Auraly.BuildingBlocks.Domain.Time;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.BuildingBlocks.Domain.Payments;
using System.Data;
using Auraly.Application.Receivables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlReceivablesStore
{
    public Task<ReceivablesReportPage> ReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token) =>
        ReportCoreAsync(user, query, query.PageSize, token);

    public Task<ReceivablesReportPage> PrintReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token) =>
        ReportCoreAsync(user, query with { Page = 1 }, PortfolioReportLimits.MaximumExportRows + 1, token);

    private async Task<ReceivablesReportPage> ReportCoreAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, int take, CancellationToken token)
    {
        var consolidated = query.Consolidated ? 1 : 0;
        var direction = query.SortBy is "ageDays" or "daysPastDue"
            ? query.SortDirection == "desc" ? "asc" : "desc" : query.SortDirection;
        var selectedOrder = SqlPagedSort.Build(query.SortBy, direction,
            new Dictionary<string, string>
            {
                ["name"] = "CustomerName", ["issuedAt"] = "IssuedAt",
                ["dueDate"] = "DueDate", ["documentNumber"] = "DocumentNumber",
                ["ageDays"] = "IssuedAt", ["daysPastDue"] = "DueDate",
                ["originalAmount"] = "OriginalAmount", ["paidAmount"] = "PaidAmount",
                ["outstandingAmount"] = "OutstandingAmount",
                ["netAmount"] = "NetAmount",
                ["overdueAmount"] = "OverdueAmount", ["invoiceCount"] = "InvoiceCount"
            }, query.Consolidated ? "name" : "issuedAt", "asc",
            "CustomerId", "PartyId", "PartySiteId", "CurrencyCode", "ReceivableId");
        var order = query.Consolidated ? selectedOrder : SqlPagedSort.PrependDistinct(selectedOrder,
            query.SortBy == "name" && query.SortDirection == "desc" ? "CustomerName DESC" : "CustomerName ASC",
            "PartySiteId ASC");
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        var zone = await SqlBusinessLocalDates.ReadTimeZoneAsync(connection, user.TenantId,
            user.BusinessId, token) ?? throw new ReceivablesForbiddenException(
                "La sede no pertenece a la empresa autenticada.");
        await using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            -- Snapshot rejects CREATE INDEX/ALTER TABLE, including temporary
            -- objects. Prepare their empty schema before the consistent read.
            CREATE TABLE #Scoped(ReceivableId uniqueidentifier,CustomerId uniqueidentifier NULL,PartyId uniqueidentifier,
              PartySiteId uniqueidentifier NULL,DocumentNumber nvarchar(64) COLLATE DATABASE_DEFAULT,CurrencyCode char(3) COLLATE DATABASE_DEFAULT,OriginalAmount decimal(19,4),
              DueDate datetimeoffset(7),Status nvarchar(24) COLLATE DATABASE_DEFAULT,IssuedAt datetimeoffset(7));
            CREATE TABLE #DebtGroups(PartyId uniqueidentifier,PartySiteId uniqueidentifier NULL,CurrencyCode char(3) COLLATE DATABASE_DEFAULT);
            CREATE UNIQUE CLUSTERED INDEX IX_DebtGroups ON #DebtGroups(PartyId,PartySiteId,CurrencyCode);
            CREATE TABLE #CreditScope(CustomerCreditId uniqueidentifier,CustomerId uniqueidentifier NULL,PartyId uniqueidentifier,
              PartySiteId uniqueidentifier NULL,
              CurrencyCode char(3) COLLATE DATABASE_DEFAULT NULL,CreditNature nvarchar(16) COLLATE DATABASE_DEFAULT NULL,OriginalAmount decimal(19,4));
            CREATE TABLE #CreditCandidates(CustomerCreditId uniqueidentifier,PartyId uniqueidentifier,
              PartySiteId uniqueidentifier NULL,CurrencyCode char(3) COLLATE DATABASE_DEFAULT NULL,
              CreditNature nvarchar(16) COLLATE DATABASE_DEFAULT NULL,OriginalAmount decimal(19,4));
            CREATE TABLE #PageApplications(PositionId uniqueidentifier,Amount decimal(19,4),OccurredAt datetimeoffset(7),
              AppliedAt datetimeoffset(7),PaymentId uniqueidentifier,UnapplicationTransactionId uniqueidentifier NULL);
            CREATE CLUSTERED INDEX IX_PageApplications ON #PageApplications(PaymentId);
            CREATE TABLE #ScopedKeys(ReceivableId uniqueidentifier PRIMARY KEY);
            SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
            BEGIN TRY
            BEGIN TRANSACTION;
              DECLARE @RequestedPartyId uniqueidentifier=(SELECT PartyId FROM dbo.Customers
                WHERE CustomerId=@CustomerId AND TenantId=@TenantId);
              -- Resolve role-backed party identities before joining dimensions.
              -- A COALESCE in the party join produced a cartesian candidate plan
              -- and thousands of repeated site/role lookups for one customer.
              SELECT r.ReceivableId,r.CustomerId,COALESCE(customer.PartyId,r.PartyId) PartyId,
                r.PartySiteId,CASE WHEN @TemplateVersion>=4 THEN COALESCE(fiscal.FiscalNumber,entry.EntryNumber,r.DocumentNumber)
                  ELSE r.DocumentNumber END DocumentNumber,r.DocumentNumber InternalDocumentNumber,
                r.CurrencyCode,r.OriginalAmount,r.DueDate,r.Status,
                r.SourceDocumentId,r.SourceDocumentType
              INTO #DocumentCandidates
              FROM dbo.Receivables r
              JOIN dbo.Businesses business ON business.BusinessId=r.BusinessId AND business.TenantId=@TenantId
              LEFT JOIN dbo.Customers customer ON customer.CustomerId=r.CustomerId AND customer.TenantId=@TenantId
              LEFT JOIN dbo.FiscalDocuments fiscal ON @TemplateVersion>=4 AND fiscal.DocumentId=r.SourceDocumentId
                AND fiscal.BusinessId=r.BusinessId AND fiscal.SourceDocumentType=r.SourceDocumentType AND fiscal.FiscalDocumentType=N'Invoice'
              -- Seek the complete unique source identity. A tenant-only index
              -- plan otherwise reread unrelated entries for every obligation.
              LEFT JOIN dbo.AccountingEntries entry
                WITH(FORCESEEK(UQ_AccountingEntries_Source(SourceDocumentId,SourceDocumentType)))
                ON @TemplateVersion>=4 AND r.SourceDocumentType=N'ManualAccountingVoucher'
                AND entry.SourceDocumentId=r.SourceDocumentId AND entry.SourceDocumentType=r.SourceDocumentType
                AND entry.BusinessId=r.BusinessId AND entry.TenantId=@TenantId
              WHERE r.BusinessId=@BusinessId AND r.CreatedAt<@Cutoff
                AND (@CustomerId IS NULL OR @WithCredits=1 AND COALESCE(customer.PartyId,r.PartyId)=@RequestedPartyId
                  OR @WithCredits=0 AND r.CustomerId=@CustomerId)
                AND (@PartySiteId IS NULL OR r.PartySiteId=@PartySiteId)
                AND (@DocumentNumber IS NULL OR r.DocumentNumber=@DocumentNumber OR
                  @TemplateVersion>=4 AND COALESCE(fiscal.FiscalNumber,entry.EntryNumber)=@DocumentNumber)
                AND (@DueUpper IS NULL OR r.DueDate<@DueUpper)
                AND (@DueLower IS NULL OR r.DueDate>=@DueLower)
                AND (@From IS NULL OR r.CreatedAt>=@From)
                AND (@To IS NULL OR r.CreatedAt<@To);
              IF @WithCredits=1
                INSERT #CreditCandidates
                SELECT credit.CustomerCreditId,COALESCE(credit.PartyId,customer.PartyId),credit.PartySiteId,
                  credit.CurrencyCode,credit.CreditNature,credit.OriginalAmount
                FROM dbo.CustomerCredits credit
                JOIN dbo.Businesses business ON business.BusinessId=credit.BusinessId AND business.TenantId=@TenantId
                LEFT JOIN dbo.Customers customer ON customer.CustomerId=credit.CustomerId AND customer.TenantId=@TenantId
                WHERE credit.BusinessId=@BusinessId AND credit.CreatedAt<@Cutoff
                  AND (@CustomerId IS NULL OR COALESCE(credit.PartyId,customer.PartyId)=@RequestedPartyId)
                  AND NOT EXISTS(SELECT 1 FROM dbo.CustomerCreditTransactions opening
                    WHERE opening.CustomerCreditId=credit.CustomerCreditId AND opening.TransactionType=N'Opening'
                      AND (opening.OccurredAt>=@Cutoff OR opening.CreatedAt>=@Cutoff))
                  AND (@PartySiteId IS NULL OR credit.PartySiteId=@PartySiteId)
                  AND (@From IS NULL OR credit.CreatedAt>=@From) AND (@To IS NULL OR credit.CreatedAt<@To);
              SELECT identityScope.PartyId,identityScope.PartySiteId,currentCustomer.CustomerId,
                COALESCE(party.DisplayName,party.LegalName,party.Identification) CustomerName,
                COALESCE(party.Identification,N'') Identification,party.DisplayName,site.Name PartySiteName,site.Code PartySiteCode
              INTO #ReportIdentities
              FROM (SELECT PartyId,PartySiteId FROM #DocumentCandidates
                UNION SELECT PartyId,PartySiteId FROM #CreditCandidates) identityScope
              JOIN dbo.Parties party ON party.PartyId=identityScope.PartyId AND party.TenantId=@TenantId
              LEFT JOIN dbo.Customers currentCustomer ON currentCustomer.PartyId=party.PartyId AND currentCustomer.TenantId=@TenantId
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=identityScope.PartySiteId AND site.PartyId=party.PartyId;
              -- Resolve each immutable source once, using its complete PK. A
              -- source can own many obligations; scanning all other sources or
              -- parsing the same large voucher for each obligation is unrelated
              -- work. Keep the existing issue-date policy and tenant/site scope.
              SELECT selected.SourceDocumentId,selected.SourceDocumentType,issue.IssuedAt
              INTO #DocumentSources
              FROM (SELECT DISTINCT SourceDocumentId,SourceDocumentType FROM #DocumentCandidates) selected
              JOIN dbo.AccountingSourceDocuments source
                WITH(FORCESEEK(PK_AccountingSourceDocuments(SourceDocumentId,SourceDocumentType)))
                ON source.SourceDocumentId=selected.SourceDocumentId AND source.SourceDocumentType=selected.SourceDocumentType
                  AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
              CROSS APPLY accounting.PortfolioDocumentIssueDate(source.PayloadJson,source.OccurredAt) issue
              WHERE source.OccurredAt<@Cutoff;
              -- The write-once scopes remain heaps to avoid random GUID index
              -- maintenance. Set aggregates choose their own hash/sort plans.
              INSERT #Scoped
              SELECT document.ReceivableId,CASE WHEN @WithCredits=1 THEN identityScope.CustomerId ELSE document.CustomerId END,
                identityScope.PartyId,document.PartySiteId,document.DocumentNumber,document.CurrencyCode,document.OriginalAmount,
                document.DueDate,document.Status,source.IssuedAt
              FROM #DocumentCandidates document
              JOIN #ReportIdentities identityScope ON identityScope.PartyId=document.PartyId
                AND (identityScope.PartySiteId=document.PartySiteId OR identityScope.PartySiteId IS NULL AND document.PartySiteId IS NULL)
              JOIN #DocumentSources source ON source.SourceDocumentId=document.SourceDocumentId
                AND source.SourceDocumentType=document.SourceDocumentType
              WHERE (@CustomerId IS NULL OR CASE WHEN @WithCredits=1 THEN identityScope.CustomerId ELSE document.CustomerId END=@CustomerId)
                AND (@Search IS NULL OR document.DocumentNumber LIKE N'%' + @Search + N'%'
                  OR document.InternalDocumentNumber LIKE N'%' + @Search + N'%'
                  OR identityScope.DisplayName LIKE N'%' + @Search + N'%'
                  OR identityScope.Identification LIKE N'%' + @Search + N'%'
                  OR identityScope.PartySiteName LIKE N'%' + @Search + N'%'
                  OR identityScope.PartySiteCode LIKE N'%' + @Search + N'%');
            -- Materialize the immutable issue date once. Reusing the inlined
            -- JSON expression in each date predicate parses it repeatedly for
            -- every candidate. These predicates still precede all financial
            -- aggregates, totals, ordering and pagination.
            IF @IssuedFrom IS NOT NULL OR @IssuedTo IS NOT NULL OR @AgeUpper IS NOT NULL OR @AgeLower IS NOT NULL
              DELETE FROM #Scoped WHERE
                (@IssuedFrom IS NOT NULL AND IssuedAt<@IssuedFrom)
                OR (@IssuedTo IS NOT NULL AND IssuedAt>=@IssuedTo)
                OR (@AgeUpper IS NOT NULL AND IssuedAt>=@AgeUpper)
                OR (@AgeLower IS NOT NULL AND IssuedAt<@AgeLower);
            -- Financial aggregates need identities, not the repeated names and
            -- document labels of the report. Share one narrow unique key set
            -- after every document/date filter, before reading any movements.
            INSERT #ScopedKeys SELECT ReceivableId FROM #Scoped;
            -- Reuse this authorized set with real cardinality statistics. Inlining
            -- its party/search joins into each aggregate led to nested loops over
            -- millions of reads for a 10,000-position historical report.
            WITH Movement AS (
              SELECT tx.ReceivableId,
                SUM(effect.BalanceChange) Balance
              FROM dbo.ReceivableTransactions tx
              CROSS APPLY accounting.PortfolioTransactionEffect(tx.TransactionType,tx.Amount) effect
              JOIN #ScopedKeys scoped ON scoped.ReceivableId=tx.ReceivableId
              WHERE tx.CreatedAt<@Cutoff AND tx.OccurredAt<@Cutoff
              GROUP BY tx.ReceivableId
            ), Paid AS (
              SELECT application.PositionId ReceivableId,SUM(application.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') application
              JOIN #ScopedKeys scoped ON scoped.ReceivableId=application.PositionId
              WHERE application.AppliedAt<@Cutoff AND application.OccurredAt<@Cutoff
              GROUP BY application.PositionId
            ), Ledger AS (
            SELECT scoped.*,COALESCE(movement.Balance,0) OutstandingAmount,
              COALESCE(paid.PaidAmount,0) PaidAmount,
              CASE WHEN movement.Balance<=0 THEN
                CASE WHEN scoped.Status=N'Cancelled' THEN N'Cancelled' ELSE N'Paid' END
                WHEN movement.Balance>=scoped.OriginalAmount THEN N'Open'
                ELSE N'PartiallyPaid' END HistoricalStatus
            FROM #Scoped scoped
            JOIN Movement movement ON movement.ReceivableId=scoped.ReceivableId
            LEFT JOIN Paid paid ON paid.ReceivableId=scoped.ReceivableId
            )

            -- Filter the derived ledger directly. Copying its wide document
            -- rows into a second temporary heap added a full write/read before
            -- applying the same historical status and balance predicates.
            SELECT *,CASE WHEN OutstandingAmount>0 AND DueDate<@AgeDay
              THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END OverdueAmount,
              CASE WHEN OutstandingAmount>0 AND DueDate>=@AgeDay THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END NotDueAmount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@AgeDay AND DueDate>=@Age30 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END Overdue1To30Amount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@Age30 AND DueDate>=@Age60 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END Overdue31To60Amount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@Age60 AND DueDate>=@Age90 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END Overdue61To90Amount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@Age90 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END OverdueOver90Amount
            INTO #Filtered FROM Ledger
            WHERE (@Status IS NULL OR HistoricalStatus=@Status)
              AND (@OutstandingOnly=0 OR OutstandingAmount>0)
              AND (@OverdueOnly=0 OR (OutstandingAmount>0 AND DueDate<@AgeDay));

            CREATE TABLE #CreditBalances(CustomerId uniqueidentifier NULL,PartyId uniqueidentifier NOT NULL,
              PartySiteId uniqueidentifier NULL,CurrencyCode char(3) COLLATE DATABASE_DEFAULT,
              AvailableAdvances decimal(38,4),AvailableCredits decimal(38,4),UnprovenCredits int);
            DECLARE @UnprovenCredits int=0;
            IF @WithCredits=1
            BEGIN
              INSERT #DebtGroups SELECT DISTINCT PartyId,PartySiteId,CurrencyCode FROM #Filtered;
              -- Scope identities before history. Never reconstruct a historical
              -- credit from today's mutable available balance.
              -- Search and debt-group membership belong to the party/site and
              -- currency, not to each credit. Resolve them once per identity;
              -- 10,000 credits for one party must not repeat 10,000 debt probes.
              SELECT identityScope.PartyId,identityScope.PartySiteId,identityScope.CustomerId,
                credit.CurrencyCode INTO #CreditIdentities
              FROM (SELECT DISTINCT PartyId,PartySiteId,CurrencyCode FROM #CreditCandidates) credit
              JOIN #ReportIdentities identityScope ON identityScope.PartyId=credit.PartyId
                AND (identityScope.PartySiteId=credit.PartySiteId OR identityScope.PartySiteId IS NULL AND credit.PartySiteId IS NULL)
              LEFT JOIN (SELECT DISTINCT PartyId,CurrencyCode FROM #DebtGroups) debt
                ON debt.PartyId=identityScope.PartyId AND debt.CurrencyCode=credit.CurrencyCode
              WHERE (@CustomerId IS NULL OR identityScope.CustomerId=@CustomerId)
                AND (@RestrictCreditGroups=0 OR debt.PartyId IS NOT NULL)
                AND (@Search IS NULL OR identityScope.DisplayName LIKE N'%'+@Search+N'%'
                  OR identityScope.Identification LIKE N'%'+@Search+N'%' OR identityScope.PartySiteName LIKE N'%'+@Search+N'%'
                  OR identityScope.PartySiteCode LIKE N'%'+@Search+N'%' OR debt.PartyId IS NOT NULL);
              INSERT #CreditScope SELECT credit.CustomerCreditId,identityScope.CustomerId,identityScope.PartyId,credit.PartySiteId,
                credit.CurrencyCode,credit.CreditNature,credit.OriginalAmount
              FROM #CreditCandidates credit
              JOIN #CreditIdentities identityScope ON identityScope.PartyId=credit.PartyId
                AND (identityScope.PartySiteId=credit.PartySiteId OR identityScope.PartySiteId IS NULL AND credit.PartySiteId IS NULL)
                AND (identityScope.CurrencyCode=credit.CurrencyCode OR identityScope.CurrencyCode IS NULL AND credit.CurrencyCode IS NULL);
              -- Validate and total each scoped credit in one aggregate. A second
              -- materialized history copied every credit and joined it twice
              -- again merely to return one balance/proof count per identity.
              WITH CreditHistory AS (
              SELECT scoped.CustomerCreditId,SUM(effect.BalanceChange) Available,
                MAX(CASE WHEN tx.TransactionType=N'Opening' THEN 1 ELSE 0 END) HasOpening,
                SUM(CASE WHEN tx.TransactionType=N'Opening' THEN tx.Amount ELSE 0 END) OpeningAmount
              FROM #CreditScope scoped
              JOIN dbo.CustomerCreditTransactions tx ON tx.CustomerCreditId=scoped.CustomerCreditId
              CROSS APPLY accounting.PortfolioTransactionEffect(tx.TransactionType,tx.Amount) effect
              WHERE tx.CreatedAt<@Cutoff AND tx.OccurredAt<@Cutoff GROUP BY scoped.CustomerCreditId)
              INSERT #CreditBalances
              SELECT scoped.CustomerId,scoped.PartyId,scoped.PartySiteId,scoped.CurrencyCode,
                SUM(CASE WHEN history.HasOpening=1 AND history.Available>0 AND scoped.CreditNature=N'Advance' THEN history.Available ELSE 0 END),
                SUM(CASE WHEN history.HasOpening=1 AND history.Available>0 AND (scoped.CreditNature<>N'Advance' OR scoped.CreditNature IS NULL) THEN history.Available ELSE 0 END),
                SUM(CASE WHEN history.HasOpening IS NULL OR history.HasOpening=0 OR scoped.CurrencyCode IS NULL OR scoped.CurrencyCode<>N'COP'
                  OR history.OpeningAmount<>scoped.OriginalAmount OR history.Available<0 OR history.Available>scoped.OriginalAmount THEN 1 ELSE 0 END)
              FROM #CreditScope scoped LEFT JOIN CreditHistory history ON history.CustomerCreditId=scoped.CustomerCreditId
              GROUP BY scoped.CustomerId,scoped.PartyId,scoped.PartySiteId,scoped.CurrencyCode;
              SELECT @UnprovenCredits=COALESCE(SUM(UnprovenCredits),0) FROM #CreditBalances;
              -- Proof failures remain counted even when no credit is available;
              -- zero-only groups must not create a synthetic report row.
              DELETE #CreditBalances WHERE AvailableAdvances=0 AND AvailableCredits=0;
            END;

            -- Exactly one credit row per party/site/currency, never per invoice.
            -- Inline the derived rows into the chosen report shape instead of
            -- writing/copying a second wide heap. The closed mode is a constant:
            -- a detail page must not reserve memory for an unused grouped sort.
            WITH Positions AS (
            SELECT CustomerId,PartySiteId,CurrencyCode,
              InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount,NotDueAmount,
              Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount,
              ReceivableId,DocumentNumber,IssuedAt,DueDate,PartyId,
              CONVERT(decimal(38,4),0) AvailableAdvances,CONVERT(decimal(38,4),0) AvailableCredits,CONVERT(bit,0) IsCreditBalance
            FROM (SELECT *,1 InvoiceCount FROM #Filtered) debt
            UNION ALL
            SELECT CustomerId,PartySiteId,CurrencyCode,
              0,0,0,0,0,0,0,0,0,0,NULL,N'Anticipos y otros saldos a favor',NULL,NULL,PartyId,
              AvailableAdvances,AvailableCredits,CONVERT(bit,1) FROM #CreditBalances
            )

            SELECT CustomerId,PartySiteId,
              CurrencyCode,SUM(InvoiceCount) InvoiceCount,SUM(OriginalAmount) OriginalAmount,
              SUM(PaidAmount) PaidAmount,SUM(OutstandingAmount) OutstandingAmount,
              SUM(OverdueAmount) OverdueAmount,
              SUM(NotDueAmount) NotDueAmount,SUM(Overdue1To30Amount) Overdue1To30Amount,
              SUM(Overdue31To60Amount) Overdue31To60Amount,SUM(Overdue61To90Amount) Overdue61To90Amount,
              SUM(OverdueOver90Amount) OverdueOver90Amount,
              CAST(NULL AS uniqueidentifier) ReceivableId,CAST(NULL AS nvarchar(64)) DocumentNumber,
              CAST(NULL AS datetimeoffset(7)) IssuedAt,CAST(NULL AS datetimeoffset(7)) DueDate,
              PartyId,SUM(AvailableAdvances) AvailableAdvances,SUM(AvailableCredits) AvailableCredits,CONVERT(bit,0) IsCreditBalance,
              CONVERT(decimal(28,4),SUM(OutstandingAmount))-CONVERT(decimal(28,4),SUM(AvailableAdvances))-CONVERT(decimal(28,4),SUM(AvailableCredits)) NetAmount
            INTO #Report FROM Positions WHERE {consolidated}=1
            GROUP BY CustomerId,PartyId,PartySiteId,CurrencyCode
            UNION ALL
            SELECT CustomerId,PartySiteId,
              CurrencyCode,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount,
              NotDueAmount,Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount,
              ReceivableId,DocumentNumber,IssuedAt,DueDate,PartyId,AvailableAdvances,AvailableCredits,IsCreditBalance,
              CONVERT(decimal(28,4),OutstandingAmount)-CONVERT(decimal(28,4),AvailableAdvances)-CONVERT(decimal(28,4),AvailableCredits)
            FROM Positions WHERE {consolidated}=0;

            SELECT COUNT(*),COALESCE(SUM(InvoiceCount),0),COALESCE(SUM(OriginalAmount),0),
              COALESCE(SUM(PaidAmount),0),COALESCE(SUM(OutstandingAmount),0),
              COALESCE(SUM(OverdueAmount),0),COALESCE(SUM(NotDueAmount),0),
              COALESCE(SUM(Overdue1To30Amount),0),COALESCE(SUM(Overdue31To60Amount),0),
              COALESCE(SUM(Overdue61To90Amount),0),COALESCE(SUM(OverdueOver90Amount),0),
              COALESCE(SUM(AvailableAdvances),0),COALESCE(SUM(AvailableCredits),0),@UnprovenCredits FROM #Report;
            -- Aggregate each authorized group once instead of building window
            -- totals on every wide report row before choosing the page.
            SELECT PartyId GroupPartyId,PartySiteId GroupSiteId,CurrencyCode GroupCurrencyCode,
              SUM(InvoiceCount) GroupInvoiceCount,SUM(OriginalAmount) GroupOriginal,
              SUM(PaidAmount) GroupPaid,SUM(OutstandingAmount) GroupOutstanding,
              SUM(NotDueAmount) GroupNotDue,SUM(Overdue1To30Amount) Group1To30,
              SUM(Overdue31To60Amount) Group31To60,SUM(Overdue61To90Amount) Group61To90,
              SUM(OverdueOver90Amount) GroupOver90,SUM(AvailableAdvances) GroupAdvances,SUM(AvailableCredits) GroupCredits
            INTO #ReportGroups FROM #Report GROUP BY PartyId,PartySiteId,CurrencyCode;
            -- The next page must not sort every amount/label column of every
            -- report row. Select its stable identities first, then hydrate only
            -- those rows, just as the administrative verification pages do.
            -- Labels have one owner per party/site. Carry only identities and
            -- financial values through the ledger/credit aggregates, and join
            -- only the name needed to order the page before its limit.
            WITH OrderedReport AS (
              SELECT report.*,identityScope.CustomerName FROM #Report report
              JOIN #ReportIdentities identityScope ON identityScope.PartyId=report.PartyId
                AND (identityScope.PartySiteId=report.PartySiteId OR identityScope.PartySiteId IS NULL AND report.PartySiteId IS NULL)
            )
            SELECT CustomerId,PartyId,PartySiteId,CurrencyCode,ReceivableId,IsCreditBalance
            INTO #ReportPageKeys FROM OrderedReport
            ORDER BY {order} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT report.* INTO #Page FROM #ReportPageKeys selected JOIN #Report report
              ON report.PartyId=selected.PartyId AND report.CurrencyCode=selected.CurrencyCode
                AND report.IsCreditBalance=selected.IsCreditBalance
                AND (report.CustomerId=selected.CustomerId OR report.CustomerId IS NULL AND selected.CustomerId IS NULL)
                AND (report.PartySiteId=selected.PartySiteId OR report.PartySiteId IS NULL AND selected.PartySiteId IS NULL)
                AND (report.ReceivableId=selected.ReceivableId OR report.ReceivableId IS NULL AND selected.ReceivableId IS NULL);
            WITH PageDetails AS (
              SELECT page.*,identityScope.CustomerName,identityScope.Identification,identityScope.PartySiteName
              FROM #Page page JOIN #ReportIdentities identityScope ON identityScope.PartyId=page.PartyId
                AND (identityScope.PartySiteId=page.PartySiteId OR identityScope.PartySiteId IS NULL AND page.PartySiteId IS NULL)
            )
            SELECT CustomerId,CustomerName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,
              OverdueAmount,ReceivableId,DocumentNumber,IssuedAt,DueDate,
              NotDueAmount,Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount,PartyId,
              details.LegalName,details.Address,details.City,details.Phone,details.Email,details.CreditLimit,
              GroupInvoiceCount,GroupOriginal,GroupPaid,GroupOutstanding,GroupNotDue,
              Group1To30,Group31To60,Group61To90,GroupOver90,
              AvailableAdvances,AvailableCredits,IsCreditBalance,GroupAdvances,GroupCredits,
              documentIdentity.InternalDocumentNumber
            FROM PageDetails page
            OUTER APPLY (SELECT CASE WHEN original.DocumentNumber<>page.DocumentNumber THEN original.DocumentNumber END InternalDocumentNumber
              FROM dbo.Receivables original WHERE original.ReceivableId=page.ReceivableId AND original.BusinessId=@BusinessId) documentIdentity
            JOIN #ReportGroups totals ON totals.GroupPartyId=page.PartyId
              AND totals.GroupCurrencyCode=page.CurrencyCode
              AND (totals.GroupSiteId=page.PartySiteId OR (totals.GroupSiteId IS NULL AND page.PartySiteId IS NULL))
            OUTER APPLY (
              SELECT party.LegalName,site.AddressLine Address,city.Name City,
                COALESCE(NULLIF(site.Phone,N''),phone.Value) Phone,site.Email,credit.CreditLimit
              FROM dbo.Parties party
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=page.PartySiteId AND site.PartyId=party.PartyId
              LEFT JOIN dbo.Cities city ON city.CityId=site.CityId
              LEFT JOIN dbo.CustomerCreditProfiles credit ON credit.CustomerId=page.CustomerId AND credit.BusinessId=@BusinessId
              LEFT JOIN dbo.PartyContacts phone ON phone.PartyId=party.PartyId
                AND phone.ContactType=N'Phone' AND phone.IsPrimary=1 AND phone.IsActive=1
              WHERE party.PartyId=page.PartyId AND party.TenantId=@TenantId
            ) details
            ORDER BY {order};
            IF @IncludeApplications=1
            BEGIN
            -- Materialize only this page's application facts before enriching
            -- their receipt identities. This prevents the optimizer from
            -- expanding the origins view over the complete business history.
            INSERT #PageApplications SELECT TOP(@ApplicationTake) application.*
            FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') application
            JOIN #Page page ON page.ReceivableId=application.PositionId
            WHERE application.AppliedAt<@Cutoff AND application.OccurredAt<@Cutoff
            ORDER BY application.PositionId,application.AppliedAt,application.PaymentId,application.UnapplicationTransactionId;
            SELECT TOP(@ApplicationTake) application.PositionId,
              CASE WHEN application.Amount<0 THEN CONCAT(N'Desaplicación de ',origin.DocumentNumber) ELSE origin.DocumentNumber END,application.AppliedAt,
              application.Amount
            FROM #PageApplications application
            -- PaymentId and kind identify one origin: payment/draft primary
            -- keys and UQ_AccountingEntries_Source enforce that cardinality.
            -- Keep that lookup correlated with the bounded application page.
            CROSS APPLY (SELECT TOP(1) receipt.DocumentNumber
              FROM accounting.PortfolioPaymentOrigins receipt
              WHERE receipt.PaymentId=application.PaymentId
                AND receipt.SubledgerKind=N'Receivable' AND receipt.BusinessId=@BusinessId) origin
            ORDER BY application.PositionId,application.AppliedAt,application.PaymentId,application.UnapplicationTransactionId;
            END
            ELSE
              SELECT ReceivableId PositionId,CAST(NULL AS nvarchar(80)) DocumentNumber,
                CAST(NULL AS datetimeoffset) AppliedAt,CAST(NULL AS decimal(19,4)) Amount
              FROM #Page WHERE 1=0;
            COMMIT TRANSACTION;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            END TRY
            BEGIN CATCH
              IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
              SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
              THROW;
            END CATCH;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@WithCredits", query.TemplateVersion >= 4);
        command.Parameters.AddWithValue("@RestrictCreditGroups", query.DocumentNumber is not null || query.Status is not null ||
            query.IssuedFrom is not null || query.IssuedTo is not null || query.MinimumAgeDays is not null ||
            query.MaximumAgeDays is not null || query.MinimumDaysPastDue is not null || query.MaximumDaysPastDue is not null || query.OverdueOnly);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@CustomerId", (object?)query.CustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId", (object?)query.PartySiteId ?? DBNull.Value);
        command.Parameters.Add("@DocumentNumber", SqlDbType.NVarChar, 64).Value = (object?)query.DocumentNumber ?? DBNull.Value;
        command.Parameters.AddWithValue("@TemplateVersion", query.TemplateVersion);
        command.Parameters.Add("@IssuedFrom", SqlDbType.DateTimeOffset).Value = query.IssuedFrom is { } issuedFrom
            ? SqlBusinessLocalDates.StartOfDay(issuedFrom, zone) : DBNull.Value;
        command.Parameters.Add("@IssuedTo", SqlDbType.DateTimeOffset).Value = query.IssuedTo is { } issuedTo
            ? SqlBusinessLocalDates.StartOfDay(issuedTo.AddDays(1), zone) : DBNull.Value;
        // Convert inclusive calendar-day ranges to indexed instant boundaries. No
        // per-row zone conversion or second interpretation of the aging bands.
        command.Parameters.Add("@AgeUpper", SqlDbType.DateTimeOffset).Value = query.MinimumAgeDays is { } minAge
            ? SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(1 - minAge), zone) : DBNull.Value;
        command.Parameters.Add("@AgeLower", SqlDbType.DateTimeOffset).Value = query.MaximumAgeDays is { } maxAge
            ? SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-maxAge), zone) : DBNull.Value;
        command.Parameters.Add("@DueUpper", SqlDbType.DateTimeOffset).Value = query.MinimumDaysPastDue is { } minDue
            ? SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(1 - minDue), zone) : DBNull.Value;
        command.Parameters.Add("@DueLower", SqlDbType.DateTimeOffset).Value = query.MaximumDaysPastDue is { } maxDue
            ? SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-maxDue), zone) : DBNull.Value;
        command.Parameters.AddWithValue("@Search", (object?)query.Search?.Trim() is string { Length: > 0 } search ? search : DBNull.Value);
        command.Parameters.AddWithValue("@IncludeApplications", query.IncludeApplications && !query.Consolidated);
        command.Parameters.AddWithValue("@ApplicationTake", take > 100 ? PortfolioReportLimits.MaximumExportRows + 1 : int.MaxValue);
        command.Parameters.AddWithValue("@OutstandingOnly", query.OutstandingOnly);
        command.Parameters.AddWithValue("@OverdueOnly", query.OverdueOnly);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        SqlBusinessLocalDates.BindDateRange(command,query.From,query.To,zone,query.DateRangeVersion);
        command.Parameters.AddWithValue("@Cutoff", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(1), zone));
        command.Parameters.AddWithValue("@AgeDay", SqlBusinessLocalDates.StartOfDay(query.Cutoff, zone));
        command.Parameters.AddWithValue("@Age30", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-PortfolioAging.FirstBandDays), zone));
        command.Parameters.AddWithValue("@Age60", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-PortfolioAging.SecondBandDays), zone));
        command.Parameters.AddWithValue("@Age90", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-PortfolioAging.ThirdBandDays), zone));
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", take);
        command.CommandText = SqlQueryShape.FoldOptionalPredicates(command);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var count = reader.GetInt32(0);
        var invoiceCount = reader.GetInt32(1);
        var original = reader.GetDecimal(2);
        var paid = reader.GetDecimal(3);
        var outstanding = reader.GetDecimal(4);
        var overdue = reader.GetDecimal(5);
        var notDue = reader.GetDecimal(6);
        var overdue1To30 = reader.GetDecimal(7);
        var overdue31To60 = reader.GetDecimal(8);
        var overdue61To90 = reader.GetDecimal(9);
        var overdueOver90 = reader.GetDecimal(10);
        var availableAdvances = reader.GetDecimal(11);
        var availableCredits = reader.GetDecimal(12);
        if (reader.GetInt32(13) > 0)
            throw new ReceivablesValidationException("Hay saldos a favor sin apertura histórica, moneda o movimientos conciliables. Revisa su evidencia antes de emitir el informe al corte.");
        await reader.NextResultAsync(token);
        var rows = new List<ReceivablesReportItem>();
        while (await reader.ReadAsync(token))
        {
            var age = reader.IsDBNull(13) || reader.IsDBNull(14) ? (PortfolioDocumentAge?)null :
                PortfolioAging.Describe(reader.GetDateTimeOffset(13), reader.GetDateTimeOffset(14), query.Cutoff, zone);
            rows.Add(new(reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8),
                reader.GetDecimal(9), reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : BusinessCalendar.InZone(reader.GetDateTimeOffset(13), zone),
                reader.IsDBNull(14) ? null : BusinessCalendar.InZone(reader.GetDateTimeOffset(14), zone),
                PartyId: reader.IsDBNull(20) ? null : reader.GetGuid(20))
            {
                TermDays = age?.TermDays, AgeDays = age?.AgeDays, DaysPastDue = age?.DaysPastDue,
                InternalDocumentNumber = reader.IsDBNull(41) ? null : reader.GetString(41),
                LegalName = reader.IsDBNull(21) ? null : reader.GetString(21),
                Address = reader.IsDBNull(22) ? null : reader.GetString(22),
                City = reader.IsDBNull(23) ? null : reader.GetString(23),
                Phone = reader.IsDBNull(24) ? null : reader.GetString(24),
                Email = reader.IsDBNull(25) ? null : reader.GetString(25),
                CreditLimit = reader.IsDBNull(26) ? null : reader.GetDecimal(26),
                GroupTotals = query.Consolidated ? null : new(reader.GetInt32(27), reader.GetDecimal(28),
                    reader.GetDecimal(29), reader.GetDecimal(30), reader.GetDecimal(31), reader.GetDecimal(32),
                    reader.GetDecimal(33), reader.GetDecimal(34), reader.GetDecimal(35))
                    { AvailableAdvances = reader.GetDecimal(39), AvailableCredits = reader.GetDecimal(40) },
                AvailableAdvances = reader.GetDecimal(36), AvailableCredits = reader.GetDecimal(37), IsCreditBalance = reader.GetBoolean(38),
                NotDueAmount = reader.GetDecimal(15), Overdue1To30Amount = reader.GetDecimal(16),
                Overdue31To60Amount = reader.GetDecimal(17), Overdue61To90Amount = reader.GetDecimal(18),
                OverdueOver90Amount = reader.GetDecimal(19)
            });
        }
        await reader.NextResultAsync(token);
        var applications = new Dictionary<Guid, List<ReceivablesReportApplication>>();
        while (await reader.ReadAsync(token))
        {
            var id = reader.GetGuid(0);
            if (!applications.TryGetValue(id, out var lines))
                applications.Add(id, lines = []);
            lines.Add(new(reader.GetString(1), BusinessCalendar.InZone(reader.GetDateTimeOffset(2), zone), reader.GetDecimal(3)));
        }
        var items = rows.Select(row => row.ReceivableId is Guid id
            ? row with { Applications = applications.GetValueOrDefault(id) ?? [] }
            : row).ToArray();
        return new ReceivablesReportPage(items, query.Page, take, count, invoiceCount,
            original, paid, outstanding, overdue)
        {
            TotalNotDue = notDue, TotalOverdue1To30 = overdue1To30,
            TotalOverdue31To60 = overdue31To60, TotalOverdue61To90 = overdue61To90,
            TotalOverdueOver90 = overdueOver90,
            TotalAvailableAdvances = availableAdvances, TotalAvailableCredits = availableCredits
        };
    }
}
