// Contracts of the Brocal API: camelCase JSON, enums as camelCase strings, dates as ISO strings
// (DateOnly = yyyy-MM-dd, DateTimeOffset = full ISO), ids as GUID strings.

export type Guid = string;
export type IsoDate = string;
export type IsoDateTime = string;

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Lookup {
  id: Guid;
  code: string;
  name: string;
  isActive: boolean;
  parentId?: Guid | null;
  extra?: string | null;
}

export interface Kpi {
  label: string;
  value: number;
  format?: string | null;
}

export interface NamedRef {
  id: Guid;
  code: string;
  name: string;
}

export interface GeoPoint {
  latitude: number;
  longitude: number;
  accuracyMeters?: number | null;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

// ---- Enums ---------------------------------------------------------------------------------------

export type FileCategory = 'other' | 'logo' | 'photo' | 'signature' | 'document' | 'receipt' | 'generatedPdf' | 'invoiceXml' | 'export';
export type TenantStatus = 'trial' | 'active' | 'suspended' | 'cancelled';
export type MembershipStatus = 'invited' | 'active' | 'disabled';
export type ColorSchemePreference = 'system' | 'light' | 'dark';
export type CornerStyle = 'rounded' | 'soft' | 'square';
export type ModuleCategory = 'operations' | 'assets' | 'compliance' | 'fiscal' | 'management' | 'platform';
export type TicketStatus = 'draft' | 'signed' | 'approved' | 'invoiced' | 'cancelled';
export type WorkOrderStatus = 'scheduled' | 'dispatched' | 'inProgress' | 'completed' | 'cancelled';
export type WorkOrderPriority = 'low' | 'normal' | 'high' | 'urgent';
export type WorkOrderAction = 'dispatch' | 'start' | 'complete' | 'cancel';
export type PriceSource = 'contract' | 'customer' | 'general' | 'basePrice' | 'none' | 'manual';
export type FormFieldType = 'text' | 'number' | 'select' | 'checkbox' | 'date' | 'time' | 'textArea';
export type ContractStatus = 'draft' | 'active' | 'suspended' | 'closed';
export type SiteType = 'well' | 'field' | 'facility' | 'yard' | 'other';
export type UnitType = 'tankTruck' | 'tractor' | 'trailer' | 'vacuumTruck' | 'pumpUnit' | 'crane' | 'pickup' | 'equipment' | 'other';
export type UnitStatus = 'available' | 'operating' | 'workshop' | 'outOfService';
export type MeterKind = 'odometer' | 'hourMeter' | 'both';
export type MaintenanceTrigger = 'kilometers' | 'hours' | 'days';
export type MaintenanceType = 'preventive' | 'corrective';
export type MaintenanceOrderStatus = 'open' | 'inProgress' | 'waitingParts' | 'closed' | 'cancelled';
export type FuelPaymentMethod = 'cash' | 'card' | 'voucher' | 'credit';
export type ComplianceStatus = 'noExpiry' | 'valid' | 'expiringSoon' | 'expired' | 'missing';
export type DocumentSubject = 'unit' | 'employee';
export type ItemCategory = 'sparePart' | 'consumable' | 'tool' | 'ppe' | 'chemical' | 'other';
export type MovementType = 'receipt' | 'issue' | 'transfer' | 'adjustment' | 'maintenance' | 'crewIssue' | 'return';
export type EmployeeCategory = 'operator' | 'helper' | 'supervisor' | 'technician' | 'driver' | 'hse' | 'office' | 'other';
export type AttendanceStatus = 'present' | 'absent' | 'rest' | 'sickLeave' | 'vacation' | 'travel' | 'training';
export type ExpenseCategory = 'meals' | 'lodging' | 'tolls' | 'fuel' | 'transport' | 'materials' | 'other';
export type ExpenseStatus = 'draft' | 'submitted' | 'approved' | 'rejected' | 'paid';
export type ExpenseAction = 'submit' | 'approve' | 'reject' | 'markPaid';
export type IncidentType = 'incident' | 'accident' | 'nearMiss' | 'unsafeCondition' | 'unsafeAct' | 'spill' | 'equipmentDamage' | 'environmental' | 'vehicleAccident';
export type IncidentSeverity = 'low' | 'medium' | 'high' | 'critical';
export type IncidentStatus = 'reported' | 'investigating' | 'closed';
export type ActionStatus = 'pending' | 'inProgress' | 'done';
export type InspectionCategory = 'unitPreUse' | 'ppe' | 'site' | 'audit' | 'other';
export type CheckResult = 'ok' | 'notOk' | 'notApplicable';
export type InvoiceStatus = 'draft' | 'stamped' | 'cancellationPending' | 'cancelled';
export type CfdiType = 'income' | 'transfer';
export type TripStatus = 'draft' | 'stamped' | 'cancelled';
export type PaymentComplementStatus = 'notRequired' | 'pending' | 'stamped';
export type EstimateStatus = 'draft' | 'submitted' | 'approved' | 'copadeAssigned' | 'invoiced' | 'paid' | 'rejected';
export type EstimateAction = 'submit' | 'approve' | 'reject' | 'assignCopade';
export type ContactChannel = 'phone' | 'email' | 'whatsApp' | 'visit' | 'portal';
export type QuoteStatus = 'draft' | 'sent' | 'accepted' | 'rejected' | 'expired' | 'converted';
export type QuoteAction = 'send' | 'accept' | 'reject';
export type AuditAction = 'created' | 'updated' | 'deleted' | 'statusChanged' | 'signed' | 'cancelled' | 'reopened' | 'stamped' | 'exported' | 'accessed';
export type NotificationEvent =
  | 'ticketSigned' | 'ticketCancelled' | 'workOrderAssigned' | 'unitReleased' | 'permitExpiring' | 'documentExpiring' | 'maintenanceDue'
  | 'invoiceStamped' | 'invoiceOverdue' | 'incidentReported' | 'quoteAccepted' | 'expenseSubmitted' | 'fuelAnomaly' | 'lowStock' | 'uninvoicedTickets'
  | 'approvalRequested' | 'approvalDecided' | 'businessRuleAlert' | 'contractExpiring' | 'correctiveActionDue' | 'apiKeyExpiring' | 'documentMissing';
export type NotificationSeverity = 'info' | 'success' | 'warning' | 'critical';
export type OutboxStatus = 'pending' | 'sent' | 'failed' | 'simulated';
export type OutboxChannel = 'email' | 'whatsApp';
export type AccountingExportFormat = 'excel' | 'csv';
export type TenantStatusAction = 'activate' | 'startTrial' | 'suspend' | 'cancel';
export type ReportFormat = 'pdf' | 'excel';

// ---- Session and platform ------------------------------------------------------------------------

export interface ThemePalette {
  primary: string;
  secondary: string;
  tertiary: string;
  neutral: string;
  error: string;
  success: string;
  warning: string;
  info: string;
}

export interface Branding {
  useChronoLithTheme: boolean;
  palette: ThemePalette;
  appName: string;
  tagline: string | null;
  logoFileId: Guid | null;
  logoUrl: string | null;
  logoDarkFileId: Guid | null;
  logoDarkUrl: string | null;
  density: number;
  defaultColorScheme: ColorSchemePreference;
  fontFamily: string;
  corners: CornerStyle;
  documentFooter: string | null;
}

export interface CurrentUser {
  id: string;
  username: string;
  email: string | null;
  fullName: string | null;
  isPlatformAdmin: boolean;
  canAccess: boolean;
}

export interface MyTenant {
  id: Guid;
  slug: string;
  name: string;
  appName: string;
  logoUrl: string | null;
  status: TenantStatus;
  isTenantAdmin: boolean;
  isPortalUser: boolean;
}

export interface CurrentMembership {
  id: Guid | null;
  fullName: string;
  isTenantAdmin: boolean;
  employeeId: Guid | null;
  customerId: Guid | null;
  customerName: string | null;
}

export interface SettingsSummary {
  timeZone: string;
  currency: string;
  defaultLanguage: string;
  expiryWarningDays: number;
  minTicketPhotos: number;
  requireCustomerSignature: boolean;
  requireApprovalBeforeInvoice: boolean;
  defaultVatRate: number;
}

export interface CurrentTenant {
  id: Guid;
  slug: string;
  name: string;
  legalName: string | null;
  status: TenantStatus;
  plan: string;
  trialEndsOn: IsoDate | null;
  branding: Branding;
  settings: SettingsSummary;
  enabledModules: string[];
  permissions: string[];
  membership: CurrentMembership;
  fiscalComplete: boolean;
}

export interface MyContext {
  user: CurrentUser;
  tenants: MyTenant[];
  tenant: CurrentTenant | null;
  mode: string;
  version: string;
}

export interface ChronoLithTheme {
  name: string;
  palette: ThemePalette;
  appName: string | null;
  logoUrl: string | null;
  density: number;
  defaultColorScheme: string;
}

export interface PermissionInfo {
  code: string;
  name: string;
  nameEn: string;
  description: string | null;
  descriptionEn: string | null;
}

export interface ModuleInfo {
  code: string;
  number: number;
  category: ModuleCategory;
  name: string;
  nameEn: string;
  description: string;
  descriptionEn: string;
  icon: string;
  isCore: boolean;
  dependsOn: string[];
  permissions: PermissionInfo[];
}

export interface TenantQuotas {
  maxUsers: number | null;
  maxPortalUsers: number | null;
  maxUnits: number | null;
  maxEmployees: number | null;
  maxStorageMb: number | null;
  maxTicketsPerMonth: number | null;
  maxApiKeys: number | null;
}

export interface TenantSettings {
  timeZone: string;
  currency: string;
  defaultLanguage: string;
  ticketPrefix: string;
  workOrderPrefix: string;
  quotePrefix: string;
  maintenancePrefix: string;
  incidentPrefix: string;
  tripPrefix: string;
  estimatePrefix: string;
  expensePrefix: string;
  expiryWarningDays: number;
  uninvoicedAlertDays: number;
  minTicketPhotos: number;
  requireCustomerSignature: boolean;
  requireApprovalBeforeInvoice: boolean;
  defaultVatRate: number;
  fuelEfficiencyTolerancePercent: number;
}

export interface FiscalProfile {
  rfc: string | null;
  legalName: string | null;
  taxRegime: string | null;
  postalCode: string | null;
  invoiceSeries: string;
  paymentSeries: string;
  pacProvider: string;
  pacUser: string | null;
  hasPacSecret: boolean;
  pacSandbox: boolean;
  isComplete: boolean;
}

export interface Usage {
  users: number;
  portalUsers: number;
  units: number;
  employees: number;
  storageMb: number;
  ticketsThisMonth: number;
  apiKeys: number;
}

export interface CompanySettings {
  id: Guid;
  slug: string;
  name: string;
  legalName: string | null;
  taxId: string | null;
  plan: string;
  status: TenantStatus;
  branding: Branding;
  settings: TenantSettings;
  fiscal: FiscalProfile;
  quotas: TenantQuotas;
  usage: Usage;
  enabledModules: string[];
  pacProviders: string[];
}

export interface TenantSummary {
  id: Guid;
  slug: string;
  name: string;
  taxId: string | null;
  status: TenantStatus;
  plan: string;
  trialEndsOn: IsoDate | null;
  moduleCount: number;
  users: number;
  logoUrl: string | null;
  primaryColor: string;
  createdAt: IsoDateTime;
}

export interface TenantDetail {
  id: Guid;
  slug: string;
  name: string;
  legalName: string | null;
  taxId: string | null;
  contactEmail: string | null;
  contactPhone: string | null;
  plan: string;
  status: TenantStatus;
  statusReason: string | null;
  trialEndsOn: IsoDate | null;
  branding: Branding;
  quotas: TenantQuotas;
  settings: TenantSettings;
  fiscal: FiscalProfile;
  enabledModules: string[];
  usage: Usage;
  createdAt: IsoDateTime;
}

export interface Member {
  id: Guid;
  email: string;
  fullName: string;
  status: MembershipStatus;
  isTenantAdmin: boolean;
  roleIds: Guid[];
  roleNames: string[];
  employeeId: Guid | null;
  employeeName: string | null;
  customerId: Guid | null;
  customerName: string | null;
  isBound: boolean;
  invitedAt: IsoDateTime | null;
  lastAccessAt: IsoDateTime | null;
}

export interface Role {
  id: Guid;
  name: string;
  description: string | null;
  templateKey: string | null;
  permissions: string[];
  members: number;
}

export interface UploadedFile {
  id: Guid;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  url: string;
}

export interface AuditEntry {
  id: Guid;
  occurredAt: IsoDateTime;
  userId: string | null;
  userName: string;
  action: AuditAction;
  entityType: string;
  entityId: Guid | null;
  summary: string | null;
  changes: string | null;
  reason: string | null;
  ipAddress: string | null;
}

export interface UserNotification {
  id: Guid;
  event: NotificationEvent;
  severity: NotificationSeverity;
  title: string;
  body: string;
  link: string | null;
  createdAt: IsoDateTime;
  isRead: boolean;
}

export interface MyNotifications {
  unread: number;
  items: UserNotification[];
}

export interface NotificationRule {
  event: NotificationEvent;
  id: Guid | null;
  isEnabled: boolean;
  channels: string;
  roleIds: Guid[];
  emails: string[];
  phones: string[];
  notifyCustomerContacts: boolean;
  notifyAssignees: boolean;
  daysBefore: number | null;
  isDefault: boolean;
}

export interface ChannelSettings {
  useOwnSmtp: boolean;
  smtpHost: string | null;
  smtpPort: number;
  smtpUseSsl: boolean;
  smtpUser: string | null;
  hasSmtpPassword: boolean;
  fromAddress: string | null;
  fromName: string | null;
  whatsAppEnabled: boolean;
  whatsAppPhoneNumberId: string | null;
  hasWhatsAppToken: boolean;
  whatsAppTemplate: string;
  whatsAppLanguage: string;
}

export interface OutboxMessage {
  id: Guid;
  channel: OutboxChannel;
  recipient: string;
  subject: string;
  event: NotificationEvent;
  status: OutboxStatus;
  attempts: number;
  createdAt: IsoDateTime;
  sentAt: IsoDateTime | null;
  lastError: string | null;
}

export interface ApiKeyInfo {
  id: Guid;
  name: string;
  prefix: string;
  scopes: string[];
  expiresOn: IsoDate | null;
  lastUsedAt: IsoDateTime | null;
  isRevoked: boolean;
  createdAt: IsoDateTime;
  createdBy: string | null;
}

export interface CreatedApiKey {
  key: ApiKeyInfo;
  plainValue: string;
}

export interface ExportDataset {
  key: string;
  name: string;
  module: string;
}

export interface AccountingSettings {
  customersAccount: string;
  revenueAccount: string;
  vatTransferredAccount: string;
  vatPendingAccount: string;
  bankAccount: string;
  format: AccountingExportFormat;
}

// ---- Commercial ------------------------------------------------------------------------------------

export interface CustomerContact {
  name: string;
  position: string | null;
  email: string | null;
  phone: string | null;
  notifyTickets: boolean;
  notifyInvoices: boolean;
  notifyCollections: boolean;
}

export interface FormFieldDefinition {
  key: string;
  label: string;
  type: FormFieldType;
  required: boolean;
  options: string[] | null;
  unit: string | null;
  order: number;
}

export interface ServiceType {
  id: Guid;
  code: string;
  name: string;
  description: string | null;
  icon: string;
  color: string | null;
  requiresSignature: boolean;
  minPhotos: number;
  fields: FormFieldDefinition[];
  defaultServiceIds: Guid[];
  isActive: boolean;
}

export interface ResolvedPrice {
  serviceId: Guid;
  code: string;
  name: string;
  unitName: string;
  vatRate: number;
  unitPrice: number | null;
  source: PriceSource;
  priceListId: Guid | null;
  priceListName: string | null;
}

// ---- Tickets and work orders -----------------------------------------------------------------------

export interface TicketData {
  customerId: Guid;
  contractId: Guid | null;
  siteId: Guid;
  serviceTypeId: Guid;
  workOrderId: Guid | null;
  serviceDate: IsoDate;
  startedAt: IsoDateTime | null;
  finishedAt: IsoDateTime | null;
  unitIds: Guid[];
  crewId: Guid | null;
  employeeIds: Guid[];
  description: string | null;
  observations: string | null;
  customerRepresentative: string | null;
  answers: Record<string, string | null>;
  captureLocation: GeoPoint | null;
}

export interface TicketLineInput {
  serviceId: Guid;
  quantity: number;
  unitPrice: number | null;
  description: string | null;
}

export interface UnitMeterInput {
  unitId: Guid;
  odometer: number | null;
  hours: number | null;
}

export interface SaveTicketResult {
  id: Guid;
  folio: string;
  created: boolean;
  warnings: string[];
}

export interface TicketSummary {
  id: Guid;
  folio: string;
  status: TicketStatus;
  serviceDate: IsoDate;
  customerId: Guid;
  customerName: string | null;
  siteId: Guid;
  siteName: string | null;
  serviceTypeId: Guid;
  serviceTypeName: string | null;
  serviceTypeColor: string | null;
  workOrderId: Guid | null;
  workOrderFolio: string | null;
  total: number;
  currency: string;
  capturedByName: string | null;
  signedAt: IsoDateTime | null;
  hasSignature: boolean;
  photoCount: number;
  invoiceId: Guid | null;
  createdAt: IsoDateTime;
}

export interface TicketLine {
  id: Guid;
  order: number;
  serviceId: Guid;
  serviceCode: string | null;
  description: string;
  quantity: number;
  unitName: string;
  unitPrice: number;
  priceSource: PriceSource;
  vatRate: number;
  amount: number;
}

export interface TicketPhoto {
  id: Guid;
  fileId: Guid;
  url: string;
  caption: string | null;
  takenAt: IsoDateTime;
  latitude: number | null;
  longitude: number | null;
}

export interface TicketDetail {
  id: Guid;
  folio: string;
  clientId: Guid | null;
  status: TicketStatus;
  customerId: Guid;
  customerName: string | null;
  contractId: Guid | null;
  contractName: string | null;
  siteId: Guid;
  siteName: string | null;
  serviceTypeId: Guid;
  serviceTypeName: string | null;
  workOrderId: Guid | null;
  workOrderFolio: string | null;
  serviceDate: IsoDate;
  startedAt: IsoDateTime | null;
  finishedAt: IsoDateTime | null;
  units: NamedRef[];
  crewId: Guid | null;
  crewName: string | null;
  employees: NamedRef[];
  description: string | null;
  observations: string | null;
  customerRepresentative: string | null;
  answers: Record<string, string | null>;
  captureLatitude: number | null;
  captureLongitude: number | null;
  lines: TicketLine[];
  photos: TicketPhoto[];
  currency: string;
  subtotal: number;
  vat: number;
  total: number;
  capturedByName: string | null;
  signerName: string | null;
  signerPosition: string | null;
  signatureUrl: string | null;
  signedAt: IsoDateTime | null;
  approvedAt: IsoDateTime | null;
  approvedBy: string | null;
  invoiceId: Guid | null;
  invoiceReference: string | null;
  cancelledAt: IsoDateTime | null;
  cancellationReason: string | null;
  pdfUrl: string | null;
  createdAt: IsoDateTime;
  createdBy: string | null;
  updatedAt: IsoDateTime | null;
  updatedBy: string | null;
}

export interface TicketCostRow {
  employeeId: Guid;
  code: string;
  name: string;
  position: string | null;
  daysWorked: number;
  hours: number;
  overtimeHours: number;
  absences: number;
  rest: number;
  leave: number;
  hourlyCost: number | null;
  cost: number | null;
}

export interface TicketCost {
  ticketId: Guid;
  folio: string;
  revenue: number;
  laborHours: number;
  laborCost: number;
  margin: number;
  people: TicketCostRow[];
}

export interface PlannedService {
  serviceId: Guid;
  quantity: number;
}

export interface WorkOrderData {
  customerId: Guid;
  contractId: Guid | null;
  siteId: Guid;
  serviceTypeId: Guid;
  description: string;
  priority: WorkOrderPriority;
  scheduledStart: IsoDateTime;
  scheduledEnd: IsoDateTime;
  unitIds: Guid[];
  crewId: Guid | null;
  employeeIds: Guid[];
  plannedServices: PlannedService[];
  instructions: string | null;
  customerReference: string | null;
}

export interface WorkOrderSummary {
  id: Guid;
  folio: string;
  status: WorkOrderStatus;
  priority: WorkOrderPriority;
  customerId: Guid;
  customerName: string | null;
  customerColor: string | null;
  siteId: Guid;
  siteName: string | null;
  serviceTypeId: Guid;
  serviceTypeName: string | null;
  serviceTypeColor: string | null;
  description: string;
  scheduledStart: IsoDateTime;
  scheduledEnd: IsoDateTime;
  unitIds: Guid[];
  crewId: Guid | null;
  crewName: string | null;
  customerReference: string | null;
  ticketCount: number;
}

export interface PlannedServiceInfo {
  serviceId: Guid;
  serviceName: string | null;
  unitName: string | null;
  quantity: number;
}

export interface WorkOrderDetail {
  summary: WorkOrderSummary;
  contractId: Guid | null;
  contractName: string | null;
  instructions: string | null;
  units: NamedRef[];
  employees: NamedRef[];
  plannedServices: PlannedServiceInfo[];
  quoteId: Guid | null;
  quoteFolio: string | null;
  dispatchedAt: IsoDateTime | null;
  startedAt: IsoDateTime | null;
  completedAt: IsoDateTime | null;
  cancellationReason: string | null;
  tickets: TicketSummary[];
  warnings: string[];
  createdAt: IsoDateTime;
  createdBy: string | null;
}

export interface SaveWorkOrderResult {
  id: Guid;
  folio: string;
  warnings: string[];
}

export interface BoardUnit {
  id: Guid;
  code: string;
  name: string;
  type: UnitType;
  status: UnitStatus;
}

export interface DispatchBoard {
  from: IsoDateTime;
  to: IsoDateTime;
  units: BoardUnit[];
  orders: WorkOrderSummary[];
}

export interface TicketDraft {
  data: TicketData;
  lines: TicketLineInput[];
  prices: ResolvedPrice[];
}

// ---- Fleet ----------------------------------------------------------------------------------------

export interface UnitInfo {
  id: Guid;
  code: string;
  name: string;
  type: UnitType;
  status: UnitStatus;
  brand: string | null;
  model: string | null;
  year: number | null;
  plates: string | null;
  serialNumber: string | null;
  meterKind: MeterKind;
  currentOdometer: number;
  currentHours: number;
  metersUpdatedAt: IsoDateTime | null;
  capacity: number | null;
  capacityUnit: string | null;
  fuelType: string | null;
  fuelTankLiters: number | null;
  expectedEfficiency: number | null;
  vehicleConfig: string | null;
  grossWeightTons: number | null;
  sctPermitType: string | null;
  sctPermitNumber: string | null;
  insurerName: string | null;
  insurancePolicy: string | null;
  environmentalInsurer: string | null;
  environmentalPolicy: string | null;
  defaultOperatorId: Guid | null;
  defaultOperatorName: string | null;
  photoFileId: Guid | null;
  acquiredOn: IsoDate | null;
  acquisitionCost: number | null;
  lastLatitude: number | null;
  lastLongitude: number | null;
  lastPositionAt: IsoDateTime | null;
  notes: string | null;
  isActive: boolean;
}

export interface UnitHistoryItem {
  at: IsoDateTime;
  kind: string;
  title: string;
  detail: string | null;
  by: string | null;
  link: string | null;
}

export interface UnitDocumentStatus {
  documentTypeId: Guid;
  documentType: string;
  number: string | null;
  expiresOn: IsoDate | null;
  status: ComplianceStatus;
  daysLeft: number | null;
}

export interface UnitOverview {
  unit: UnitInfo;
  history: UnitHistoryItem[];
  kpis: Kpi[];
  documents: UnitDocumentStatus[];
}

export interface MaintenanceDue {
  planId: Guid;
  planName: string;
  trigger: MaintenanceTrigger;
  unitId: Guid;
  unitCode: string;
  unitName: string;
  unitStatus: UnitStatus;
  nextAt: number | null;
  remaining: number | null;
  nextOn: IsoDate | null;
  isDue: boolean;
  isOverdue: boolean;
  hasOpenOrder: boolean;
}

export interface MaintenanceOrderSummary {
  id: Guid;
  folio: string;
  unitId: Guid;
  unitCode: string | null;
  unitName: string | null;
  type: MaintenanceType;
  status: MaintenanceOrderStatus;
  openedAt: IsoDateTime;
  closedAt: IsoDateTime | null;
  reportedFailure: string | null;
  totalCost: number;
}

export interface MaintenanceTask {
  description: string;
  done: boolean;
}

export interface MaintenancePart {
  id: Guid;
  itemId: Guid | null;
  warehouseId: Guid | null;
  description: string;
  quantity: number;
  unitCost: number;
  amount: number;
  consumed: boolean;
}

export interface MaintenanceOrderDetail {
  summary: MaintenanceOrderSummary;
  planId: Guid | null;
  planName: string | null;
  diagnosis: string | null;
  workPerformed: string | null;
  tasks: MaintenanceTask[];
  parts: MaintenancePart[];
  laborHours: number;
  laborCostPerHour: number;
  technicians: string | null;
  externalProvider: string | null;
  externalInvoice: string | null;
  externalCost: number;
  partsCost: number;
  laborCost: number;
  odometerAtService: number | null;
  hoursAtService: number | null;
  downtimeHours: number;
  notes: string | null;
  cancellationReason: string | null;
}

export interface FuelLoad {
  id: Guid;
  unitId: Guid;
  unitCode: string | null;
  loadedAt: IsoDateTime;
  liters: number;
  pricePerLiter: number;
  amount: number;
  odometer: number | null;
  hours: number | null;
  isFullTank: boolean;
  station: string | null;
  paymentMethod: FuelPaymentMethod;
  driverId: Guid | null;
  driverName: string | null;
  workOrderId: Guid | null;
  receiptFileId: Guid | null;
  receiptUrl: string | null;
  efficiency: number | null;
  efficiencyUnit: string | null;
  deviationPercent: number | null;
  isAnomaly: boolean;
  notes: string | null;
  createdBy: string | null;
}

export interface FuelEfficiencyRow {
  unitId: Guid;
  unitCode: string;
  unitName: string;
  meterKind: MeterKind;
  liters: number;
  amount: number;
  distance: number | null;
  hoursWorked: number | null;
  efficiency: number | null;
  efficiencyUnit: string | null;
  expected: number | null;
  deviationPercent: number | null;
  loads: number;
  anomalies: number;
  costPerKm: number | null;
  revenue: number | null;
}

// ---- Compliance ---------------------------------------------------------------------------------

export interface ComplianceDocument {
  id: Guid;
  subject: DocumentSubject;
  subjectId: Guid;
  subjectCode: string | null;
  subjectName: string | null;
  documentTypeId: Guid;
  documentTypeName: string | null;
  number: string | null;
  issuer: string | null;
  issuedOn: IsoDate | null;
  expiresOn: IsoDate | null;
  hours: number | null;
  fileId: Guid | null;
  fileUrl: string | null;
  notes: string | null;
  status: ComplianceStatus;
  daysLeft: number | null;
}

export interface ComplianceDocumentData {
  documentTypeId: Guid;
  number: string | null;
  issuer: string | null;
  issuedOn: IsoDate | null;
  expiresOn: IsoDate | null;
  hours: number | null;
  fileId: Guid | null;
  notes: string | null;
}

export interface MatrixCell {
  documentTypeId: Guid;
  status: ComplianceStatus;
  expiresOn: IsoDate | null;
  daysLeft: number | null;
  documentId: Guid | null;
}

export interface MatrixRow {
  subjectId: Guid;
  code: string;
  name: string;
  detail: string | null;
  worst: ComplianceStatus;
  cells: MatrixCell[];
}

export interface ComplianceMatrix {
  subject: DocumentSubject;
  documentTypes: Lookup[];
  rows: MatrixRow[];
  totals: Partial<Record<ComplianceStatus, number>>;
}

// ---- Inventory ------------------------------------------------------------------------------------

export interface StockRow {
  itemId: Guid;
  itemCode: string;
  itemName: string;
  category: ItemCategory;
  unitName: string;
  warehouseId: Guid;
  warehouseName: string;
  quantity: number;
  averageCost: number;
  value: number;
  minStock: number;
}

export interface Movement {
  id: Guid;
  type: MovementType;
  itemId: Guid;
  itemCode: string | null;
  itemName: string | null;
  warehouseId: Guid;
  warehouseName: string | null;
  targetWarehouseId: Guid | null;
  targetWarehouseName: string | null;
  quantity: number;
  unitCost: number;
  totalCost: number;
  difference: number | null;
  movedAt: IsoDateTime;
  reference: string | null;
  crewId: Guid | null;
  crewName: string | null;
  employeeId: Guid | null;
  employeeName: string | null;
  unitId: Guid | null;
  maintenanceOrderId: Guid | null;
  notes: string | null;
  createdBy: string | null;
}

export interface ToolAssignment {
  id: Guid;
  itemId: Guid;
  itemCode: string | null;
  itemName: string | null;
  warehouseId: Guid;
  quantity: number;
  serialNumber: string | null;
  crewId: Guid | null;
  crewName: string | null;
  employeeId: Guid | null;
  employeeName: string | null;
  assignedAt: IsoDateTime;
  expectedReturnOn: IsoDate | null;
  returnedAt: IsoDateTime | null;
  returnCondition: string | null;
  notes: string | null;
  isOverdue: boolean;
}

// ---- People -----------------------------------------------------------------------------------------

export interface Employee {
  id: Guid;
  code: string;
  name: string;
  firstName: string;
  lastName: string;
  curp: string | null;
  rfc: string | null;
  nss: string | null;
  position: string | null;
  category: EmployeeCategory;
  hiredOn: IsoDate | null;
  terminatedOn: IsoDate | null;
  phone: string | null;
  email: string | null;
  emergencyContact: string | null;
  bloodType: string | null;
  licenseNumber: string | null;
  licenseType: string | null;
  licenseExpiresOn: IsoDate | null;
  hourlyCost: number | null;
  photoFileId: Guid | null;
  hasUser: boolean;
  notes: string | null;
  isActive: boolean;
}

export interface AttendanceRow {
  id: Guid | null;
  employeeId: Guid;
  employeeCode: string;
  employeeName: string;
  position: string | null;
  date: IsoDate;
  status: AttendanceStatus | null;
  crewId: Guid | null;
  workOrderId: Guid | null;
  ticketId: Guid | null;
  siteId: Guid | null;
  checkInAt: IsoDateTime | null;
  checkOutAt: IsoDateTime | null;
  hours: number;
  overtimeHours: number;
  notes: string | null;
}

export interface PayrollRow {
  employeeId: Guid;
  code: string;
  name: string;
  position: string | null;
  daysWorked: number;
  hours: number;
  overtimeHours: number;
  absences: number;
  rest: number;
  leave: number;
  hourlyCost: number | null;
  cost: number | null;
}

export interface ExpenseItem {
  id: Guid;
  date: IsoDate;
  category: ExpenseCategory;
  description: string;
  vendor: string | null;
  amount: number;
  hasInvoice: boolean;
  invoiceUuid: string | null;
  receiptFileId: Guid | null;
  receiptUrl: string | null;
}

export interface ExpenseReport {
  id: Guid;
  folio: string;
  employeeId: Guid;
  employeeName: string | null;
  workOrderId: Guid | null;
  workOrderFolio: string | null;
  periodStart: IsoDate;
  periodEnd: IsoDate;
  purpose: string;
  advanceAmount: number;
  total: number;
  balance: number;
  status: ExpenseStatus;
  submittedAt: IsoDateTime | null;
  reviewedAt: IsoDateTime | null;
  reviewedBy: string | null;
  rejectionReason: string | null;
  paidAt: IsoDateTime | null;
  items: ExpenseItem[];
  createdAt: IsoDateTime;
}

// ---- HSE ---------------------------------------------------------------------------------------------

export interface CorrectiveAction {
  description: string;
  responsible: string | null;
  dueOn: IsoDate | null;
  status: ActionStatus;
  completedOn: IsoDate | null;
}

export interface IncidentSummary {
  id: Guid;
  folio: string;
  occurredAt: IsoDateTime;
  type: IncidentType;
  severity: IncidentSeverity;
  status: IncidentStatus;
  siteName: string | null;
  unitCode: string | null;
  description: string;
  openActions: number;
  reportedByName: string;
}

export interface IncidentDetail {
  summary: IncidentSummary;
  siteId: Guid | null;
  unitId: Guid | null;
  workOrderId: Guid | null;
  workOrderFolio: string | null;
  location: string | null;
  employees: NamedRef[];
  immediateActions: string | null;
  rootCause: string | null;
  lostDays: number | null;
  actions: CorrectiveAction[];
  photos: TicketPhoto[];
  closedAt: IsoDateTime | null;
}

export interface ChecklistItem {
  key: string;
  question: string;
  isCritical: boolean;
}

export interface InspectionTemplate {
  id: Guid;
  code: string;
  name: string;
  category: InspectionCategory;
  items: ChecklistItem[];
  notes: string | null;
  isActive: boolean;
}

export interface InspectionSummary {
  id: Guid;
  templateName: string;
  category: InspectionCategory;
  performedAt: IsoDateTime;
  inspectorName: string;
  unitCode: string | null;
  siteName: string | null;
  score: number;
  findings: number;
  criticalFindings: number;
  passed: boolean;
}

export interface InspectionAnswer {
  key: string;
  question: string;
  isCritical: boolean;
  result: CheckResult;
  comment: string | null;
  photoFileId: Guid | null;
}

export interface InspectionDetail {
  summary: InspectionSummary;
  templateId: Guid;
  unitId: Guid | null;
  siteId: Guid | null;
  workOrderId: Guid | null;
  answers: InspectionAnswer[];
  photoUrls: Record<string, string>;
  notes: string | null;
}

export interface SafetyTalk {
  id: Guid;
  heldAt: IsoDateTime;
  topic: string;
  durationMinutes: number;
  siteId: Guid | null;
  siteName: string | null;
  workOrderId: Guid | null;
  instructor: string;
  attendees: NamedRef[];
  externalAttendees: string | null;
  evidenceUrls: string[];
  evidenceFileIds: Guid[];
  notes: string | null;
}

export interface HseStats {
  incidents: number;
  accidents: number;
  nearMisses: number;
  lostDays: number;
  openIncidents: number;
  openActions: number;
  inspections: number;
  averageScore: number;
  failedInspections: number;
  safetyTalks: number;
  talkAttendees: number;
  daysWithoutLostTimeAccident: number;
  byType: Kpi[];
}

// ---- Fiscal ------------------------------------------------------------------------------------------

export interface InvoiceSummary {
  id: Guid;
  series: string;
  folio: number | null;
  status: InvoiceStatus;
  type: CfdiType;
  customerId: Guid;
  receiverName: string;
  receiverRfc: string;
  issueDate: IsoDate;
  dueOn: IsoDate;
  total: number;
  balance: number;
  currency: string;
  uuid: string | null;
  copadeNumber: string | null;
  ticketCount: number;
  isOverdue: boolean;
  daysOverdue: number;
}

export interface InvoiceLine {
  id: Guid;
  order: number;
  ticketId: Guid | null;
  serviceId: Guid | null;
  satProductCode: string;
  satUnitCode: string;
  unitName: string;
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
  discount: number;
  taxObject: string;
  vatRate: number;
  vat: number;
}

export interface InvoicePaymentItem {
  paymentId: Guid;
  paidAt: IsoDateTime;
  amount: number;
  partiality: number;
  paymentForm: string;
  reference: string | null;
  complementStatus: PaymentComplementStatus;
}

export interface InvoiceDetail {
  summary: InvoiceSummary;
  receiver: { rfc: string; name: string; taxRegime: string | null; postalCode: string | null; cfdiUse: string };
  issuer: { rfc: string; name: string; taxRegime: string; expeditionPostalCode: string } | null;
  paymentForm: string;
  paymentMethod: string;
  exchangeRate: number;
  subtotal: number;
  discount: number;
  vat: number;
  paidAmount: number;
  lines: InvoiceLine[];
  ticketIds: Guid[];
  estimateId: Guid | null;
  tripId: Guid | null;
  stampedAt: IsoDateTime | null;
  pacProvider: string | null;
  xmlUrl: string | null;
  pdfUrl: string | null;
  cancellationMotive: string | null;
  substitutionUuid: string | null;
  cancelledAt: IsoDateTime | null;
  notes: string | null;
  payments: InvoicePaymentItem[];
}

export interface InvoiceableTicket {
  id: Guid;
  folio: string;
  serviceDate: IsoDate;
  customerId: Guid;
  customerName: string | null;
  siteName: string | null;
  status: TicketStatus;
  subtotal: number;
  total: number;
  inDraft: boolean;
  /** The validation workflow of the ticket keeps it from being invoiced. */
  held: 'pending' | 'rejected' | null;
}

export interface PaymentSummary {
  id: Guid;
  customerId: Guid;
  customerName: string | null;
  paidAt: IsoDateTime;
  amount: number;
  paymentForm: string;
  reference: string | null;
  complementStatus: PaymentComplementStatus;
  complementUuid: string | null;
  invoices: number;
}

export interface AgingRow {
  customerId: Guid;
  customerName: string;
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  over90: number;
  total: number;
  invoices: number;
  oldestDueOn: IsoDate | null;
  nextPromise: IsoDate | null;
}

export interface Aging {
  asOf: IsoDate;
  rows: AgingRow[];
  totals: AgingRow;
}

export interface CollectionNote {
  id: Guid;
  customerId: Guid;
  customerName: string | null;
  invoiceId: Guid | null;
  invoiceReference: string | null;
  estimateId: Guid | null;
  contactedAt: IsoDateTime;
  channel: ContactChannel;
  contactName: string | null;
  comment: string;
  promisedPaymentOn: IsoDate | null;
  nextActionOn: IsoDate | null;
  createdBy: string | null;
}

export interface Estimate {
  id: Guid;
  folio: string;
  customerId: Guid;
  customerName: string | null;
  contractId: Guid | null;
  contractName: string | null;
  periodStart: IsoDate;
  periodEnd: IsoDate;
  description: string;
  ticketIds: Guid[];
  amount: number;
  status: EstimateStatus;
  submittedAt: IsoDateTime | null;
  approvedAt: IsoDateTime | null;
  copadeNumber: string | null;
  copadeAt: IsoDateTime | null;
  invoiceId: Guid | null;
  invoiceReference: string | null;
  rejectionReason: string | null;
  notes: string | null;
  daysInStatus: number;
}

export interface TripLocation {
  siteId: Guid | null;
  name: string;
  rfc: string | null;
  postalCode: string;
  state: string;
  municipality: string | null;
  address: string | null;
  dateTime: IsoDateTime;
}

export interface TripGoods {
  satGoodsCode: string;
  description: string;
  quantity: number;
  satUnitCode: string;
  weightKg: number;
  isHazardous: boolean;
  hazardousCode: string | null;
  packagingCode: string | null;
}

export interface TripSummary {
  id: Guid;
  folio: string;
  status: TripStatus;
  cfdiType: CfdiType;
  unitCode: string | null;
  operatorName: string | null;
  originName: string;
  destinationName: string;
  departure: IsoDateTime;
  distanceKm: number;
  totalWeightKg: number;
  hasHazardousGoods: boolean;
  uuid: string | null;
}

export interface TripDetail {
  summary: TripSummary;
  customerId: Guid | null;
  workOrderId: Guid | null;
  unitId: Guid;
  trailerUnitId: Guid | null;
  operatorId: Guid;
  origin: TripLocation;
  destination: TripLocation;
  goods: TripGoods[];
  freightServiceId: Guid | null;
  freightAmount: number | null;
  idCcp: string;
  invoiceId: Guid | null;
  notes: string | null;
  cancellationReason: string | null;
  missingData: string[];
}

export interface QuoteSummary {
  id: Guid;
  folio: string;
  status: QuoteStatus;
  customerId: Guid;
  customerName: string | null;
  subject: string | null;
  issuedOn: IsoDate;
  validUntil: IsoDate;
  total: number;
  currency: string;
  workOrderId: Guid | null;
  isExpired: boolean;
}

export interface QuoteLine {
  id: Guid;
  order: number;
  serviceId: Guid;
  description: string;
  quantity: number;
  unitName: string;
  unitPrice: number;
  discountPercent: number;
  amount: number;
  vatRate: number;
  vat: number;
}

export interface QuoteDetail {
  summary: QuoteSummary;
  contractId: Guid | null;
  siteId: Guid | null;
  serviceTypeId: Guid | null;
  terms: string | null;
  notes: string | null;
  lines: QuoteLine[];
  subtotal: number;
  discount: number;
  vat: number;
  sentAt: IsoDateTime | null;
  acceptedAt: IsoDateTime | null;
  rejectionReason: string | null;
  workOrderFolio: string | null;
}

// ---- Management ------------------------------------------------------------------------------------

export interface BillingMonth {
  year: number;
  month: number;
  executed: number;
  invoiced: number;
  collected: number;
  tickets: number;
}

export interface BillingCustomer {
  customerId: Guid;
  customerName: string;
  executed: number;
  uninvoiced: number;
  invoiced: number;
  collected: number;
  receivable: number;
}

export interface UninvoicedTicket {
  id: Guid;
  folio: string;
  serviceDate: IsoDate;
  customerName: string | null;
  status: TicketStatus;
  total: number;
  daysWaiting: number;
  alert: boolean;
}

export interface BillingBoard {
  executed: number;
  uninvoiced: number;
  invoiced: number;
  collected: number;
  receivable: number;
  ticketsWithoutInvoice: number;
  alertTickets: number;
  alertDays: number;
  months: BillingMonth[];
  customers: BillingCustomer[];
  pending: UninvoicedTicket[];
}

export interface UnitUtilization {
  unitId: Guid;
  code: string;
  name: string;
  status: UnitStatus;
  daysWorked: number;
  utilizationPercent: number;
  revenue: number;
}

export interface Dashboard {
  from: IsoDate;
  to: IsoDate;
  unitUtilizationPercent: number;
  revenuePerUnit: number;
  averageDaysTicketToInvoice: number;
  overdueReceivables: number;
  servicesThisMonth: number;
  servicesAmountThisMonth: number;
  servicesPreviousMonth: number;
  servicesAmountPreviousMonth: number;
  units: UnitUtilization[];
  revenueByServiceType: Kpi[];
  revenueByCustomer: Kpi[];
  ticketsByDay: Kpi[];
  aging: Kpi[];
  fleetStatus: Record<string, number>;
}

export interface MonthSummary {
  signedTickets: number;
  amount: number;
  pendingInvoice: number;
  pendingAmount: number;
  drafts: number;
  openWorkOrders: number;
}

export interface PortalTicket {
  id: Guid;
  folio: string;
  serviceDate: IsoDate;
  siteName: string | null;
  status: TicketStatus;
  total: number;
}

export interface PortalSummary {
  customerId: Guid;
  customerName: string;
  ticketsThisMonth: number;
  amountThisMonth: number;
  ticketsLastMonth: number;
  scheduledOrders: number;
  openInvoices: number;
  balance: number;
  overdueBalance: number;
  recentTickets: PortalTicket[];
}

// ---- Workflows and business rules ------------------------------------------------------------------

export type AutomationDocument = 'serviceTicket' | 'workOrder' | 'quote' | 'invoice' | 'expenseReport' | 'maintenanceOrder' | 'fuelLoad' | 'inventoryMovement';
export type AutomationTrigger = 'save' | 'close' | 'approve' | 'dispatch' | 'send' | 'stamp' | 'submit';
export type FactType = 'number' | 'money' | 'boolean' | 'enum' | 'reference';
export type ConditionOperator = 'equal' | 'notEqual' | 'greaterThan' | 'greaterOrEqual' | 'lessThan' | 'lessOrEqual' | 'in' | 'notIn' | 'isTrue' | 'isFalse';
export type ConditionMatch = 'all' | 'any';
export type WorkflowCompletion = 'execute' | 'unlock';
export type WorkflowStatus = 'pending' | 'approved' | 'rejected' | 'cancelled';
export type RuleEffect = 'block' | 'notify';
export type ApprovalView = 'mine' | 'requested' | 'all';
export type ApprovalStepState = 'approved' | 'current' | 'waiting' | 'rejected' | 'cancelled';

/** Datum of a document that conditions can test. */
export interface FactDefinition {
  key: string;
  type: FactType;
  name: string;
  nameEn: string;
  values: string[] | null;
  /** Translations enums.<enumName>.<value> of enum facts (null: the values are shown as they are). */
  enumName: string | null;
  /** Lookup catalog of reference facts. */
  catalog: string | null;
  /** Depends on the moment (it does not invalidate an approval already given). */
  volatile: boolean;
  /** Records of one company: only company definitions can use it. */
  companyOnly: boolean;
}

export interface WorkflowGate {
  start: AutomationTrigger;
  completion: WorkflowCompletion;
  description: string;
  descriptionEn: string;
}

export interface AutomationDocumentInfo {
  document: AutomationDocument;
  module: string;
  enabled: boolean;
  name: string;
  nameEn: string;
  icon: string;
  triggers: AutomationTrigger[];
  workflow: WorkflowGate | null;
  facts: FactDefinition[];
}

export interface AutomationCatalog {
  documents: AutomationDocumentInfo[];
  triggers: { trigger: AutomationTrigger; name: string; nameEn: string }[];
  roleTemplates: { key: string; name: string }[];
  canManage: boolean;
  canManageGlobal: boolean;
}

export interface RuleCondition {
  field: string;
  operator: ConditionOperator;
  value: string | null;
}

/** People chosen by role template (every company), company role, permission or the company administrators. */
export interface Audience {
  roleTemplates: string[];
  roleIds: Guid[];
  permissions: string[];
  tenantAdmins: boolean;
}

export interface WorkflowStep {
  name: string;
  approvers: Audience;
  requiredApprovals: number;
  slaHours: number | null;
  allowRequester: boolean;
}

export interface WorkflowDefinition {
  id: Guid;
  isGlobal: boolean;
  name: string;
  description: string | null;
  document: AutomationDocument;
  isEnabled: boolean;
  isMandatory: boolean;
  priority: number;
  match: ConditionMatch;
  conditions: RuleCondition[];
  steps: WorkflowStep[];
  optedOut: boolean;
  canEdit: boolean;
  pending: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime | null;
  updatedBy: string | null;
}

export interface RuleRecipients {
  audience: Audience;
  emails: string[];
  notifyActor: boolean;
}

export interface BusinessRule {
  id: Guid;
  isGlobal: boolean;
  name: string;
  description: string | null;
  document: AutomationDocument;
  triggers: AutomationTrigger[];
  isEnabled: boolean;
  isMandatory: boolean;
  priority: number;
  match: ConditionMatch;
  conditions: RuleCondition[];
  effect: RuleEffect;
  message: string;
  severity: NotificationSeverity;
  recipients: RuleRecipients;
  optedOut: boolean;
  canEdit: boolean;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime | null;
  updatedBy: string | null;
}

export interface WorkflowDecision {
  step: number;
  membershipId: Guid | null;
  name: string;
  approved: boolean;
  comment: string | null;
  at: IsoDateTime;
}

export interface ApprovalSummary {
  id: Guid;
  document: AutomationDocument;
  documentId: Guid;
  reference: string;
  link: string;
  definitionName: string;
  fromGlobalDefinition: boolean;
  status: WorkflowStatus;
  currentStep: number;
  stepCount: number;
  stepName: string | null;
  requestedByName: string;
  requestedAt: IsoDateTime;
  stepStartedAt: IsoDateTime;
  completedAt: IsoDateTime | null;
  closingComment: string | null;
  amount: number | null;
  overdue: boolean;
  canDecide: boolean;
}

export interface ApprovalStep {
  index: number;
  name: string;
  requiredApprovals: number;
  slaHours: number | null;
  allowRequester: boolean;
  approvers: Audience;
  state: ApprovalStepState;
  approverNames: string[];
}

export interface ApprovalDetail {
  summary: ApprovalSummary;
  steps: ApprovalStep[];
  decisions: WorkflowDecision[];
  facts: Record<string, string | null>;
  conditions: RuleCondition[];
  match: ConditionMatch;
  canCancel: boolean;
}

export interface DocumentWorkflow {
  document: AutomationDocument;
  documentId: Guid;
  atGate: boolean;
  applies: boolean;
  definitionName: string | null;
  current: ApprovalDetail | null;
  history: ApprovalSummary[];
  canRequest: boolean;
}

export interface RequestApprovalResult {
  required: boolean;
  instanceId: Guid | null;
  message: string;
}

/** Body of a 202: the action waits for the approval request. */
export interface ApprovalPending {
  approvalPending: true;
  instanceId: Guid;
  message: string;
}

export interface JobCompanyResult {
  slug: string;
  name: string;
  counts: Record<string, number>;
  error: string | null;
}

export interface ScheduledJobRun {
  id: Guid;
  job: string;
  triggeredBy: string;
  startedAt: IsoDateTime;
  finishedAt: IsoDateTime | null;
  companies: number;
  notices: number;
  failures: number;
  details: JobCompanyResult[];
}

export interface ScheduledJobs {
  backgroundEnabled: boolean;
  intervalMinutes: number;
  nextRunAt: IsoDateTime | null;
  runs: ScheduledJobRun[];
}
