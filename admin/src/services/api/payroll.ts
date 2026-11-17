import { apiClient } from "@/services/api/client";

export interface PayrollCatalogOption { optionId:string;catalogCode:string;code:string;label:string;description:string|null;metadataCode:string|null;dianCode:string|null;isActive:boolean;sortOrder:number }
export interface PayrollConcept { conceptId:string;code:string;name:string;natureCode:string;calculationMethodCode:string;treatmentCode:string;dianConceptCode:string|null;accountingCategoryCode:string;systemRoleCode:string|null;isSalaryBase:boolean;isSocialSecurityBase:boolean;isBenefitsBase:boolean;isTaxWithholdingBase:boolean;requiresDeductionAgreement:boolean;effectiveFrom:string;effectiveTo:string|null;isActive:boolean;rowVersion:string }
export interface PayrollEmployment { appliesTransportAllowance?:boolean; employmentId:string;partyId:string;businessId:string;contractNumber:string;employeeName:string;monthlySalary:number;isActive:boolean;employeeId:string|null;contractTypeOptionId:string;salaryTypeOptionId:string;payFrequencyOptionId:string;riskClassOptionId:string;workerTypeOptionId:string;workerSubtypeOptionId:string|null;paymentMethodOptionId:string;startDate:string;endDate:string|null;integralSalaryPercentage:number|null;bankAccountReference:string|null;bankOptionId:string|null;bankAccountTypeOptionId:string|null;bankAccountNumber:string|null;rowVersion:string }
export interface PayrollEmploymentPage { items:PayrollEmployment[];page:number;pageSize:number;totalCount:number;totalPages:number }
export interface PayrollParty { partyId:string;employeeId:string;identification:string;name:string }
export interface PayrollRuleParameter { code:string;numericValue:number;unitCode:string;description:string|null }
export interface PayrollRuleSet { ruleSetId:string;countryCode:string;code:string;name:string;effectiveFrom:string;effectiveTo:string|null;sourceReference:string;status:string;parameters:PayrollRuleParameter[];rowVersion:string }
export interface PayrollSettings { isEmployerExemptFromHealthSenaIcbf:boolean;electronicPayrollEnabled:boolean;rowVersion:string }
export interface FiscalIssuerOption { fiscalIssuerConfigurationId:string;version:number;legalName:string;softwareIdentificationCode:string;softwarePinSecretReference:string;environment:number;testSetId:string|null;isActive:boolean }
export interface ElectronicPayrollConfiguration { businessId:string;fiscalIssuerConfigurationId:string;softwareIdentificationCode:string;softwarePinSecretReference:string;testSetId:string|null;prefix:string;nextConsecutive:number;qrValidationUrl:string;isActive:boolean;rowVersion:string }
export interface PayrollDeductionAgreement { deductionAgreementId:string;employmentId:string;employeeName:string;conceptId:string;conceptName:string;authorityOptionId:string;beneficiaryPartyId:string|null;authorityName:string;referenceNumber:string;evidenceUrl:string;effectiveFrom:string;effectiveTo:string|null;authorizedTotal:number|null;installmentAmount:number|null;deductedToDate:number;priority:number;mustProtectMinimumNetPay:boolean;isActive:boolean;rowVersion:string }
export interface PayrollNovelty { rowVersion?:string;reasonId?:string|null; noveltyId:string;employmentId:string;employeeName:string;conceptId:string;conceptName:string;noveltyTypeOptionId:string;noveltyTypeName:string;deductionAgreementId:string|null;startDate:string;endDate:string;quantity:number;unitAmount:number|null;totalAmount:number;notes:string|null;evidenceUrl:string|null;status:string }
export interface PayrollPayableEmployee { payrollRunEmployeeId:string;paymentMethodOptionId:string;paymentMethodName:string;amount:number;bankDestinationComplete:boolean;employeeName:string }
export interface PayrollPaymentBatch { accountingStatus?:string|null; isPaid?:boolean; paymentMethodId?:string|null; accountId?:string|null; remainingEmployees:PayrollPayableEmployee[]; paymentBatchId:string;payrollRunId:string;paymentDate:string;paymentMethodOptionId:string;paymentMethodName:string;referenceNumber:string;status:string;employeeCount:number;totalAmount:number;rowVersion:string }
export interface PayrollReportColumn { key:string;label:string;format:"text"|"number"|"currency"|"date"|"datetime";align:"left"|"right" }
export interface PayrollReportDefinition { code:string;name:string;description:string;dataset:string;columns:PayrollReportColumn[];sortOrder:number }
export interface PayrollReportResult { definition:PayrollReportDefinition;from:string;to:string;rows:Array<Record<string,string|number|null>> }
export interface PayrollOptions { catalogs:Record<string,PayrollCatalogOption[]>;concepts:PayrollConcept[];employments:PayrollEmployment[];parties:PayrollParty[];ruleSets:PayrollRuleSet[];settings:PayrollSettings|null;electronicConfiguration:ElectronicPayrollConfiguration|null;fiscalIssuers:FiscalIssuerOption[];deductionAgreements:PayrollDeductionAgreement[];novelties:PayrollNovelty[];paymentBatches:PayrollPaymentBatch[];electronicPeriods:ElectronicPayrollPeriod[] }
export interface PayrollRunSummary { payrollRunId:string;runKind:string;periodStart:string;periodEnd:string;paymentDate:string;status:string;employeeCount:number;totalEarnings:number;totalDeductions:number;netPayable:number;rowVersion:string }
export interface PayrollRunLine { payrollRunLineId?:string;noveltyId?:string|null;deductionAgreementId?:string|null; lineNumber:number;conceptId:string;conceptCode:string;conceptName:string;natureCode:string;dianConceptCode:string|null;accountingCategoryCode:string;quantity:number;rate:number|null;baseAmount:number|null;amount:number;isEmployerCost:boolean;isSalaryBase:boolean }
export interface PayrollConceptWorkedDays { conceptId:string;baseDays:number;workedDays:number }
export interface PayrollRunEmployee { conceptWorkedDays?:PayrollConceptWorkedDays[];monthlySalary?:number|null;novelties?:PayrollNovelty[];identification?:string|null;contractNumber?:string|null;baseWorkedDays?:number|null;maximumContractDays?:number|null;periodDays?:number|null; payrollRunEmployeeId:string;employmentId:string;partyId:string;employeeName:string;workedDays:number;earnings:number;deductions:number;employerContributions:number;provisions:number;netPayable:number;lines:PayrollRunLine[] }
export interface PayrollRun extends PayrollRunSummary { isPartialCalculation?:boolean; payableEmployees:PayrollPayableEmployee[]; businessId:string;ruleSetId:string;originalPayrollRunId:string|null;calculationVersion:number;totalEmployerContributions:number;totalProvisions:number;employees:PayrollRunEmployee[] }

export interface SaveEmployment { appliesTransportAllowance?:boolean|null; employmentId:string;partyId:string;businessId:string;employeeId:string|null;contractTypeOptionId:string;salaryTypeOptionId:string;payFrequencyOptionId:string;riskClassOptionId:string;workerTypeOptionId:string;workerSubtypeOptionId:string|null;paymentMethodOptionId:string;contractNumber:string;startDate:string;endDate:string|null;monthlySalary:number;integralSalaryPercentage:number|null;bankAccountReference:string|null;bankOptionId:string|null;bankAccountTypeOptionId:string|null;bankAccountNumber:string|null;isActive:boolean;rowVersion:string|null }
export interface SaveConcept { conceptId:string;code:string;name:string;natureOptionId:string;calculationMethodOptionId:string;treatmentOptionId:string;dianConceptOptionId:string|null;accountingCategoryOptionId:string;systemRoleOptionId:string|null;isSalaryBase:boolean;isSocialSecurityBase:boolean;isBenefitsBase:boolean;isTaxWithholdingBase:boolean;requiresDeductionAgreement:boolean;effectiveFrom:string;effectiveTo:string|null;isActive:boolean;rowVersion:string|null }
export interface SaveRuleSet { ruleSetId:string;countryCode:string;code:string;name:string;effectiveFrom:string;effectiveTo:string|null;sourceReference:string;parameters:Array<{code:string;numericValue:number;unitCode:string;description:string|null}>;rowVersion:string|null }
export interface CreateRun { payrollRunId:string;businessId:string;ruleSetId:string;payFrequencyOptionId:string;runKind:string;originalPayrollRunId:string|null;periodStart:string;periodEnd:string;paymentDate:string }
export interface ElectronicPayrollDocument { electronicPayrollDocumentId:string;partyId:string;employeeName:string;documentKind:string;fiscalDocumentId:string|null;status:string;sourceHashHex:string }
export interface ElectronicPayrollPeriod { electronicPeriodId:string;year:number;month:number;status:string;documents:ElectronicPayrollDocument[];rowVersion:string }
export interface GenerateElectronicPayrollPeriod { electronicPeriodId:string;businessId:string;year:number;month:number }
export interface SaveElectronicPayrollConfiguration { businessId:string;fiscalIssuerConfigurationId:string;softwareIdentificationCode:string;softwarePinSecretReference:string;testSetId:string|null;prefix:string;nextConsecutive:number;qrValidationUrl:string;isActive:boolean;rowVersion:string|null }
export interface SaveDeductionAgreement { deductionAgreementId:string;employmentId:string;conceptId:string;authorityOptionId:string;beneficiaryPartyId:string|null;referenceNumber:string;evidenceUrl:string;effectiveFrom:string;effectiveTo:string|null;authorizedTotal:number|null;installmentAmount:number|null;priority:number;mustProtectMinimumNetPay:boolean;isActive:boolean;rowVersion:string|null }
export interface SavePayrollNovelty { rowVersion?:string|null; noveltyId:string;employmentId:string;conceptId:string;noveltyTypeOptionId:string;reasonId:string|null;deductionAgreementId:string|null;startDate:string;endDate:string;quantity:number;unitAmount:number|null;totalAmount:number;notes:string|null;evidenceUrl:string|null }
export interface CreatePayrollPayment { paymentMethodId?:string; paymentBatchId:string;payrollRunId:string;paymentMethodOptionId:string;paymentDate:string;referenceNumber:string }

export interface PayrollRunEmploymentCandidate { employmentId:string;employeeName:string;identification:string;contractNumber:string;startDate:string;endDate:string|null;payFrequencyName:string;isSelected:boolean }
export interface PayrollRunEmploymentPage { items:PayrollRunEmploymentCandidate[];page:number;pageSize:number;totalCount:number;eligibleCount:number;selectedCount:number;rowVersion:string }
export interface CalculatePayrollRun { recalculateEmploymentIds?:string[]; rowVersion?:string;selectAll?:boolean;selectionChanges?:Array<{employmentId:string;isSelected:boolean}>;workedDaysChanges?:Array<{employmentId:string;workedDays:number;conceptId?:string}> }

export const payrollApi = {
  options: () => apiClient.get<PayrollOptions>("/commerce/v1/payroll/options"),
  employments: (params:{page:number;pageSize:number;search?:string;isActive?:boolean;employmentId?:string}) => apiClient.get<PayrollEmploymentPage>("/commerce/v1/payroll/employments",params),
  runs: () => apiClient.get<PayrollRunSummary[]>("/commerce/v1/payroll/runs"),
  run: (id:string) => apiClient.get<PayrollRun>(`/commerce/v1/payroll/runs/${id}`),
  createRun: (request:CreateRun) => apiClient.post<PayrollRun>("/commerce/v1/payroll/runs",request),
  runEmployments: (id:string,params:{page:number;pageSize:number;search?:string;contractNumber?:string;sortBy?:string;sortDirection?:"asc"|"desc"}) => apiClient.get<PayrollRunEmploymentPage>(`/commerce/v1/payroll/runs/${id}/employments`,params),
  calculateRun: (id:string,request:CalculatePayrollRun={}) => apiClient.post<PayrollRun>(`/commerce/v1/payroll/runs/${id}/calculate`,request),
  approveRun: (id:string,rowVersion:string,key:string) => apiClient.postIdempotent(`/commerce/v1/payroll/runs/${id}/approve`,{rowVersion},key),
  saveEmployment: (request:SaveEmployment) => apiClient.put<PayrollEmployment>(`/commerce/v1/payroll/employments/${request.employmentId}`,request),
  saveConcept: (request:SaveConcept) => apiClient.put(`/commerce/v1/payroll/concepts/${request.conceptId}`,request),
  saveRuleSet: (request:SaveRuleSet) => apiClient.put<PayrollRuleSet>(`/commerce/v1/payroll/rule-sets/${request.ruleSetId}`,request),
  approveRuleSet: (id:string,rowVersion:string) => apiClient.post<PayrollRuleSet>(`/commerce/v1/payroll/rule-sets/${id}/approve`,{rowVersion}),
  retireRuleSet: (id:string,rowVersion:string) => apiClient.post<PayrollRuleSet>(`/commerce/v1/payroll/rule-sets/${id}/retire`,{rowVersion}),
  saveSettings: (request:{isEmployerExemptFromHealthSenaIcbf:boolean;electronicPayrollEnabled:boolean;rowVersion:string|null}) =>
    apiClient.put<PayrollSettings>("/commerce/v1/payroll/settings",request),
  saveElectronicConfiguration: (request:SaveElectronicPayrollConfiguration) =>
    apiClient.put<ElectronicPayrollConfiguration>("/commerce/v1/payroll/electronic-configuration",request),
  generateElectronicPeriod: (request:GenerateElectronicPayrollPeriod) =>
    apiClient.post<ElectronicPayrollPeriod>("/commerce/v1/payroll/electronic-periods",request),
  saveDeductionAgreement: (request:SaveDeductionAgreement) =>
    apiClient.put<PayrollDeductionAgreement>(`/commerce/v1/payroll/deduction-agreements/${request.deductionAgreementId}`,request),
  saveNovelty: (request:SavePayrollNovelty) => request.rowVersion
    ? apiClient.put<PayrollNovelty>(`/commerce/v1/payroll/novelties/${request.noveltyId}`,request)
    : apiClient.post<PayrollNovelty>("/commerce/v1/payroll/novelties",request),
  voidNovelty: (id:string,rowVersion:string) => apiClient.post<PayrollNovelty>(`/commerce/v1/payroll/novelties/${id}/void`,{rowVersion}),
  createPayment: (request:CreatePayrollPayment) => apiClient.post<PayrollPaymentBatch>("/commerce/v1/payroll/payments",request),
  reportDefinitions: () => apiClient.get<PayrollReportDefinition[]>("/commerce/v1/payroll/reports/definitions"),
  report: (code:string,from:string,to:string,partyId?:string) =>
    apiClient.get<PayrollReportResult>(`/commerce/v1/payroll/reports/${encodeURIComponent(code)}`,{from,to,partyId}),
};
