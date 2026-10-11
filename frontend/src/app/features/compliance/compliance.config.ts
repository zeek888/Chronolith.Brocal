import { firstValueFrom } from 'rxjs';
import { fileUrl } from '../../core/config';
import { nowLocalInput } from '../../core/format';
import { ComplianceDocument, DocumentSubject, Movement, StockRow, ToolAssignment } from '../../core/models';
import { P } from '../../core/permissions';
import { FieldDef } from '../../shared/crud/fields';
import { DialogSpec, ListConfig } from '../../shared/crud/list-page';

const STATUSES = ['valid', 'expiringSoon', 'expired', 'noExpiry'] as const;

function documentFields(subject: DocumentSubject): FieldDef[] {
  return [
    subject === 'unit'
      ? { key: 'subjectId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units', required: true }
      : { key: 'subjectId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees', required: true },
    { key: 'documentTypeId', label: 'fields.documentType', type: 'lookup', catalog: 'compliance/document-types', query: { appliesTo: subject }, required: true },
    { key: 'number', label: 'fields.number', type: 'text' },
    { key: 'issuer', label: 'fields.issuer', type: 'text', hint: subject === 'employee' ? 'compliance.issuerHintPerson' : 'compliance.issuerHintUnit' },
    { key: 'issuedOn', label: 'fields.issuedOn', type: 'date' },
    { key: 'expiresOn', label: 'fields.expiresOn', type: 'date', hint: 'compliance.expiresHint' },
    ...(subject === 'employee' ? [{ key: 'hours', label: 'compliance.hours', type: 'number' as const, min: 0, hint: 'compliance.hoursHint' }] : []),
    { key: 'fileId', label: 'compliance.file', type: 'file', category: 'document', fileUrlKey: 'fileUrl', span: 'full' },
    { key: 'notes', label: 'fields.notes', type: 'textarea' },
  ];
}

export function documentDialog(subject: DocumentSubject, existing: ComplianceDocument | null = null): DialogSpec {
  const fields = documentFields(subject);
  return {
    title: existing ? `${existing.documentTypeName} · ${existing.subjectCode}` : undefined,
    titleKey: subject === 'unit' ? 'records.unitDocument' : 'records.personnelDocument',
    fields,
    width: '720px',
    save: (api, v) => {
      const { subjectId, ...data } = v;
      const body = { subject, subjectId, data };
      return existing ? api.put(`compliance/documents/${existing.id}`, body) : api.post('compliance/documents', body);
    },
  };
}

/** Permisos y certificaciones por unidad (SCT, ASEA/SEMARNAT, pólizas) or expediente de personal (DC-3, H2S, médicos, SSPA). */
export function documentList(subject: DocumentSubject): ListConfig<ComplianceDocument> {
  const unit = subject === 'unit';
  return {
    key: unit ? 'unitPermits' : 'personnelDocuments',
    icon: unit ? 'verified' : 'workspace_premium',
    api: 'compliance/documents',
    query: { subject },
    exportDataset: 'documents',
    create: { permissions: [unit ? P.permitsManage : P.personnelManage], dialog: documentDialog(subject), label: 'compliance.newDocument' },
    headerActions: [
      { key: 'matrix', icon: 'grid_on', label: 'compliance.matrix', path: unit ? 'permisos/matriz' : 'personal/matriz' },
      { key: 'types', icon: 'description', label: 'nav.documentTypes', path: 'permisos/tipos', permissions: [P.permitsManage, P.personnelManage] },
    ],
    filters: [
      { key: 'search', label: 'common.search', type: 'search' },
      unit ? { key: 'subjectId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' } : { key: 'subjectId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees' },
      { key: 'status', label: 'fields.status', type: 'enum', enum: 'complianceStatus', values: STATUSES },
    ],
    columns: [
      { key: 'subjectCode', label: unit ? 'fields.unit' : 'fields.employee', sub: (r) => r.subjectName },
      { key: 'documentTypeName', label: 'fields.documentType', sub: (r) => r.number },
      { key: 'issuer', label: 'fields.issuer', wide: true },
      { key: 'expiresOn', label: 'fields.expiresOn', type: 'date', sub: (r) => (r.daysLeft === null ? null : r.daysLeft < 0 ? `vencido hace ${-r.daysLeft} d` : `${r.daysLeft} d`) },
      { key: 'status', label: 'fields.status', type: 'status', enum: 'complianceStatus' },
      { key: 'fileUrl', label: 'compliance.file', value: (r) => (r.fileUrl ? '📎' : ''), wide: true },
    ],
    rowActions: [
      {
        key: 'open',
        icon: 'open_in_new',
        label: 'compliance.openFile',
        visible: (r) => !!r.fileUrl,
        run: async (_ctx, row) => {
          window.open(fileUrl(row.fileUrl) ?? '', '_blank', 'noopener');
          return false;
        },
      },
      {
        key: 'edit',
        icon: 'edit',
        label: 'common.edit',
        permissions: [unit ? P.permitsManage : P.personnelManage],
        run: async (ctx, row) => !!(await ctx.form(documentDialog(subject, row), { ...row })),
      },
      {
        key: 'renew',
        icon: 'autorenew',
        label: 'compliance.renew',
        permissions: [unit ? P.permitsManage : P.personnelManage],
        run: async (ctx, row) =>
          !!(await ctx.form(documentDialog(subject), { subjectId: row.subjectId, documentTypeId: row.documentTypeId, issuer: row.issuer })),
      },
      {
        key: 'delete',
        icon: 'delete',
        label: 'common.delete',
        permissions: [unit ? P.permitsManage : P.personnelManage],
        run: async (ctx, row) => {
          if (!(await ctx.dialogs.confirm(ctx.i18n.t('common.deleteTitle'), ctx.i18n.t('compliance.deleteMessage', { name: row.documentTypeName ?? '' }), ctx.i18n.t('common.delete'), true))) {
            return false;
          }

          await firstValueFrom(ctx.api.delete(`compliance/documents/${row.id}`, { subject }));
          return true;
        },
      },
    ],
  };
}

// ---- Inventory ---------------------------------------------------------------------------------------------

const MOVEMENT_TYPES = ['receipt', 'issue', 'transfer', 'adjustment', 'crewIssue', 'return'] as const;

export const MOVEMENT_DIALOG: DialogSpec = {
  titleKey: 'records.movement',
  width: '760px',
  fields: [
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'movementType', values: MOVEMENT_TYPES, required: true },
    { key: 'itemId', label: 'fields.item', type: 'lookup', catalog: 'inventory/items', required: true },
    { key: 'warehouseId', label: 'fields.warehouse', type: 'lookup', catalog: 'inventory/warehouses', required: true },
    { key: 'targetWarehouseId', label: 'inventory.targetWarehouse', type: 'lookup', catalog: 'inventory/warehouses', showIf: (v) => v['type'] === 'transfer' },
    { key: 'quantity', label: 'fields.quantity', type: 'number', required: true, min: 0, hint: 'inventory.quantityHint' },
    { key: 'unitCost', label: 'fields.unitCost', type: 'money', showIf: (v) => v['type'] === 'receipt' },
    { key: 'movedAt', label: 'inventory.movedAt', type: 'datetime', required: true },
    { key: 'reference', label: 'fields.reference', type: 'text', hint: 'inventory.referenceHint' },
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units', showIf: (v) => v['type'] === 'issue' },
    { key: 'crewId', label: 'fields.crew', type: 'lookup', catalog: 'crews', showIf: (v) => v['type'] === 'crewIssue' || v['type'] === 'issue' },
    { key: 'employeeId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees', showIf: (v) => v['type'] === 'crewIssue' || v['type'] === 'issue' },
    { key: 'notes', label: 'fields.notes', type: 'textarea' },
  ],
  defaults: () => ({ type: 'receipt', movedAt: nowLocalInput() }),
  save: (api, v) => api.post('inventory/movements', { ...v, movedAt: new Date(v['movedAt'] as string).toISOString(), maintenanceOrderId: null }),
};

export const STOCK_LIST: ListConfig<StockRow> = {
  key: 'stock',
  icon: 'inventory_2',
  api: 'inventory/stock',
  paged: false,
  exportDataset: 'stock',
  create: { permissions: [P.inventoryMove, P.inventoryManage], dialog: MOVEMENT_DIALOG, label: 'inventory.newMovement' },
  headerActions: [{ key: 'items', icon: 'category', label: 'nav.items', path: 'inventario/articulos' }],
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'warehouseId', label: 'fields.warehouse', type: 'lookup', catalog: 'inventory/warehouses' },
    { key: 'onlyWithStock', label: 'inventory.onlyWithStock', type: 'toggle', default: true },
  ],
  columns: [
    { key: 'itemName', label: 'fields.item', sub: (r) => r.itemCode },
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'itemCategory', wide: true },
    { key: 'warehouseName', label: 'fields.warehouse' },
    { key: 'quantity', label: 'inventory.onHand', align: 'end', value: (r) => `${r.quantity} ${r.unitName}`, sub: (r, t) => (r.quantity < r.minStock ? t('inventory.belowMinimum', { min: r.minStock }) : null) },
    { key: 'averageCost', label: 'fields.averageCost', type: 'money', align: 'end', wide: true },
    { key: 'value', label: 'inventory.value', type: 'money', align: 'end' },
  ],
  muted: (r) => r.quantity <= 0,
};

export const MOVEMENT_LIST: ListConfig<Movement> = {
  key: 'movements',
  icon: 'swap_horiz',
  api: 'inventory/movements',
  create: { permissions: [P.inventoryMove, P.inventoryManage], dialog: MOVEMENT_DIALOG, label: 'inventory.newMovement' },
  filters: [
    { key: 'itemId', label: 'fields.item', type: 'lookup', catalog: 'inventory/items' },
    { key: 'warehouseId', label: 'fields.warehouse', type: 'lookup', catalog: 'inventory/warehouses' },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'movementType', values: [...MOVEMENT_TYPES, 'maintenance'] },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'movedAt', label: 'inventory.movedAt', type: 'datetime', sub: (r) => r.reference },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'movementType' },
    { key: 'itemName', label: 'fields.item', sub: (r) => r.itemCode },
    { key: 'warehouseName', label: 'fields.warehouse', sub: (r) => (r.targetWarehouseName ? `→ ${r.targetWarehouseName}` : null), wide: true },
    { key: 'quantity', label: 'fields.quantity', type: 'number', align: 'end' },
    { key: 'totalCost', label: 'fields.amount', type: 'money', align: 'end', wide: true },
    { key: 'crewName', label: 'inventory.assignedTo', value: (r) => r.crewName ?? r.employeeName ?? '—', wide: true },
    { key: 'createdBy', label: 'fields.createdBy', wide: true },
  ],
};

export const TOOL_LIST: ListConfig<ToolAssignment> = {
  key: 'tools',
  icon: 'construction',
  api: 'inventory/tools',
  paged: false,
  create: {
    permissions: [P.inventoryMove, P.inventoryManage],
    label: 'inventory.assignTool',
    dialog: {
      titleKey: 'records.toolAssignment',
      fields: [
        { key: 'itemId', label: 'fields.item', type: 'lookup', catalog: 'inventory/items', required: true },
        { key: 'warehouseId', label: 'fields.warehouse', type: 'lookup', catalog: 'inventory/warehouses', required: true },
        { key: 'quantity', label: 'fields.quantity', type: 'number', required: true, min: 0 },
        { key: 'serialNumber', label: 'fields.serialNumber', type: 'text' },
        { key: 'crewId', label: 'fields.crew', type: 'lookup', catalog: 'crews' },
        { key: 'employeeId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees' },
        { key: 'assignedAt', label: 'inventory.assignedAt', type: 'datetime', required: true },
        { key: 'expectedReturnOn', label: 'inventory.expectedReturnOn', type: 'date' },
        { key: 'notes', label: 'fields.notes', type: 'textarea' },
      ],
      defaults: () => ({ quantity: 1, assignedAt: nowLocalInput() }),
      save: (api, v) => api.post('inventory/tools', { ...v, assignedAt: new Date(v['assignedAt'] as string).toISOString() }),
    },
  },
  filters: [
    { key: 'crewId', label: 'fields.crew', type: 'lookup', catalog: 'crews' },
    { key: 'employeeId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees' },
    { key: 'openOnly', label: 'inventory.openOnly', type: 'toggle', default: true },
  ],
  columns: [
    { key: 'itemName', label: 'fields.item', sub: (r) => r.serialNumber },
    { key: 'quantity', label: 'fields.quantity', type: 'number', align: 'end' },
    { key: 'crewName', label: 'inventory.assignedTo', value: (r) => r.crewName ?? r.employeeName ?? '—' },
    { key: 'assignedAt', label: 'inventory.assignedAt', type: 'date', wide: true },
    { key: 'expectedReturnOn', label: 'inventory.expectedReturnOn', type: 'date', sub: (r) => (r.isOverdue ? '⚠ vencido' : null) },
    { key: 'returnedAt', label: 'inventory.returnedAt', type: 'date', sub: (r) => r.returnCondition, wide: true },
  ],
  rowActions: [
    {
      key: 'return',
      icon: 'assignment_return',
      label: 'inventory.return',
      permissions: [P.inventoryMove, P.inventoryManage],
      visible: (r) => !r.returnedAt,
      run: async (ctx, row) =>
        !!(await ctx.form(
          {
            title: ctx.i18n.t('inventory.returnTitle', { item: row.itemName ?? '' }),
            fields: [
              { key: 'condition', label: 'inventory.condition', type: 'text', hint: 'inventory.conditionHint' },
              { key: 'backToStock', label: 'inventory.backToStock', type: 'checkbox' },
            ],
            save: (api, v) => api.post(`inventory/tools/${row.id}/return`, { condition: v['condition'] ?? null, backToStock: !!v['backToStock'] }),
            width: '520px',
          },
          { backToStock: true },
        )),
    },
  ],
  muted: (r) => !!r.returnedAt,
};
