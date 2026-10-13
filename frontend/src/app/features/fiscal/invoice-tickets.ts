import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { DatePipe, MoneyPipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { InvoiceDetail, InvoiceableTicket, Lookup } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { apiResource } from '../../core/resource';
import { Dialogs } from '../../shared/dialogs';
import { LookupSelect } from '../../shared/lookup-select';
import { PageHeader, StatusChip } from '../../shared/ui';
import { CFDI_USES, PAYMENT_FORMS, PAYMENT_METHODS } from '../catalogs/catalog-configs';

/**
 * "La boleta firmada se convierte en factura en un clic": tickets signed or validated and not invoiced yet,
 * grouped by customer; the selected ones become one CFDI (draft or stamped right away).
 */
@Component({
  selector: 'br-invoice-tickets',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, TPipe, MoneyPipe, DatePipe, LookupSelect, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.invoiceTickets' | t" icon="post_add" [subtitle]="'invoices.fromTicketsHint' | t">
        <a mat-button [routerLink]="nav.to('facturacion/facturas')"><mat-icon>arrow_back</mat-icon>{{ 'nav.invoices' | t }}</a>
      </br-page-header>

      <div class="cl-toolbar-row br-filters">
        <br-lookup class="br-filter--lookup" catalog="catalogs/customers" [label]="'fields.customer' | t" [value]="customerId()" (selected)="customerChanged($event)" />
      </div>

      @if (tickets.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @for (group of groups(); track group.customerId) {
        <section class="br-card">
          <div class="br-card__header">
            <mat-checkbox [checked]="allSelected(group.tickets)" [indeterminate]="someSelected(group.tickets)" (change)="toggleGroup(group.tickets, $event.checked)" />
            <h2>{{ group.customerName }}</h2>
            <span class="cl-muted">{{ 'invoices.ticketsReady' | t: { n: group.tickets.length } }}</span>
          </div>
          <table class="br-table">
            <tbody>
              @for (t of group.tickets; track t.id) {
                <tr [class.br-row-muted]="t.inDraft || !!t.held">
                  <td style="width: 1%"><mat-checkbox [checked]="selected().has(t.id)" [disabled]="t.inDraft || !!t.held || (!!selectedCustomer() && selectedCustomer() !== t.customerId)" (change)="toggle(t, $event.checked)" /></td>
                  <td>
                    <a class="cl-row-link" [routerLink]="nav.to('boletas', t.id)">{{ t.folio }}</a>
                    @if (t.inDraft) {
                      <div class="br-sub">{{ 'invoices.inDraft' | t }}</div>
                    } @else if (t.held) {
                      <div class="br-sub">{{ (t.held === 'pending' ? 'invoices.heldPending' : 'invoices.heldRejected') | t }}</div>
                    }
                  </td>
                  <td>{{ t.serviceDate | d }}</td>
                  <td class="br-wide">{{ t.siteName }}</td>
                  <td><br-status enum="ticketStatus" [value]="t.status" /></td>
                  <td class="br-end">{{ t.subtotal | money }}</td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      } @empty {
        @if (!tickets.isLoading()) {
          <section class="br-card cl-muted">{{ 'invoices.nothingToInvoice' | t }}</section>
        }
      }

      @if (selected().size > 0) {
        <section class="br-card br-invoice-bar">
          <h2>{{ 'invoices.newInvoice' | t: { n: selected().size } }}</h2>
          <div class="cl-form-grid">
            <mat-form-field>
              <mat-label>{{ 'fields.paymentMethod' | t }}</mat-label>
              <mat-select [formControl]="paymentMethod">
                <mat-option [value]="null">{{ 'invoices.fromCustomer' | t }}</mat-option>
                @for (o of methods; track o.value) {
                  <mat-option [value]="o.value">{{ o.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'fields.paymentForm' | t }}</mat-label>
              <mat-select [formControl]="paymentForm">
                <mat-option [value]="null">{{ 'invoices.fromCustomer' | t }}</mat-option>
                @for (o of forms; track o.value) {
                  <mat-option [value]="o.value">{{ o.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'fields.cfdiUse' | t }}</mat-label>
              <mat-select [formControl]="cfdiUse">
                <mat-option [value]="null">{{ 'invoices.fromCustomer' | t }}</mat-option>
                @for (o of uses; track o.value) {
                  <mat-option [value]="o.value">{{ o.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'fields.copade' | t }}</mat-label>
              <input matInput [formControl]="copade" />
            </mat-form-field>
            <mat-form-field class="cl-full-row">
              <mat-label>{{ 'fields.notes' | t }}</mat-label>
              <input matInput [formControl]="notes" />
            </mat-form-field>
          </div>
          <div class="br-actions">
            <strong>{{ 'fields.subtotal' | t }}: {{ selectedTotal() | money }}</strong>
            <span class="cl-spacer"></span>
            <mat-slide-toggle [formControl]="stamp">{{ 'invoices.stampNow' | t }}</mat-slide-toggle>
            <button mat-flat-button type="button" (click)="create()" [disabled]="busy()"><mat-icon>request_page</mat-icon>{{ 'invoices.create' | t }}</button>
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .br-invoice-bar { position: sticky; bottom: 0; z-index: 2; box-shadow: 0 -4px 12px rgb(0 0 0 / 0.08); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InvoiceTicketsPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly route = inject(ActivatedRoute);
  protected readonly nav = inject(BrocalNav);

  protected readonly methods = PAYMENT_METHODS;
  protected readonly forms = PAYMENT_FORMS;
  protected readonly uses = CFDI_USES;
  protected readonly customerId = signal<string | null>(this.route.snapshot.queryParamMap.get('cliente'));
  protected readonly selected = signal<Set<string>>(new Set(this.route.snapshot.queryParamMap.getAll('boleta')));
  protected readonly busy = signal(false);
  protected readonly paymentMethod = new FormControl<string | null>(null);
  protected readonly paymentForm = new FormControl<string | null>(null);
  protected readonly cfdiUse = new FormControl<string | null>(null);
  protected readonly copade = new FormControl<string | null>(null);
  protected readonly notes = new FormControl<string | null>(null);
  protected readonly stamp = new FormControl(true, { nonNullable: true });

  protected readonly tickets = apiResource({
    params: () => ({ customerId: this.customerId() }),
    stream: ({ params }) => this.api.get<InvoiceableTicket[]>('invoices/invoiceable-tickets', params),
  });

  protected readonly groups = computed(() => {
    const map = new Map<string, { customerId: string; customerName: string; tickets: InvoiceableTicket[] }>();
    for (const t of this.tickets.value() ?? []) {
      const group = map.get(t.customerId) ?? { customerId: t.customerId, customerName: t.customerName ?? '—', tickets: [] };
      group.tickets.push(t);
      map.set(t.customerId, group);
    }

    return [...map.values()];
  });

  /** One CFDI per customer: the selection is limited to the customer of the first ticket picked. */
  protected readonly selectedCustomer = computed(() => {
    const ids = this.selected();
    return (this.tickets.value() ?? []).find((t) => ids.has(t.id))?.customerId ?? null;
  });

  protected readonly selectedTotal = computed(() => (this.tickets.value() ?? []).filter((t) => this.selected().has(t.id)).reduce((s, t) => s + t.subtotal, 0));

  protected customerChanged(selected: Lookup | Lookup[] | null): void {
    this.customerId.set(Array.isArray(selected) ? null : (selected?.id ?? null));
  }

  protected toggle(ticket: InvoiceableTicket, checked: boolean): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (checked) {
        next.add(ticket.id);
      } else {
        next.delete(ticket.id);
      }

      return next;
    });
  }

  protected allSelected(tickets: InvoiceableTicket[]): boolean {
    const available = tickets.filter((t) => !t.inDraft && !t.held);
    return available.length > 0 && available.every((t) => this.selected().has(t.id));
  }

  protected someSelected(tickets: InvoiceableTicket[]): boolean {
    return tickets.some((t) => this.selected().has(t.id)) && !this.allSelected(tickets);
  }

  protected toggleGroup(tickets: InvoiceableTicket[], checked: boolean): void {
    const available = tickets.filter((t) => !t.inDraft && !t.held);
    this.selected.set(checked ? new Set(available.map((t) => t.id)) : new Set());
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await firstValueFrom(
        this.api.post<{ id: string }>('invoices/from-tickets', {
          ticketIds: [...this.selected()],
          paymentForm: this.paymentForm.value,
          paymentMethod: this.paymentMethod.value,
          cfdiUse: this.cfdiUse.value,
          copadeNumber: this.copade.value,
          estimateId: null,
          notes: this.notes.value,
          stamp: this.stamp.value,
        }),
      );
      // An approval workflow of the company can hold the stamp: the draft stays with its request.
      const invoice = this.stamp.value ? await firstValueFrom(this.api.get<InvoiceDetail>(`invoices/${result.id}`)) : null;
      if (invoice?.summary.status === 'draft') {
        this.dialogs.info(this.i18n.t('invoices.stampHeld'));
      } else {
        this.dialogs.success(this.i18n.t(this.stamp.value ? 'invoices.stamped' : 'invoices.draftCreated'));
      }

      await this.nav.navigate(['facturacion/facturas', result.id]);
    } catch {
      this.busy.set(false);
    }
  }
}
