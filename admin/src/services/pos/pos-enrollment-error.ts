export async function posEnrollmentProblemDetail(response: Response): Promise<string> {
  const raw = await response.text();
  if (response.status >= 500)
    return "Auraly no pudo completar la preparación de esta caja. Inténtalo nuevamente; si continúa, informa al administrador.";
  try {
    const problem = JSON.parse(raw) as {
      detail?: string;
      title?: string;
      message?: string;
    };
    return problem.detail || problem.message || problem.title || raw;
  } catch {
    return raw || response.statusText;
  }
}
