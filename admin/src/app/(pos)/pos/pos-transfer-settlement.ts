import type { PosBankAccount } from "@/services/pos/pos-edge-client";

export function initialTransferBankAccountId(
  accounts: PosBankAccount[],
  existingBankAccountId?: string | null,
): string {
  return existingBankAccountId ??
    accounts.find((account) => account.isPrimary)?.bankAccountId ??
    "";
}
