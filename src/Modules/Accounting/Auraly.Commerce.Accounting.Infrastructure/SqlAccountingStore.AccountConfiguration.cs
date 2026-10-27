using System.Data;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Domain;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    private static async Task<AccountingAccountView?> ReadAccountForEditAsync(SqlConnection connection,
        SqlTransaction transaction, Guid tenantId, Guid accountId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT AccountId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,RowVersion,
              ParentAccountId,FinancialFunction,BalanceSide,BusinessId,BankAccountId,PaymentMethodOptionId,ConfigurationLockedAt
            FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND AccountId=@AccountId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@AccountId", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAccount(reader) : null;
    }

    private async Task ValidateAccountConfigurationAsync(SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, Guid accountId, string code, string type, bool allowsPosting, bool requiresParty,
        AccountingAccountSemantics? semantics, CancellationToken ct, bool preserveLegacyParent = false)
    {
        if (semantics is null) return; // Existing clients retain their historical unclassified accounts.
        await using var command = new SqlCommand("""
            IF @Parent IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK)
              WHERE TenantId=@TenantId AND AccountId=@Parent AND AccountId<>@AccountId AND IsActive=1
                AND AllowsPosting=0 AND AccountType=@Type AND LEN(Code)<LEN(@Code)
                AND LEFT(@Code,LEN(Code))=Code AND (LEN(@Code)<=6 OR LEN(Code)>=6))
              THROW 51411,N'La cuenta padre debe ser una agrupación activa del mismo tipo y prefijo.',1;
            IF LEN(@Code)>6 AND @Parent IS NULL AND @PreserveLegacyParent=0
              THROW 51411,N'Selecciona la cuenta padre del auxiliar.',1;
            IF NOT EXISTS(SELECT 1 FROM reference.Options WITH(HOLDLOCK)
              WHERE CatalogCode=N'accounting-financial-function' AND Code=@Function AND IsActive=1)
              THROW 51411,N'La función financiera no está activa.',1;
            IF @BusinessId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Businesses WITH(HOLDLOCK)
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51411,N'La sede no está activa o no pertenece a esta empresa.',1;
            IF @BankId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM accounting.BankAccounts WITH(HOLDLOCK)
              WHERE TenantId=@TenantId AND BankAccountId=@BankId AND IsActive=1)
              THROW 51411,N'La cuenta bancaria no está activa o no pertenece a esta empresa.',1;
            IF @PaymentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM reference.Options WITH(HOLDLOCK)
              WHERE OptionId=@PaymentId AND CatalogCode=N'payment-method' AND IsActive=1)
              THROW 51411,N'El medio de pago no está activo.',1;
            SELECT Code FROM reference.Options WITH(HOLDLOCK) WHERE OptionId=@PaymentId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@PreserveLegacyParent", preserveLegacyParent);
        AddAccountConfigurationParameters(command, semantics);
        var payment = await command.ExecuteScalarAsync(ct) as string;
        var error = PucAuxiliaryDefinition.Validate(semantics.FinancialFunction, type, semantics.BalanceSide,
            allowsPosting, requiresParty, semantics.BankAccountId.HasValue, payment);
        if (error is not null) throw new AccountingValidationException(error);
    }

    private static void AddAccountConfigurationParameters(SqlCommand command, AccountingAccountSemantics? value)
    {
        command.Parameters.Add("@Parent", SqlDbType.UniqueIdentifier).Value = (object?)value?.ParentAccountId ?? DBNull.Value;
        command.Parameters.Add("@Function", SqlDbType.NVarChar, 64).Value = (object?)value?.FinancialFunction ?? DBNull.Value;
        command.Parameters.Add("@Balance", SqlDbType.NVarChar, 6).Value = (object?)value?.BalanceSide ?? DBNull.Value;
        command.Parameters.Add("@BusinessId", SqlDbType.UniqueIdentifier).Value = (object?)value?.BusinessId ?? DBNull.Value;
        command.Parameters.Add("@BankId", SqlDbType.UniqueIdentifier).Value = (object?)value?.BankAccountId ?? DBNull.Value;
        command.Parameters.Add("@PaymentId", SqlDbType.UniqueIdentifier).Value = (object?)value?.PaymentMethodOptionId ?? DBNull.Value;
    }
}
