import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { money, num } from './format';
import { I18n } from './i18n';
import { Lookups } from './lookups';
import { Audience, AutomationCatalog, AutomationDocument, AutomationDocumentInfo, ConditionMatch, ConditionOperator, FactDefinition, Lookup, Role, RuleCondition } from './models';

/** Operators each kind of datum accepts (same rules as the API). */
export const OPERATORS: Record<FactDefinition['type'], ConditionOperator[]> = {
  number: ['greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual', 'equal', 'notEqual'],
  money: ['greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual', 'equal', 'notEqual'],
  boolean: ['isTrue', 'isFalse'],
  enum: ['in', 'notIn', 'equal', 'notEqual'],
  reference: ['in', 'notIn', 'equal', 'notEqual'],
};

const SYMBOLS: Partial<Record<ConditionOperator, string>> = {
  greaterThan: '>',
  greaterOrEqual: '≥',
  lessThan: '<',
  lessOrEqual: '≤',
  equal: '=',
  notEqual: '≠',
};

/** Catalog of documents, moments and data for workflows and rules (cached per company), and their texts. */
@Injectable({ providedIn: 'root' })
export class AutomationCatalogs {
  private readonly lookups = inject(Lookups);
  private readonly i18n = inject(I18n);

  catalog(): Observable<AutomationCatalog> {
    return this.lookups.cached<AutomationCatalog>('automation/catalog');
  }

  documentName(catalog: AutomationCatalog | null | undefined, document: AutomationDocument): string {
    const info = catalog?.documents.find((d) => d.document === document);
    return info ? this.name(info) : this.i18n.enum('automationDocument', document);
  }

  name(item: { name: string; nameEn: string }): string {
    return this.i18n.language() === 'en' ? item.nameEn : item.name;
  }

  fact(document: AutomationDocumentInfo | null | undefined, key: string): FactDefinition | undefined {
    return document?.facts.find((f) => f.key.toLowerCase() === key.toLowerCase());
  }

  /** Value of a fact as people read it (amounts, counts, yes/no, enum labels, record names). */
  formatValue(fact: FactDefinition | undefined, value: string | null | undefined, names?: ReadonlyMap<string, string>): string {
    if (value === null || value === undefined || value === '') {
      return '—';
    }

    switch (fact?.type) {
      case 'money':
        return money(Number(value));
      case 'number':
        return num(Number(value), 2);
      case 'boolean':
        return this.i18n.t(value === 'true' ? 'common.yes' : 'common.no');
      case 'enum':
        return value
          .split(',')
          .map((v) => (fact.enumName ? this.i18n.enum(fact.enumName, v) : v))
          .join(', ');
      case 'reference':
        return value
          .split(',')
          .map((v) => names?.get(v) ?? this.i18n.t('automation.selectedRecord'))
          .join(', ');
      default:
        return value;
    }
  }

  /** "Total > $50,000.00", "Prioridad es una de Alta, Urgente", "Tiene firma: no" (with the actual value when given). */
  describe(document: AutomationDocumentInfo | undefined, condition: RuleCondition, names?: ReadonlyMap<string, string>, actual?: string | null): string {
    const fact = this.fact(document, condition.field);
    const label = fact ? this.name(fact) : condition.field;
    const current = actual === undefined ? '' : ` (${this.formatValue(fact, actual, names)})`;
    switch (condition.operator) {
      case 'isTrue':
        return `${label}: ${this.i18n.t('common.yes').toLowerCase()}`;
      case 'isFalse':
        return `${label}: ${this.i18n.t('common.no').toLowerCase()}`;
      case 'in':
      case 'notIn':
        return `${label}${current} ${this.i18n.t(`automation.operators.${condition.operator}`)} ${this.formatValue(fact, condition.value, names)}`;
      default:
        return `${label}${current} ${SYMBOLS[condition.operator] ?? condition.operator} ${this.formatValue(fact, condition.value, names)}`;
    }
  }

  /** Conditions joined with "y" / "o"; without conditions the definition always applies. */
  describeAll(document: AutomationDocumentInfo | undefined, conditions: RuleCondition[], match: ConditionMatch, names?: ReadonlyMap<string, string>): string {
    if (conditions.length === 0) {
      return this.i18n.t('automation.always');
    }

    const glue = ` ${this.i18n.t(match === 'all' ? 'automation.and' : 'automation.or')} `;
    return conditions.map((c) => this.describe(document, c, names)).join(glue);
  }

  /** "Facturación, Dirección, administradores…" for an audience. */
  describeAudience(audience: Audience, catalog: AutomationCatalog | null | undefined, roles: Role[] | null | undefined): string {
    const parts = [
      ...audience.roleTemplates.map((key) => this.roleTemplateName(catalog, key)),
      ...audience.roleIds.map((id) => roles?.find((r) => r.id === id)?.name ?? this.i18n.t('automation.companyRole')),
      ...audience.permissions.map((p) => this.i18n.t('automation.withPermission', { permission: p })),
      ...(audience.tenantAdmins ? [this.i18n.t('automation.tenantAdmins')] : []),
    ];
    return parts.join(', ') || '—';
  }

  roleTemplateName(catalog: AutomationCatalog | null | undefined, key: string): string {
    const translated = this.i18n.t(`automation.roleTemplates.${key}`);
    return translated.endsWith(`roleTemplates.${key}`) ? (catalog?.roleTemplates.find((t) => t.key === key)?.name ?? key) : translated;
  }

  /** Names of the records of the reference conditions (customers, sites, units…). */
  referenceCatalogs(document: AutomationDocumentInfo | undefined, conditions: RuleCondition[]): string[] {
    return [...new Set(conditions.map((c) => this.fact(document, c.field)).filter((f) => f?.type === 'reference' && !!f.catalog).map((f) => f!.catalog!))];
  }

  lookupNames(catalog: string): Observable<Lookup[]> {
    return this.lookups.list(catalog, { activeOnly: false });
  }
}
