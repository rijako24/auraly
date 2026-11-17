param(
 [Parameter(Mandatory)][guid]$ActorUserId,
 [ValidateSet('Rehearsal','prod')][string]$Environment='Rehearsal',
 [switch]$Apply,
 [switch]$Preview,
 [switch]$ConfirmProductionExecution,
 [Parameter(Mandatory)][string]$EvidencePath
)
$ErrorActionPreference='Stop'
if($Preview -and $Apply){throw 'Preview y Apply son opciones excluyentes.'}
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if($Environment -eq 'prod'){
 if(-not $ConfirmProductionExecution){throw 'Producción exige una instrucción explícita y su confirmación; incluso el preflight realiza escrituras transaccionales que revierte.'}
 if((& git -C $repoRoot status --porcelain) -or (& git -C $repoRoot branch --show-current).Trim() -ne 'main'){
  throw 'La operación productiva exige el checkout integrado y limpio.'
 }
 & git -C $repoRoot merge-base --is-ancestor HEAD origin/main
 if($LASTEXITCODE -ne 0){throw 'El commit de la operación no pertenece a origin/main.'}
 $targetServer='sql-auraly-prod-7sov4nxc';$targetDatabase='auraly-prod'
}else{
 $targetServer='sql-auraly-dev-w5usmo6w';$targetDatabase='auraly-release-closure-20261116'
}
$planJson=Get-Content -Raw (Join-Path $PSScriptRoot 'data-plans/megafruver-historical-receivable-opening.json')
$plan=$planJson|ConvertFrom-Json
# Preserve the reviewed ISO offset as text: ConvertFrom-Json date inference
# must not change the immutable source/hash with the operator's time zone.
$planDocument=[System.Text.Json.JsonDocument]::Parse($planJson)
try{$plan.OccurredAt=$planDocument.RootElement.GetProperty('OccurredAt').GetString()}
finally{$planDocument.Dispose()}
if($plan.OriginalAmount -ne 2000 -or $plan.OutstandingAmount -ne 2000 -or
 $plan.CounterpartAccountCode -ne '36990101' -or $plan.CounterpartAccountType -ne 'Equity' -or
 $plan.ReceivableAccountCode -ne '130505'){throw 'Unexpected reviewed plan.'}
$lines=@(
 [ordered]@{LineNumber=1;AccountId=$plan.ReceivableAccountId;PartyId=$plan.PartyId;CostCenterId=$null;Description='Cartera comercial previa a activación: CVI00-00000001';Debit=$plan.OriginalAmount;Credit=0;LineId=$plan.ReceivableLineId;ExistingPosition=[ordered]@{SubledgerId=$plan.ReceivableId;SubledgerKind='Receivable'}},
 [ordered]@{LineNumber=2;AccountId=$plan.CounterpartAccountId;PartyId=$null;CostCenterId=$null;Description='Diferencia de apertura pendiente de conciliación integral';Debit=0;Credit=$plan.OriginalAmount;LineId=$plan.CounterpartLineId;ExistingPosition=$null}
)
$payload=[ordered]@{VoucherId=$plan.BatchId;BusinessId=$plan.BusinessId;OccurredAt=$plan.OccurredAt;ConceptCode='OPENING_BALANCE';Description='Conciliación apertura CVI00-00000001; saldo inicial por conciliar';Lines=$lines;PayloadVersion=3;HistoricalCatchup=$true}|ConvertTo-Json -Depth 8 -Compress
$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($payload)))
$sources=ConvertTo-Json -InputObject @([ordered]@{DocumentId=$plan.BatchId;DocumentType='AccountingOpeningBalance';BusinessId=$plan.BusinessId;Payload=$payload;Hash=$hash;OccurredAt=$plan.OccurredAt;JobId=$plan.JobId}) -Depth 4 -Compress
$validation=ConvertTo-Json -InputObject @($lines|ForEach-Object{[ordered]@{LineNumber=$_.LineNumber;AccountId=$_.AccountId;PartyId=$_.PartyId;Debit=$_.Debit;Credit=$_.Credit;LineId=$_.LineId;ExistingPayableId=$null;ExistingReceivableId=if($_.ExistingPosition){$plan.ReceivableId}else{$null}}}) -Depth 4 -Compress
if($Preview){
 [ordered]@{TargetServer=$targetServer;TargetDatabase=$targetDatabase;Plan=$plan;PayloadHash=$hash;DatabaseTouched=$false}|ConvertTo-Json -Depth 8
 return
}

$access=& az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
if($LASTEXITCODE -ne 0){throw 'Azure SQL token unavailable.'}
$connection=[Data.SqlClient.SqlConnection]::new("Server=tcp:$targetServer.database.windows.net,1433;Database=$targetDatabase;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30;Application Name=AuralyReviewedHistoricalOpening;")
$connection.AccessToken=$access;$transaction=$null
try {
 $connection.Open()
 if($connection.Database -ne $targetDatabase -or $connection.DataSource -ne "tcp:$targetServer.database.windows.net,1433"){throw 'El destino no coincide con el plan explícito.'}
 $transaction=$connection.BeginTransaction([Data.IsolationLevel]::Serializable)
 $command=$connection.CreateCommand();$command.Transaction=$transaction;$command.CommandTimeout=60
 $command.CommandText=@'
SET NOCOUNT ON; SET LOCK_TIMEOUT 10000;
IF EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments WITH(UPDLOCK,HOLDLOCK)
 WHERE SourceDocumentId=@Batch AND SourceDocumentType=N'AccountingOpeningBalance')
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Batch
   AND SourceDocumentType=N'AccountingOpeningBalance' AND TenantId=@Tenant AND BusinessId=@Business
   AND PayloadHash=CONVERT(binary(32),@Hash,2)) THROW 51990,N'La identidad de apertura tiene otro contenido.',1;
 SELECT CONVERT(bit,1); RETURN;
END;
IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@Business AND TenantId=@Tenant)
 OR NOT EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@Tenant AND Status=N'Ready'
   AND OpeningBalanceMode=N'ZeroDeclared' AND EffectiveFrom=CONVERT(date,'20260101'))
 OR NOT EXISTS(SELECT 1 FROM dbo.AppUsers WHERE UserId=@Actor AND TenantId=@Tenant AND IsActive=1)
 OR EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs WHERE TenantId=@Tenant
   AND SourceDocumentType=N'ManualAccountingVoucher' AND Status NOT IN(N'Posted',N'CommercialEffectsApplied'))
 THROW 51990,N'Cambió la empresa, activación o identidad explícita del plan.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Receivables balance WITH(UPDLOCK,HOLDLOCK)
 JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=balance.SourceDocumentId AND source.SourceDocumentType=balance.SourceDocumentType
 JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
 JOIN dbo.Customers customer ON customer.CustomerId=balance.CustomerId AND customer.TenantId=@Tenant
 CROSS APPLY accounting.ObligationAvailability(@Business,N'Receivable',balance.ReceivableId,balance.OutstandingAmount,NULL) pending
 WHERE balance.ReceivableId=@Position AND balance.BusinessId=@Business AND balance.SourceDocumentId=@OriginalSource
   AND balance.SourceDocumentType=N'SalesReceipt' AND balance.DocumentNumber=N'CVI00-00000001'
   AND balance.OriginalAmount=2000 AND balance.OutstandingAmount=2000 AND balance.CurrencyCode=N'COP' AND balance.Status=N'Open'
   AND balance.OpeningEntryId IS NULL AND balance.OpeningEntryLineNumber IS NULL AND customer.PartyId=@Party
   AND source.TenantId=@Tenant AND source.AccountingEntryRequired=0 AND job.Status=N'CommercialEffectsApplied'
   AND pending.PendingPaymentAmount=0 AND pending.PendingAdjustmentCount=0)
 THROW 51990,N'La cartera o sus reservas cambiaron respecto del plan; no se modifica.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@Tenant
 AND AccountId=@ReceivableAccount AND Code=N'130505' AND Name=N'Clientes nacionales' AND AccountType=N'Asset'
 AND (FinancialFunction IS NULL OR FinancialFunction=N'Receivable')
 AND (BalanceSide IS NULL OR BalanceSide=N'Debit') AND RequiresParty=1 AND AllowsPosting=1 AND IsActive=1)
 OR NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings WHERE TenantId=@Tenant AND Category=N'AccountsReceivable'
   AND AccountId=@ReceivableAccount AND EffectiveFrom<=@Effective AND (EffectiveTo IS NULL OR EffectiveTo>=@Effective)
   AND (BusinessId IS NULL OR BusinessId=@Business))
 OR NOT EXISTS(SELECT 1 FROM dbo.Receivables balance
   JOIN dbo.AccountingEntryLines journal ON journal.EntryId=balance.OpeningEntryId AND journal.LineNumber=balance.OpeningEntryLineNumber
   JOIN dbo.AccountingEntries entry ON entry.EntryId=journal.EntryId AND entry.TenantId=@Tenant AND entry.BusinessId=@Business
   WHERE balance.BusinessId=@Business AND journal.AccountId=@ReceivableAccount AND journal.Debit>0 AND journal.Credit=0)
 THROW 51990,N'El auxiliar de cartera no coincide con la cuenta revisada.',1;
-- Complete only fields introduced after these movements. Identity, Asset type,
-- role/party requirement, historical journal and all other configuration remain intact.
UPDATE dbo.AccountingAccounts SET FinancialFunction=COALESCE(FinancialFunction,N'Receivable'),BalanceSide=COALESCE(BalanceSide,N'Debit')
WHERE AccountId=@ReceivableAccount AND TenantId=@Tenant AND (FinancialFunction IS NULL OR BalanceSide IS NULL);
IF EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK)
 WHERE (TenantId=@Tenant AND Code=N'36990101') OR AccountId=@Counterpart)
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WHERE AccountId=@Counterpart AND TenantId=@Tenant
  AND Code=N'36990101' AND Name=N'Saldos iniciales por conciliar' AND AccountType=N'Equity'
  AND FinancialFunction=N'General' AND BalanceSide=N'Credit' AND RequiresParty=0 AND AllowsPosting=1 AND IsActive=1
  AND ParentAccountId IS NULL AND BankAccountId IS NULL AND PaymentMethodOptionId IS NULL)
  THROW 51990,N'La cuenta de apertura ya tiene otra identidad o configuración; no se sobrescribe.',1;
END
ELSE
 INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt,FinancialFunction,BalanceSide)
 VALUES(@Counterpart,@Tenant,N'36990101',N'Saldos iniciales por conciliar',N'Equity',1,0,1,@Now,N'General',N'Credit');
EXEC accounting.OpeningPositionsValidate @TenantId=@Tenant,@BusinessId=@Business,@EffectiveOn=@Effective,@LinesJson=@Lines,@HistoricalCatchup=1;
INSERT dbo.AccountingOpeningBalanceBatches(BatchId,TenantId,BusinessId,EffectiveOn,CurrencyCode,Description,Status,
 CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt,ApprovedByUserId,ApprovedAt)
VALUES(@Batch,@Tenant,@Business,@Effective,N'COP',N'Conciliación apertura CVI00-00000001; saldo inicial por conciliar',N'Approved',
 @Actor,@Now,@Actor,@Now,@Actor,@Now);
INSERT dbo.AccountingOpeningBalanceLines(BatchId,LineNumber,AccountId,PartyId,Description,Debit,Credit,LineId,ExistingReceivableId)
SELECT @Batch,LineNumber,AccountId,PartyId,CASE WHEN LineNumber=1 THEN N'Cartera comercial previa a activación: CVI00-00000001'
 ELSE N'Diferencia de apertura pendiente de conciliación integral' END,Debit,Credit,LineId,ExistingReceivableId
FROM OPENJSON(@Lines) WITH(LineNumber int,AccountId uniqueidentifier,PartyId uniqueidentifier,Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,ExistingReceivableId uniqueidentifier);
EXEC dbo.AccountingSourcesAccept @TenantId=@Tenant,@SourcesJson=@Sources,@Now=@Now,@Requirement=0;
SELECT CONVERT(bit,0);
'@
 foreach($pair in @{'@Tenant'=[guid]$plan.TenantId;'@Business'=[guid]$plan.BusinessId;'@Batch'=[guid]$plan.BatchId;
  '@Position'=[guid]$plan.ReceivableId;'@OriginalSource'=[guid]$plan.SourceDocumentId;'@Party'=[guid]$plan.PartyId;
  '@ReceivableAccount'=[guid]$plan.ReceivableAccountId;'@Counterpart'=[guid]$plan.CounterpartAccountId;
  '@Actor'=$ActorUserId;'@Effective'=[datetime]$plan.EffectiveOn;'@Now'=[datetimeoffset]::UtcNow;'@Hash'=$hash}.GetEnumerator()){
  $null=$command.Parameters.AddWithValue($pair.Key,$pair.Value)
 }
 $null=$command.Parameters.Add('@Sources',[Data.SqlDbType]::NVarChar,-1);$command.Parameters['@Sources'].Value=$sources
 $null=$command.Parameters.Add('@Lines',[Data.SqlDbType]::NVarChar,-1);$command.Parameters['@Lines'].Value=$validation
 $already=[bool]$command.ExecuteScalar()
 if($Apply){$transaction.Commit()}else{$transaction.Rollback()}
 [ordered]@{At=[datetimeoffset]::UtcNow;SourceCommit=(& git -C $repoRoot rev-parse HEAD).Trim();ActorUserId=$ActorUserId;
  PlanSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'data-plans/megafruver-historical-receivable-opening.json') -Algorithm SHA256).Hash;
  Applied=[bool]$Apply;AlreadyAccepted=$already;
  BatchId=$plan.BatchId;PayloadHash=$hash;ProductionDatabaseTouched=($Environment -eq 'prod');Committed=[bool]$Apply;DirectJournalOrPortfolioWrites=$false;
  Target=$connection.Database;Counterpart=$plan.CounterpartAccountCode;Amount=2000;RequiresCompleteOpeningReconciliation=$true}|
  ConvertTo-Json|Set-Content -Encoding utf8 $EvidencePath
 Write-Output "Reviewed historical opening: applied=$([bool]$Apply), already accepted=$already. Journal/portfolio effects still belong to the canonical processor."
}finally{
 if($transaction -and $transaction.Connection){$transaction.Rollback()}
 if($transaction){$transaction.Dispose()};$connection.Dispose();$access=$null
}
