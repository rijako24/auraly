export function canOpenFiscalResolutionChange(
  canManage: boolean,
  habilitationAccepted: boolean,
  deviceIsActive: boolean,
  saving: boolean,
) {
  return canManage && habilitationAccepted && deviceIsActive && !saving;
}
