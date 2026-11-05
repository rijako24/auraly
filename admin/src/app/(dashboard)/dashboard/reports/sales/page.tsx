import {Suspense} from "react";
import {SalesReportWorkspace} from "@/components/reports/sales-report-workspace";
import {PageLoading} from "@/components/ui/page-loading";
export default function SalesReportPage(){return <Suspense fallback={<PageLoading/>}><SalesReportWorkspace/></Suspense>;}
