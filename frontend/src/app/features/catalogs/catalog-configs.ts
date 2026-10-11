import { num } from '../../core/format';
import { P } from '../../core/permissions';
import { CatalogConfig, FieldDef, FieldOption } from '../../shared/crud/fields';

// ---- SAT catalogs used by customers and services (most common values in the oilfield services sector) ----

const literal = (value: string, label: string): FieldOption => ({ value, label: `${value} · ${label}`, literal: true });

export const TAX_REGIMES: FieldOption[] = [
  literal('601', 'General de Ley Personas Morales'),
  literal('603', 'Personas Morales con Fines no Lucrativos'),
  literal('605', 'Sueldos y Salarios'),
  literal('606', 'Arrendamiento'),
  literal('612', 'Personas Físicas con Actividades Empresariales'),
  literal('616', 'Sin obligaciones fiscales'),
  literal('621', 'Incorporación Fiscal'),
  literal('626', 'Régimen Simplificado de Confianza'),
];

export const CFDI_USES: FieldOption[] = [
  literal('G01', 'Adquisición de mercancías'),
  literal('G03', 'Gastos en general'),
  literal('I03', 'Equipo de transporte'),
  literal('I08', 'Otra maquinaria y equipo'),
  literal('S01', 'Sin efectos fiscales'),
  literal('CP01', 'Pagos'),
];

export const PAYMENT_FORMS: FieldOption[] = [
  literal('01', 'Efectivo'),
  literal('02', 'Cheque nominativo'),
  literal('03', 'Transferencia electrónica'),
  literal('04', 'Tarjeta de crédito'),
  literal('28', 'Tarjeta de débito'),
  literal('99', 'Por definir'),
];

export const PAYMENT_METHODS: FieldOption[] = [literal('PUE', 'Pago en una sola exhibición'), literal('PPD', 'Pago en parcialidades o diferido')];

export const TAX_OBJECTS: FieldOption[] = [literal('01', 'No objeto de impuesto'), literal('02', 'Sí objeto de impuesto'), literal('03', 'Sí objeto, no obligado al desglose')];

export const CURRENCIES: FieldOption[] = [
  { value: 'MXN', label: 'MXN', literal: true },
  { value: 'USD', label: 'USD', literal: true },
];

const code: FieldDef = { key: 'code', label: 'fields.code', type: 'text', required: true, upper: true, maxLength: 40, pattern: '^[A-Za-z0-9Ññ][A-Za-z0-9Ññ._/-]*$', hint: 'fields.codeHint' };
const name: FieldDef = { key: 'name', label: 'fields.name', type: 'text', required: true, maxLength: 200 };
const notes: FieldDef = { key: 'notes', label: 'fields.notes', type: 'textarea', span: 'full', maxLength: 2000 };

// ---- Commercial master data ------------------------------------------------------------------------------

export const CUSTOMERS: CatalogConfig = {
  key: 'customers',
  api: 'catalogs/customers',
  icon: 'apartment',
  manage: [P.catalogsManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['legalName'] as string },
    { key: 'code', label: 'fields.code', wide: true },
    { key: 'rfc', label: 'fields.rfc' },
    { key: 'paymentMethod', label: 'fields.paymentMethod', wide: true },
    { key: 'creditDays', label: 'fields.creditDays', type: 'number', align: 'end', wide: true },
    { key: 'requiresCopade', label: 'fields.requiresCopade', type: 'bool', wide: true },
  ],
  fields: [
    code,
    name,
    { key: 'legalName', label: 'fields.legalName', type: 'text', maxLength: 300 },
    { key: 'rfc', label: 'fields.rfc', type: 'text', upper: true, maxLength: 13 },
    { key: 'taxRegime', label: 'fields.taxRegime', type: 'select', options: TAX_REGIMES },
    { key: 'postalCode', label: 'fields.postalCode', type: 'text', maxLength: 5, pattern: '^\\d{5}$' },
    { key: 'cfdiUse', label: 'fields.cfdiUse', type: 'select', options: CFDI_USES },
    { key: 'paymentForm', label: 'fields.paymentForm', type: 'select', options: PAYMENT_FORMS },
    { key: 'paymentMethod', label: 'fields.paymentMethod', type: 'select', options: PAYMENT_METHODS, required: true },
    { key: 'creditDays', label: 'fields.creditDays', type: 'number', min: 0, max: 365, required: true },
    { key: 'email', label: 'fields.billingEmail', type: 'text', maxLength: 200 },
    { key: 'phone', label: 'fields.phone', type: 'text', maxLength: 40 },
    { key: 'color', label: 'fields.color', type: 'color', hint: 'fields.colorHint' },
    { key: 'requiresCopade', label: 'fields.requiresCopade', type: 'checkbox' },
    { key: 'address', label: 'fields.address', type: 'textarea', span: 'full', maxLength: 500 },
    {
      key: 'contacts',
      label: 'fields.contacts',
      type: 'array',
      addLabel: 'fields.addContact',
      fields: [
        { key: 'name', label: 'fields.name', type: 'text', required: true },
        { key: 'position', label: 'fields.position', type: 'text' },
        { key: 'email', label: 'fields.email', type: 'text' },
        { key: 'phone', label: 'fields.phoneWhatsApp', type: 'text' },
        { key: 'notifyTickets', label: 'fields.notifyTickets', type: 'checkbox' },
        { key: 'notifyInvoices', label: 'fields.notifyInvoices', type: 'checkbox' },
        { key: 'notifyCollections', label: 'fields.notifyCollections', type: 'checkbox' },
      ],
    },
    notes,
  ],
  defaults: () => ({ paymentMethod: 'PPD', paymentForm: '99', cfdiUse: 'G03', creditDays: 30, contacts: [] }),
  invalidates: ['catalogs/sites', 'catalogs/contracts'],
};

export const CONTRACTS: CatalogConfig = {
  key: 'contracts',
  api: 'catalogs/contracts',
  icon: 'handshake',
  manage: [P.catalogsManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'customerName', label: 'fields.customer' },
    { key: 'startDate', label: 'fields.startDate', type: 'date', wide: true },
    { key: 'endDate', label: 'fields.endDate', type: 'date', wide: true },
    { key: 'ceilingAmount', label: 'fields.ceilingAmount', type: 'money', align: 'end', wide: true, currency: (r) => (r['currency'] as string) ?? 'MXN' },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'contractStatus' },
  ],
  filters: [{ key: 'status', label: 'fields.status', type: 'enum', enum: 'contractStatus', values: ['draft', 'active', 'suspended', 'closed'] }],
  fields: [
    code,
    name,
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers', required: true },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'contractStatus', values: ['draft', 'active', 'suspended', 'closed'], required: true },
    { key: 'startDate', label: 'fields.startDate', type: 'date', required: true },
    { key: 'endDate', label: 'fields.endDate', type: 'date' },
    { key: 'ceilingAmount', label: 'fields.ceilingAmount', type: 'money' },
    { key: 'currency', label: 'fields.currency', type: 'select', options: CURRENCIES, required: true },
    { key: 'customerRepresentative', label: 'fields.customerRepresentative', type: 'text', span: 'full' },
    notes,
  ],
  defaults: () => ({ status: 'active', currency: 'MXN' }),
};

export const SITES: CatalogConfig = {
  key: 'sites',
  api: 'catalogs/sites',
  icon: 'oil_barrel',
  manage: [P.catalogsManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'siteType' },
    { key: 'customerName', label: 'fields.customer', wide: true },
    { key: 'fieldName', label: 'fields.field' },
    { key: 'municipality', label: 'fields.municipality', wide: true, sub: (r) => r['state'] as string },
  ],
  filters: [{ key: 'type', label: 'fields.type', type: 'enum', enum: 'siteType', values: ['well', 'field', 'facility', 'yard', 'other'] }],
  fields: [
    code,
    name,
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'siteType', values: ['well', 'field', 'facility', 'yard', 'other'], required: true },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers', hint: 'fields.siteCustomerHint' },
    { key: 'fieldName', label: 'fields.field', type: 'text' },
    { key: 'municipality', label: 'fields.municipality', type: 'text' },
    { key: 'state', label: 'fields.state', type: 'text' },
    { key: 'postalCode', label: 'fields.postalCode', type: 'text', maxLength: 5, pattern: '^\\d{5}$', hint: 'fields.postalCodeCartaPorte' },
    { key: 'latitude', label: 'fields.latitude', type: 'number', min: -90, max: 90 },
    { key: 'longitude', label: 'fields.longitude', type: 'number', min: -180, max: 180 },
    notes,
  ],
  defaults: () => ({ type: 'well' }),
};

// ---- Services and prices -------------------------------------------------------------------------------

export const SERVICES: CatalogConfig = {
  key: 'services',
  api: 'pricing/services',
  icon: 'design_services',
  manage: [P.pricingManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'category', label: 'fields.category', wide: true },
    { key: 'unitName', label: 'fields.unit' },
    { key: 'satProductCode', label: 'fields.satProductCode', wide: true, sub: (r) => r['satUnitCode'] as string },
    { key: 'basePrice', label: 'fields.basePrice', type: 'money', align: 'end' },
  ],
  fields: [
    code,
    name,
    { key: 'category', label: 'fields.category', type: 'text' },
    { key: 'unitName', label: 'fields.unit', type: 'text', required: true, hint: 'fields.unitHint' },
    { key: 'satProductCode', label: 'fields.satProductCode', type: 'text', required: true, pattern: '^\\d{8}$', hint: 'fields.satProductHint' },
    { key: 'satUnitCode', label: 'fields.satUnitCode', type: 'text', required: true, upper: true, hint: 'fields.satUnitHint' },
    { key: 'taxObject', label: 'fields.taxObject', type: 'select', options: TAX_OBJECTS },
    { key: 'vatRate', label: 'fields.vatRate', type: 'percent', required: true, min: 0, max: 100 },
    { key: 'basePrice', label: 'fields.basePrice', type: 'money', hint: 'fields.basePriceHint' },
    { key: 'description', label: 'fields.description', type: 'textarea', span: 'full' },
    notes,
  ],
  defaults: () => ({ taxObject: '02', vatRate: 0.16 }),
};

export const PRICE_LISTS: CatalogConfig = {
  key: 'priceLists',
  api: 'pricing/price-lists',
  icon: 'request_quote',
  manage: [P.pricingManage],
  dialogWidth: '900px',
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'customerName', label: 'fields.scope', value: (r) => (r['contractName'] as string) ?? (r['customerName'] as string) ?? '—', sub: (r) => (r['contractName'] ? (r['customerName'] as string) : null) },
    { key: 'validFrom', label: 'fields.validFrom', type: 'date' },
    { key: 'validTo', label: 'fields.validTo', type: 'date', wide: true },
    { key: 'itemCount', label: 'fields.services', type: 'number', align: 'end' },
  ],
  fields: [
    code,
    name,
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers', hint: 'fields.priceListScopeHint' },
    { key: 'contractId', label: 'fields.contract', type: 'lookup', catalog: 'catalogs/contracts', parent: 'customerId' },
    { key: 'currency', label: 'fields.currency', type: 'select', options: CURRENCIES, required: true },
    { key: 'validFrom', label: 'fields.validFrom', type: 'date', required: true },
    { key: 'validTo', label: 'fields.validTo', type: 'date' },
    {
      key: 'items',
      label: 'fields.prices',
      type: 'array',
      addLabel: 'fields.addPrice',
      fields: [
        { key: 'serviceId', label: 'fields.service', type: 'lookup', catalog: 'pricing/services', required: true },
        { key: 'unitPrice', label: 'fields.unitPrice', type: 'money', required: true, min: 0 },
        { key: 'notes', label: 'fields.notes', type: 'text' },
      ],
    },
    notes,
  ],
  defaults: () => ({ currency: 'MXN', items: [] }),
  toForm: (row) => ({ ...row, items: ((row['items'] as Record<string, unknown>[]) ?? []).map((i) => ({ serviceId: i['serviceId'], unitPrice: i['unitPrice'], notes: i['notes'] })) }),
};

export const SERVICE_TYPES: CatalogConfig = {
  key: 'serviceTypes',
  api: 'pricing/service-types',
  icon: 'dynamic_form',
  manage: [P.pricingManage],
  dialogWidth: '940px',
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'color', label: 'fields.color', type: 'color' },
    { key: 'requiresSignature', label: 'fields.requiresSignature', type: 'bool' },
    { key: 'minPhotos', label: 'fields.minPhotos', type: 'number', align: 'end' },
    { key: 'fields', label: 'fields.formFields', value: (r) => ((r['fields'] as unknown[]) ?? []).length, align: 'end', wide: true },
  ],
  fields: [
    code,
    name,
    { key: 'icon', label: 'fields.icon', type: 'text', hint: 'fields.iconHint' },
    { key: 'color', label: 'fields.color', type: 'color' },
    { key: 'requiresSignature', label: 'fields.requiresSignature', type: 'checkbox' },
    { key: 'minPhotos', label: 'fields.minPhotos', type: 'number', min: 0, max: 30, required: true },
    { key: 'defaultServiceIds', label: 'fields.defaultServices', type: 'lookups', catalog: 'pricing/services', span: 'full', hint: 'fields.defaultServicesHint' },
    { key: 'description', label: 'fields.description', type: 'textarea', span: 'full' },
    {
      key: 'fields',
      label: 'fields.formFields',
      type: 'array',
      addLabel: 'fields.addFormField',
      fields: [
        { key: 'key', label: 'fields.fieldKey', type: 'text', required: true, maxLength: 40, pattern: '^[a-zA-Z][a-zA-Z0-9_]*$' },
        { key: 'label', label: 'fields.fieldLabel', type: 'text', required: true, maxLength: 80 },
        { key: 'type', label: 'fields.fieldType', type: 'enum', enum: 'formFieldType', values: ['text', 'number', 'select', 'checkbox', 'date', 'time', 'textArea'], required: true },
        { key: 'unit', label: 'fields.unit', type: 'text' },
        { key: 'order', label: 'fields.order', type: 'number', min: 0 },
        { key: 'required', label: 'fields.required', type: 'checkbox' },
        { key: 'options', label: 'fields.options', type: 'chips', showIf: (v) => v['type'] === 'select', hint: 'fields.optionsHint' },
      ],
    },
    notes,
  ],
  defaults: () => ({ icon: 'receipt_long', requiresSignature: true, minPhotos: 1, fields: [], defaultServiceIds: [] }),
  toPayload: (value) => ({
    ...value,
    fields: ((value['fields'] as Record<string, unknown>[]) ?? []).map((f, i) => ({ ...f, options: f['type'] === 'select' ? (f['options'] ?? []) : null, order: f['order'] ?? i + 1 })),
  }),
};

// ---- Fleet --------------------------------------------------------------------------------------------

const UNIT_TYPES = ['tankTruck', 'tractor', 'trailer', 'vacuumTruck', 'pumpUnit', 'crane', 'pickup', 'equipment', 'other'] as const;
const UNIT_STATUSES = ['available', 'operating', 'workshop', 'outOfService'] as const;

export const UNITS: CatalogConfig = {
  key: 'units',
  api: 'fleet/units',
  icon: 'local_shipping',
  manage: [P.fleetManage],
  dialogWidth: '940px',
  detail: (r) => ['flota', 'unidades', r['id'] as string],
  columns: [
    { key: 'code', label: 'fields.economicNumber', sub: (r) => r['name'] as string },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'unitType' },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'unitStatus' },
    { key: 'plates', label: 'fields.plates', wide: true },
    { key: 'currentOdometer', label: 'fields.meters', align: 'end', value: (r) => meters(r) },
    { key: 'defaultOperatorName', label: 'fields.defaultOperator', wide: true },
  ],
  filters: [
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'unitType', values: UNIT_TYPES },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'unitStatus', values: UNIT_STATUSES },
  ],
  fields: [
    { ...code, label: 'fields.economicNumber' },
    { ...name, label: 'fields.description' },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'unitType', values: UNIT_TYPES, required: true },
    { key: 'meterKind', label: 'fields.meterKind', type: 'enum', enum: 'meterKind', values: ['odometer', 'hourMeter', 'both'], required: true },
    { key: 'brand', label: 'fields.brand', type: 'text' },
    { key: 'model', label: 'fields.model', type: 'text' },
    { key: 'year', label: 'fields.year', type: 'number', min: 1950, max: 2100 },
    { key: 'plates', label: 'fields.plates', type: 'text', upper: true },
    { key: 'serialNumber', label: 'fields.serialNumber', type: 'text', upper: true },
    { key: 'defaultOperatorId', label: 'fields.defaultOperator', type: 'lookup', catalog: 'personnel/employees' },
    { key: 'capacity', label: 'fields.capacity', type: 'number', min: 0 },
    { key: 'capacityUnit', label: 'fields.capacityUnit', type: 'text', hint: 'fields.capacityUnitHint' },
    { key: 'fuelType', label: 'fields.fuelType', type: 'text' },
    { key: 'fuelTankLiters', label: 'fields.fuelTankLiters', type: 'number', min: 0 },
    { key: 'expectedEfficiency', label: 'fields.expectedEfficiency', type: 'number', min: 0, hint: 'fields.expectedEfficiencyHint' },
    { key: 'cartaPorte', label: 'fields.cartaPorteSection', type: 'section' },
    { key: 'vehicleConfig', label: 'fields.vehicleConfig', type: 'text', upper: true, hint: 'fields.vehicleConfigHint' },
    { key: 'grossWeightTons', label: 'fields.grossWeightTons', type: 'number', min: 0 },
    { key: 'sctPermitType', label: 'fields.sctPermitType', type: 'text', upper: true, hint: 'fields.sctPermitTypeHint' },
    { key: 'sctPermitNumber', label: 'fields.sctPermitNumber', type: 'text' },
    { key: 'insurerName', label: 'fields.insurerName', type: 'text' },
    { key: 'insurancePolicy', label: 'fields.insurancePolicy', type: 'text' },
    { key: 'environmentalInsurer', label: 'fields.environmentalInsurer', type: 'text' },
    { key: 'environmentalPolicy', label: 'fields.environmentalPolicy', type: 'text' },
    { key: 'assets', label: 'fields.assetSection', type: 'section' },
    { key: 'acquiredOn', label: 'fields.acquiredOn', type: 'date' },
    { key: 'acquisitionCost', label: 'fields.acquisitionCost', type: 'money' },
    { key: 'photoFileId', label: 'fields.photo', type: 'file', category: 'photo' },
    notes,
  ],
  defaults: () => ({ type: 'tankTruck', meterKind: 'odometer' }),
};

function meters(row: Record<string, unknown>): string {
  const odometer = row['currentOdometer'] as number;
  const hours = row['currentHours'] as number;
  const kind = row['meterKind'];
  const km = `${num(odometer, 0)} km`;
  const h = `${num(hours, 0)} h`;
  return kind === 'hourMeter' ? h : kind === 'both' ? `${km} · ${h}` : km;
}

export const MAINTENANCE_PLANS: CatalogConfig = {
  key: 'maintenancePlans',
  api: 'maintenance/plans',
  icon: 'event_repeat',
  manage: [P.maintenanceManage],
  dialogWidth: '900px',
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'trigger', label: 'fields.trigger', type: 'enum', enum: 'maintenanceTrigger' },
    { key: 'interval', label: 'fields.interval', type: 'number', align: 'end' },
    { key: 'estimatedCost', label: 'fields.estimatedCost', type: 'money', align: 'end', wide: true },
    { key: 'units', label: 'fields.units', value: (r) => ((r['units'] as unknown[]) ?? []).length, align: 'end' },
  ],
  fields: [
    code,
    name,
    { key: 'trigger', label: 'fields.trigger', type: 'enum', enum: 'maintenanceTrigger', values: ['kilometers', 'hours', 'days'], required: true },
    { key: 'interval', label: 'fields.interval', type: 'number', required: true, min: 1, hint: 'fields.intervalHint' },
    { key: 'warningBefore', label: 'fields.warningBefore', type: 'number', min: 0, required: true, hint: 'fields.warningBeforeHint' },
    { key: 'estimatedCost', label: 'fields.estimatedCost', type: 'money' },
    { key: 'tasks', label: 'fields.tasks', type: 'chips', hint: 'fields.tasksHint' },
    {
      key: 'units',
      label: 'fields.units',
      type: 'array',
      addLabel: 'fields.addUnit',
      fields: [
        { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units', required: true },
        { key: 'lastServiceOn', label: 'fields.lastServiceOn', type: 'date' },
        { key: 'lastOdometer', label: 'fields.lastOdometer', type: 'number', min: 0 },
        { key: 'lastHours', label: 'fields.lastHours', type: 'number', min: 0 },
      ],
    },
    notes,
  ],
  defaults: () => ({ trigger: 'kilometers', interval: 10000, warningBefore: 1000, tasks: [], units: [] }),
  toForm: (row) => ({ ...row, units: ((row['units'] as Record<string, unknown>[]) ?? []).map((u) => ({ unitId: u['unitId'], lastServiceOn: u['lastServiceOn'], lastOdometer: u['lastOdometer'], lastHours: u['lastHours'] })) }),
};

// ---- Compliance -------------------------------------------------------------------------------------------

export const DOCUMENT_TYPES: CatalogConfig = {
  key: 'documentTypes',
  api: 'compliance/document-types',
  icon: 'description',
  manage: [P.permitsManage, P.personnelManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'appliesTo', label: 'fields.appliesTo', type: 'enum', enum: 'documentSubject' },
    { key: 'authority', label: 'fields.authority', wide: true },
    { key: 'validityMonths', label: 'fields.validityMonths', type: 'number', align: 'end', wide: true },
    { key: 'isMandatory', label: 'fields.mandatory', type: 'bool' },
  ],
  filters: [{ key: 'appliesTo', label: 'fields.appliesTo', type: 'enum', enum: 'documentSubject', values: ['unit', 'employee'] }],
  fields: [
    code,
    name,
    { key: 'appliesTo', label: 'fields.appliesTo', type: 'enum', enum: 'documentSubject', values: ['unit', 'employee'], required: true },
    { key: 'authority', label: 'fields.authority', type: 'text', hint: 'fields.authorityHint' },
    { key: 'validityMonths', label: 'fields.validityMonths', type: 'number', min: 1 },
    { key: 'warningDays', label: 'fields.warningDays', type: 'number', min: 1, hint: 'fields.warningDaysHint' },
    { key: 'isMandatory', label: 'fields.mandatoryHint', type: 'checkbox', span: 'full' },
    notes,
  ],
  defaults: () => ({ appliesTo: 'unit', isMandatory: true }),
};

export const REQUIREMENTS: CatalogConfig = {
  key: 'requirements',
  api: 'compliance/requirements',
  icon: 'checklist',
  manage: [P.personnelPackages, P.personnelManage, P.permitsManage],
  dialogWidth: '820px',
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'customerName', label: 'fields.customer', sub: (r) => r['contractName'] as string },
    { key: 'employeeDocumentTypeIds', label: 'fields.personnelDocuments', value: (r) => ((r['employeeDocumentTypeIds'] as unknown[]) ?? []).length, align: 'end' },
    { key: 'unitDocumentTypeIds', label: 'fields.unitDocuments', value: (r) => ((r['unitDocumentTypeIds'] as unknown[]) ?? []).length, align: 'end' },
  ],
  fields: [
    code,
    name,
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
    { key: 'contractId', label: 'fields.contract', type: 'lookup', catalog: 'catalogs/contracts', parent: 'customerId' },
    { key: 'employeeDocumentTypeIds', label: 'fields.personnelDocuments', type: 'lookups', catalog: 'compliance/document-types', query: { appliesTo: 'employee' }, span: 'full' },
    { key: 'unitDocumentTypeIds', label: 'fields.unitDocuments', type: 'lookups', catalog: 'compliance/document-types', query: { appliesTo: 'unit' }, span: 'full' },
    notes,
  ],
  defaults: () => ({ employeeDocumentTypeIds: [], unitDocumentTypeIds: [] }),
};

// ---- Inventory --------------------------------------------------------------------------------------------

export const WAREHOUSES: CatalogConfig = {
  key: 'warehouses',
  api: 'inventory/warehouses',
  icon: 'warehouse',
  manage: [P.inventoryManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'location', label: 'fields.location' },
    { key: 'responsible', label: 'fields.responsible', wide: true },
  ],
  fields: [code, name, { key: 'location', label: 'fields.location', type: 'text' }, { key: 'responsible', label: 'fields.responsible', type: 'text' }, notes],
};

const ITEM_CATEGORIES = ['sparePart', 'consumable', 'tool', 'ppe', 'chemical', 'other'] as const;

export const ITEMS: CatalogConfig = {
  key: 'items',
  api: 'inventory/items',
  icon: 'category',
  manage: [P.inventoryManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'itemCategory' },
    { key: 'onHand', label: 'fields.onHand', type: 'number', align: 'end', sub: (r, t) => (r['belowMinimum'] ? t('inventory.belowMinimum', { min: r['minStock'] }) : null) },
    { key: 'unitName', label: 'fields.unit', wide: true },
    { key: 'averageCost', label: 'fields.averageCost', type: 'money', align: 'end', wide: true },
  ],
  filters: [{ key: 'category', label: 'fields.category', type: 'enum', enum: 'itemCategory', values: ITEM_CATEGORIES }],
  fields: [
    code,
    name,
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'itemCategory', values: ITEM_CATEGORIES, required: true },
    { key: 'unitName', label: 'fields.unit', type: 'text', required: true },
    { key: 'brand', label: 'fields.brand', type: 'text' },
    { key: 'partNumber', label: 'fields.partNumber', type: 'text' },
    { key: 'minStock', label: 'fields.minStock', type: 'number', min: 0, required: true },
    { key: 'maxStock', label: 'fields.maxStock', type: 'number', min: 0 },
    { key: 'standardCost', label: 'fields.standardCost', type: 'money' },
    notes,
  ],
  defaults: () => ({ category: 'sparePart', unitName: 'Pieza', minStock: 0 }),
};

// ---- People -------------------------------------------------------------------------------------------------

const EMPLOYEE_CATEGORIES = ['operator', 'helper', 'supervisor', 'technician', 'driver', 'hse', 'office', 'other'] as const;
const BLOOD_TYPES: FieldOption[] = ['O+', 'O-', 'A+', 'A-', 'B+', 'B-', 'AB+', 'AB-'].map((v) => ({ value: v, label: v, literal: true }));

export const EMPLOYEES: CatalogConfig = {
  key: 'employees',
  api: 'personnel/employees',
  icon: 'badge',
  manage: [P.personnelManage],
  dialogWidth: '900px',
  detail: (r) => ['personal', 'empleados', r['id'] as string],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'position', label: 'fields.position' },
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'employeeCategory', wide: true },
    { key: 'phone', label: 'fields.phone', wide: true },
    { key: 'licenseExpiresOn', label: 'fields.licenseExpiresOn', type: 'date', wide: true },
    { key: 'hasUser', label: 'fields.hasUser', type: 'bool', wide: true },
  ],
  filters: [{ key: 'category', label: 'fields.category', type: 'enum', enum: 'employeeCategory', values: EMPLOYEE_CATEGORIES }],
  fields: [
    { ...code, label: 'fields.employeeNumber' },
    { key: 'firstName', label: 'fields.firstName', type: 'text', required: true },
    { key: 'lastName', label: 'fields.lastName', type: 'text', required: true },
    { key: 'position', label: 'fields.position', type: 'text' },
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'employeeCategory', values: EMPLOYEE_CATEGORIES, required: true },
    { key: 'hiredOn', label: 'fields.hiredOn', type: 'date' },
    { key: 'curp', label: 'fields.curp', type: 'text', upper: true, maxLength: 18 },
    { key: 'rfc', label: 'fields.rfc', type: 'text', upper: true, maxLength: 13 },
    { key: 'nss', label: 'fields.nss', type: 'text', maxLength: 11 },
    { key: 'phone', label: 'fields.phoneWhatsApp', type: 'text' },
    { key: 'email', label: 'fields.email', type: 'text' },
    { key: 'emergencyContact', label: 'fields.emergencyContact', type: 'text' },
    { key: 'bloodType', label: 'fields.bloodType', type: 'select', options: BLOOD_TYPES },
    { key: 'hourlyCost', label: 'fields.hourlyCost', type: 'money', hint: 'fields.hourlyCostHint' },
    { key: 'license', label: 'fields.licenseSection', type: 'section' },
    { key: 'licenseNumber', label: 'fields.licenseNumber', type: 'text' },
    { key: 'licenseType', label: 'fields.licenseType', type: 'text', hint: 'fields.licenseTypeHint' },
    { key: 'licenseExpiresOn', label: 'fields.licenseExpiresOn', type: 'date' },
    { key: 'photoFileId', label: 'fields.photo', type: 'file', category: 'photo' },
    notes,
  ],
  defaults: () => ({ category: 'operator' }),
  invalidates: ['crews'],
};

export const CREWS: CatalogConfig = {
  key: 'crews',
  api: 'crews',
  icon: 'groups',
  manage: [P.crewsManage],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'leaderName', label: 'fields.leader' },
    { key: 'shift', label: 'fields.shift', wide: true },
    { key: 'memberCount', label: 'fields.members', type: 'number', align: 'end' },
  ],
  fields: [
    code,
    name,
    { key: 'leaderId', label: 'fields.leader', type: 'lookup', catalog: 'personnel/employees' },
    {
      key: 'shift',
      label: 'fields.shift',
      type: 'select',
      options: [
        { value: 'Diurno', label: 'enums.shift.day' },
        { value: 'Nocturno', label: 'enums.shift.night' },
        { value: 'Mixto', label: 'enums.shift.mixed' },
        { value: '14x14', label: '14 x 14', literal: true },
        { value: '21x7', label: '21 x 7', literal: true },
      ],
    },
    { key: 'memberIds', label: 'fields.members', type: 'lookups', catalog: 'personnel/employees', span: 'full' },
    { key: 'unitIds', label: 'fields.assignedUnits', type: 'lookups', catalog: 'fleet/units', span: 'full' },
    notes,
  ],
  defaults: () => ({ memberIds: [], unitIds: [], shift: 'Diurno' }),
};

// ---- HSE ----------------------------------------------------------------------------------------------------

export const INSPECTION_TEMPLATES: CatalogConfig = {
  key: 'inspectionTemplates',
  api: 'hse/templates',
  icon: 'rule',
  manage: [P.hseManage],
  dialogWidth: '900px',
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r['code'] as string },
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'inspectionCategory' },
    { key: 'items', label: 'fields.questions', value: (r) => ((r['items'] as unknown[]) ?? []).length, align: 'end' },
  ],
  fields: [
    code,
    name,
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'inspectionCategory', values: ['unitPreUse', 'ppe', 'site', 'audit', 'other'], required: true },
    {
      key: 'items',
      label: 'fields.questions',
      type: 'array',
      addLabel: 'fields.addQuestion',
      fields: [
        { key: 'key', label: 'fields.fieldKey', type: 'text', required: true, maxLength: 40 },
        { key: 'question', label: 'fields.question', type: 'text', required: true, span: 'full' },
        { key: 'isCritical', label: 'fields.critical', type: 'checkbox' },
      ],
    },
    notes,
  ],
  defaults: () => ({ category: 'unitPreUse', items: [] }),
};
