export function getBackendUrl(): string {
  const backendUrl = process.env.NEXT_PUBLIC_API_URL?.trim();

  if (!backendUrl) {
    throw new Error("NEXT_PUBLIC_API_URL is required.");
  }

  return backendUrl.replace(/\/+$/, "");
}