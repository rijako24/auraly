const numericIdentificationTypes = new Set(["NIT", "CC", "CE", "PPT"]);
const nitWeights = [71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3] as const;

export function sanitizeTenantIdentification(type: string, value: string) {
  const normalized = value.toUpperCase();
  return (numericIdentificationTypes.has(type)
    ? normalized.replace(/\D/g, "")
    : normalized.replace(/[^A-Z0-9]/g, "")).slice(0, 32);
}

export function validateTenantIdentification(type: string, value: string, verificationDigit: string | null) {
  if (value.length < 3 || value.length > 32) return "Debe tener entre 3 y 32 caracteres.";
  if (numericIdentificationTypes.has(type) && !/^\d+$/.test(value)) return "Solo admite números sin puntos ni guiones.";
  if (!numericIdentificationTypes.has(type) && !/^[A-Z0-9]+$/.test(value)) return "Solo admite letras y números sin espacios ni signos.";
  if (type !== "NIT") return null;
  if (!/^\d$/.test(verificationDigit ?? "")) return "Escribe el dígito de verificación por separado.";
  return calculateNitVerificationDigit(value) === Number(verificationDigit)
    ? null
    : "El dígito de verificación no corresponde al NIT.";
}

export function calculateNitVerificationDigit(nit: string) {
  if (!/^\d{3,15}$/.test(nit)) return null;
  const offset = nitWeights.length - nit.length;
  const sum = [...nit].reduce((total, value, index) => total + Number(value) * nitWeights[offset + index], 0);
  const remainder = sum % 11;
  return remainder === 0 || remainder === 1 ? remainder : 11 - remainder;
}

export function supportsTenantVerificationDigit(type: string) {
  return numericIdentificationTypes.has(type);
}

export function calculateTenantVerificationDigit(type: string, identification: string) {
  return supportsTenantVerificationDigit(type)
    ? calculateNitVerificationDigit(identification)
    : null;
}
