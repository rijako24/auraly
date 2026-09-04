type BinaryRequest = Pick<Request, "arrayBuffer">;

export async function readBackendProxyBody(
  request: BinaryRequest,
  method: string,
): Promise<ArrayBuffer | undefined> {
  if (method === "GET" || method === "HEAD") return undefined;

  const body = await request.arrayBuffer();
  return body.byteLength === 0 ? undefined : body;
}
