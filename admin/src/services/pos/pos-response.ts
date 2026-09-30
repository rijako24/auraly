export async function readPosResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
