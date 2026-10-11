import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { DatePipe, MoneyPipe, nowLocalInput } from '../../core/format';
import { TPipe } from '../../core/i18n';
import { InvoiceSummary, Lookup, PagedResult } from '../../core/models';
import { ActionContext } from '../../shared/crud/list-page';
import { LookupSelect } from '../../shared/lookup-select';
import { PAYMENT_FORMS } from '../catalogs/catalog-configs';

interface Application {
  invoice: InvoiceSummary;
  amount: number | null;
}

/** Records a payment of a customer and applies it to one or several of its open invoices (partial payments allowed). */
@Component({
  selector: 'br-register-payment',
  imports: [FormsModule, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TPipe, MoneyPipe, DatePipe, LookupSelect],
  template: `
    <h2 mat-dialog-title>{{ 'payments.register' | t }}</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <mat-dialog-content>
      <div class="cl-form-grid">
        <br-lookup [formControl]="customer" catalog="catalogs/customers" [label]="'fields.customer' | t" [required]="true" (selected)="customerChanged($event)" />
        <mat-form-field>
          <mat-label>{{ 'fields.paidAt' | t }}</mat-label>
          <input matInput type="datetime-local" [formControl]="paidAt" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.paymentForm' | t }}</mat-label>
          <mat-select [formControl]="paymentForm">
            @for (form of forms; track form.value) {
              <mat-option [value]="form.value">{{ form.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.reference' | t }}</mat-label>
          <input matInput [formControl]="reference" />
        </mat-form-field>
      </div>

      <h3 class="br-form-section">{{ 'payments.applyTo' | t }}</h3>
      <div class="cl-table-wrapper">
        <table class="br-table">
          <thead>
            <tr>
              <th>{{ 'fields.folio' | t }}</th>
              <th>{{ 'fields.dueOn' | t }}</th>
              <th class="br-end">{{ 'fields.balance' | t }}</th>
              <th class="br-end">{{ 'payments.applied' | t }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of applications(); track row.invoice.id; let i = $index) {
              <tr>
                <td>{{ row.invoice.series }}-{{ row.invoice.folio }}<div class="br-sub">{{ row.invoice.copadeNumber }}</div></td>
                <td>{{ row.invoice.dueOn | d }}</td>
                <td class="br-end">{{ row.invoice.balance | money }}</td>
                <td class="br-end br-input-cell">
                  <mat-form-field subscriptSizing="dynamic">
                    <input matInput type="number" step="0.01" min="0" [max]="row.invoice.balance" [ngModel]="row.amount" (ngModelChange)="setAmount(i, $event)" />
                    <button matSuffix mat-icon-button type="button" (click)="setAmount(i, row.invoice.balance)" [attr.aria-label]="'payments.full' | t"><mat-icon>done_all</mat-icon></button>
                  </mat-form-field>
                </td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="br-table-empty">{{ customer.value ? ('payments.noOpenInvoices' | t) : ('payments.chooseCustomer' | t) }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <div class="br-totals">
        <span class="br-totals__grand">{{ 'payments.total' | t }}</span><strong class="br-totals__grand">{{ total() | money }}</strong>
      </div>
      <mat-form-field class="br-full" style="margin-top: 12px">
        <mat-label>{{ 'fields.notes' | t }}</mat-label>
        <textarea matInput rows="2" [formControl]="notes"></textarea>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | t }}</button>
      <button mat-flat-button type="button" (click)="save()" [disabled]="saving() || total() <= 0 || customer.invalid">{{ 'payments.register' | t }}</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPaymentDialog {
  private readonly api = inject(BrocalApi);
  private readonly ref = inject(MatDialogRef<RegisterPaymentDialog, string>);
  private readonly data = inject<{ customerId: string | null }>(MAT_DIALOG_DATA);

  protected readonly forms = PAYMENT_FORMS.filter((f) => f.value !== '99');
  protected readonly customer = new FormControl<string | null>(this.data.customerId, Validators.required);
  protected readonly paidAt = new FormControl(nowLocalInput(), Validators.required);
  protected readonly paymentForm = new FormControl<string>('03', { nonNullable: true });
  protected readonly reference = new FormControl<string | null>(null);
  protected readonly notes = new FormControl<string | null>(null);
  protected readonly applications = signal<Application[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly total = computed(() => Math.round(this.applications().reduce((sum, a) => sum + (a.amount ?? 0), 0) * 100) / 100);

  constructor() {
    if (this.data.customerId) {
      void this.load(this.data.customerId);
    }
  }

  protected customerChanged(selected: Lookup | Lookup[] | null): void {
    const id = Array.isArray(selected) ? null : (selected?.id ?? null);
    if (id) {
      void this.load(id);
    } else {
      this.applications.set([]);
    }
  }

  private async load(customerId: string): Promise<void> {
    this.loading.set(true);
    try {
      const page = await firstValueFrom(this.api.get<PagedResult<InvoiceSummary>>('invoices', { customerId, onlyWithBalance: true, pageSize: 100 }));
      this.applications.set(page.items.sort((a, b) => a.dueOn.localeCompare(b.dueOn)).map((invoice) => ({ invoice, amount: null })));
    } finally {
      this.loading.set(false);
    }
  }

  protected setAmount(index: number, value: number | null): void {
    this.applications.update((list) => list.map((a, i) => (i === index ? { ...a, amount: value === null || isNaN(value) ? null : Math.min(value, a.invoice.balance) } : a)));
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    try {
      const result = await firstValueFrom(
        this.api.post<{ id: string }>('collections/payments', {
          customerId: this.customer.value,
          paidAt: new Date(this.paidAt.value!).toISOString(),
          paymentForm: this.paymentForm.value,
          reference: this.reference.value,
          notes: this.notes.value,
          applications: this.applications()
            .filter((a) => (a.amount ?? 0) > 0)
            .map((a) => ({ invoiceId: a.invoice.id, amount: a.amount })),
        }),
      );
      this.ref.close(result.id);
    } catch {
      this.saving.set(false);
    }
  }
}

export async function registerPayment(ctx: ActionContext, customerId: string | null = null): Promise<boolean> {
  const id = await firstValueFrom(ctx.injector.get(MatDialog).open(RegisterPaymentDialog, { data: { customerId }, width: '820px', maxWidth: '96vw' }).afterClosed());
  if (id) {
    ctx.dialogs.success(ctx.i18n.t('payments.registered'));
    return true;
  }

  return false;
}
