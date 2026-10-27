namespace Auraly.Commerce.Accounting.Domain;

public static class PucAuxiliaryDefinition
{
    public static string? Validate(string function, string accountType, string balanceSide,
        bool allowsPosting, bool requiresParty, bool hasBank, string? paymentCode)
    {
        if (balanceSide is not ("Debit" or "Credit")) return "Selecciona la naturaleza débito o crédito.";
        var compatible = function switch
        {
            "General" => true,
            "Receivable" or "SupplierAdvance" or "Cash" or "Bank" or "CardClearing" or "UnallocatedPurchaseCost" => accountType == "Asset" && balanceSide == "Debit",
            "Payable" or "CustomerAdvance" or "CreditCardLiability" => accountType == "Liability" && balanceSide == "Credit",
            _ => false
        };
        if (!compatible) return "La función financiera no corresponde al tipo y naturaleza de la cuenta.";
        if (!allowsPosting && function != "General") return "Las agrupaciones no ejecutan funciones financieras.";
        if (RequiresParty(function) && !requiresParty) return "Esta función financiera requiere tercero en todas sus partidas.";
        if (function == "Bank" && requiresParty) return "El auxiliar bancario no debe exigir tercero en todas sus partidas.";
        if (function is "Bank" or "CreditCardLiability" && !hasBank)
            return "Esta función requiere identificar la cuenta bancaria.";
        if (function is not ("Bank" or "CreditCardLiability" or "CardClearing") && hasBank)
            return "Esta función no admite una dimensión bancaria.";
        if (function is not ("Cash" or "Bank" or "CreditCardLiability" or "CardClearing") && paymentCode is not null)
            return "Solo las cuentas de tesorería o compensación admiten medio de pago.";
        if (paymentCode == "Credit" || (paymentCode is not null && (function == "Cash") != (paymentCode == "Cash")))
            return "El medio de pago no corresponde a la función de la cuenta.";
        return null;
    }

    public static bool RequiresParty(string function) => function is
        "Receivable" or "Payable" or "SupplierAdvance" or "CustomerAdvance";

}
