import test from "node:test";
import assert from "node:assert/strict";
import { initialTransferBankAccountId } from "./pos-transfer-settlement";

const accounts = [
  { bankAccountId: "secondary", displayName: "Secundaria", bankName: "Banco", accountNumber: "1", accountTypeName: "Ahorros", isPrimary: false, rowVersion: "1" },
  { bankAccountId: "primary", displayName: "Principal", bankName: "Banco", accountNumber: "2", accountTypeName: "Ahorros", isPrimary: true, rowVersion: "2" },
];

test("a new transfer selects the primary bank account", () => {
  assert.equal(initialTransferBankAccountId(accounts), "primary");
});

test("editing a transfer preserves the account chosen by the cashier", () => {
  assert.equal(initialTransferBankAccountId(accounts, "secondary"), "secondary");
});
