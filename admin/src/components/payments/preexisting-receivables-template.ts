export type ParsedPreexistingReceivable={identification:string;documentNumber:string;issuedAt:string;dueDate:string;amount:number;notes:string|null};

export function buildPreexistingReceivablesImport(businessId:string,counterpartAccountId:string,rows:ParsedPreexistingReceivable[]){
  return {businessId,items:rows.map(row=>({
    receivableId:crypto.randomUUID(),customerId:null,customerIdentification:row.identification,
    partySiteId:null,documentNumber:row.documentNumber,
    issuedAt:`${row.issuedAt}T12:00:00-05:00`,dueDate:`${row.dueDate}T12:00:00-05:00`,
    amount:row.amount,counterpartAccountId,notes:row.notes
  }))};
}

export const preexistingReceivablesTemplate="\uFEFFidentificacion_cliente;numero_factura;fecha_emision;fecha_vencimiento;saldo;notas\r\n";

export function parsePreexistingReceivablesCsv(text:string):ParsedPreexistingReceivable[]{
  const lines=text.replace(/^\uFEFF/,"").split(/\r?\n/).filter(Boolean);
  if(lines.length<2)throw new Error("La plantilla no contiene facturas.");
  const header=lines[0].split(";").map(value=>value.trim().toLowerCase());
  const required=["identificacion_cliente","numero_factura","fecha_emision","fecha_vencimiento","saldo"];
  if(required.some(value=>!header.includes(value)))throw new Error(`La plantilla requiere: ${required.join(", ")}.`);
  if(lines.length>101)throw new Error("Cada importación admite máximo 100 facturas.");
  const index=(name:string)=>header.indexOf(name);
  return lines.slice(1).map((line,offset)=>{
    const cells=line.split(";").map(value=>value.trim());
    const rawAmount=cells[index("saldo")];const amount=Number(rawAmount);
    const issuedAt=cells[index("fecha_emision")],dueDate=cells[index("fecha_vencimiento")];
    if(!cells[index("identificacion_cliente")]||!cells[index("numero_factura")]||
       cells[index("numero_factura")].length>64||!validDate(issuedAt)||!validDate(dueDate)||dueDate<issuedAt||
       !/^\d+(?:\.\d{1,4})?$/.test(rawAmount)||!Number.isFinite(amount)||amount<=0||amount>=1_000_000_000_000_000)
      throw new Error(`La fila ${offset+2} contiene datos inválidos.`);
    return {identification:cells[index("identificacion_cliente")],documentNumber:cells[index("numero_factura")],
      issuedAt,dueDate,amount,notes:index("notas")>=0?cells[index("notas")]||null:null};
  });
}

function validDate(value:string){
  if(!/^\d{4}-\d{2}-\d{2}$/.test(value))return false;
  const parsed=new Date(`${value}T12:00:00Z`);
  return !Number.isNaN(parsed.getTime())&&parsed.toISOString().startsWith(value);
}
