import assert from "node:assert/strict";
import test from "node:test";
import { buildPreexistingReceivablesImport,parsePreexistingReceivablesCsv,preexistingReceivablesTemplate } from "./preexisting-receivables-template";

test("downloaded template can be filled and uploaded with two customer balances",()=>{
  const rows=parsePreexistingReceivablesCsv(preexistingReceivablesTemplate+
    "123456;FAC-1;2026-01-10;2026-02-10;50000;Saldo anterior\r\n"+
    "900123;FAC-2;2026-01-11;2026-02-11;100000;\r\n");
  assert.deepEqual(rows,[
    {identification:"123456",documentNumber:"FAC-1",issuedAt:"2026-01-10",dueDate:"2026-02-10",amount:50000,notes:"Saldo anterior"},
    {identification:"900123",documentNumber:"FAC-2",issuedAt:"2026-01-11",dueDate:"2026-02-11",amount:100000,notes:null}
  ]);
  const request=buildPreexistingReceivablesImport("business-id","counterpart-id",rows);
  assert.equal(request.businessId,"business-id");
  assert.equal(request.items.length,2);
  assert.notEqual(request.items[0].receivableId,request.items[1].receivableId);
  assert.deepEqual(request.items.map(({receivableId,...item})=>{assert.match(receivableId,/^[0-9a-f-]{36}$/);return item;}),[
    {customerId:null,customerIdentification:"123456",partySiteId:null,documentNumber:"FAC-1",issuedAt:"2026-01-10T12:00:00-05:00",dueDate:"2026-02-10T12:00:00-05:00",amount:50000,counterpartAccountId:"counterpart-id",notes:"Saldo anterior"},
    {customerId:null,customerIdentification:"900123",partySiteId:null,documentNumber:"FAC-2",issuedAt:"2026-01-11T12:00:00-05:00",dueDate:"2026-02-11T12:00:00-05:00",amount:100000,counterpartAccountId:"counterpart-id",notes:null}
  ]);
});

test("upload rejects missing required columns and empty templates",()=>{
  assert.throws(()=>parsePreexistingReceivablesCsv(preexistingReceivablesTemplate),/no contiene facturas/);
  assert.throws(()=>parsePreexistingReceivablesCsv("identificacion_cliente;saldo\n123;50"),/La plantilla requiere/);
});

test("upload identifies invalid rows, dates, balances and due dates",()=>{
  const row=(value:string)=>parsePreexistingReceivablesCsv(preexistingReceivablesTemplate+value);
  for(const value of [
    ";FAC-1;2026-01-10;2026-02-10;50;",
    "123;FAC-1;2026-02-30;2026-03-10;50;",
    "123;FAC-1;2026-03-10;2026-02-10;50;",
    "123;FAC-1;2026-01-10;2026-02-10;0;",
    "123;FAC-1;2026-01-10;2026-02-10;abc;"
  ]) assert.throws(()=>row(value),/fila 2 contiene datos inválidos/);
});

test("upload caps each batch at 100 invoices",()=>{
  const row="123;FAC-1;2026-01-10;2026-02-10;50;\n";
  assert.equal(parsePreexistingReceivablesCsv(preexistingReceivablesTemplate+row.repeat(100)).length,100);
  assert.throws(()=>parsePreexistingReceivablesCsv(preexistingReceivablesTemplate+row.repeat(101)),/máximo 100/);
});
