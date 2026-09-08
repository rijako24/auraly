export function shouldCompletePosEnrollment(
  completionPending: boolean,
  edgeStatus: string,
): boolean {
  return completionPending && edgeStatus !== "EnrollmentRequired";
}
