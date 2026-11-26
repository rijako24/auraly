"use client";

import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import type { PayrollCatalogOption } from "@/services/api/payroll";

function percentageRate(value:string):string {
  if(value==="")return "";
  const numeric=Number(value),plain=String(numeric);
  if(!Number.isFinite(numeric))return "";
  if(plain.includes("e"))return String(numeric/100);
  const negative=plain.startsWith("-"),[integer,fraction=""]=plain.replace(/^-/u,"").split(".");
  const digits=integer.padStart(3,"0");
  // Shift decimal text rather than dividing a binary float (4.13 / 100
  // produces 0.041299999999999996 and violates the server's decimal scale).
  return `${negative?"-":""}${digits.slice(0,-2)}.${digits.slice(-2)}${fraction}`;
}

export function PayrollRuleParameterFields({definitions,values,onChange,disabled=false}:{
  definitions:PayrollCatalogOption[];values:Record<string,string>;
  onChange:(code:string,value:string)=>void;disabled?:boolean;
}){
  return <div className="grid min-w-0 grid-cols-1 gap-4 sm:grid-cols-2">{definitions.map(item=>{
    const rate=item.metadataCode==="Rate",currency=item.metadataCode==="COP";
    const unit=rate?"%":currency?"COP":item.metadataCode==="Days"?"Días":item.metadataCode==="Multiple"?"Veces":"Valor";
    const value=values[item.code];
    return <label key={item.optionId} className="min-w-0 space-y-2"><span className="block text-sm font-medium">{item.label} · {unit}</span>
      {rate?<Input type="number" step="0.000001" disabled={disabled} value={value===undefined||value===""?"":Number((Number(value)*100).toFixed(6))}
        onChange={event=>onChange(item.code,percentageRate(event.target.value))}/>:
      <FormattedNumberInput ariaLabel={`${item.label} · ${unit}`} value={value??""} disabled={disabled} kind={currency?"currency":"number"}
        onValueChange={amount=>onChange(item.code,amount===null?"":String(amount))}/>}
    </label>;
  })}</div>;
}
