/**
 * Canonical typography shared by every 58/80 mm POS receipt rendered in the
 * browser. Document-specific templates only decide which business rows exist.
 */
export const posReceiptTypographyCss = `
  body { text-transform: none; }
  header, footer { text-align: center; }
  .brand-logo { margin-left: auto; margin-right: auto; }
  strong, b { font-weight: 800; }
`;
