import { SalesReturnWorkspace } from "@/components/returns/sales-return-workspace";

export default async function SalesReturnsPage({searchParams}:{searchParams:Promise<{document?:string|string[]}>}) {
  const {document}=await searchParams;
  return <SalesReturnWorkspace initialSearch={typeof document==="string"?document.slice(0,160):""} />;
}
