import assert from "node:assert/strict";
import { it } from "node:test";
import { payrollReceiptRows } from "./payroll-receipt";

it("separates workers and periods, preserves the frozen totals and omits employer costs from worker pay",()=>{
  const header={payrollRunEmployeeId:"first",employeeName:"Ana",identification:"123",contractNumber:"A",periodStart:"2026-11-01",periodEnd:"2026-11-30",paymentDate:"2026-11-30",monthlySalary:1000,workedDays:30,earnings:1000,deductions:80,netPayable:920};
  const rows=payrollReceiptRows([
    {...header,id:"salary",conceptCode:"S",conceptName:"Salario",nature:"Earning",amount:1000,quantity:30},
    {...header,id:"health",conceptCode:"H",conceptName:"Salud",nature:"Deduction",amount:80,quantity:1,percentageRate:0.04},
    {...header,id:"employer",conceptCode:"E",conceptName:"Aporte patronal",nature:"EmployerContribution",amount:100,quantity:1},
    {...header,payrollRunEmployeeId:"second",employeeName:"Beatriz",id:"second-salary",conceptCode:"S",conceptName:"Salario",nature:"Earning",amount:1000,quantity:30},
  ],true);
  assert.equal(rows.filter(row=>row.__groupLevel===1).length,2);
  assert.equal(rows.filter(row=>row.__groupLevel===3).length,2);
  assert.deepEqual(rows.find(row=>row.id==="salary"),{id:"salary",concept:"S · Salario",quantity:30,percentageRate:undefined,earnings:1000,deductions:null});
  assert.equal(rows.find(row=>row.id==="health")?.deductions,80);
  assert.equal(rows.find(row=>row.id==="health")?.percentageRate,0.04);
  assert.equal(rows.some(row=>row.id==="employer"),false);
  assert.equal(rows.find(row=>row.id==="first-net")?.earnings,920);
  assert.match(String(rows.find(row=>row.id==="first-payment")?.__group),/Pago previsto: 30 de nov\.? de 2026/);
  const paid=payrollReceiptRows([{...header,paidDate:"2026-12-01",id:"paid",nature:"Earning",conceptCode:"SAL",conceptName:"Salario",quantity:30,amount:1000}],false);
  assert.match(String(paid.find(row=>row.id==="first-payment")?.__group),/Fecha de pago: 1 de dic\.? de 2026/);
  assert.equal(paid.find(row=>row.id==="first-net")?.concept,"Neto pagado");
  assert.equal(payrollReceiptRows([{...header,id:"empty",nature:"Provision"}],false).some(row=>row.__groupLevel===3),false);
  assert.throws(()=>payrollReceiptRows([{id:"no-result"}],false),/no identifica/);
});
