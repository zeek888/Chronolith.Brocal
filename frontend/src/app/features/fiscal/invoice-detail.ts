import { ChangeDetectionStrategy, Component, Injector, computed, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, openFile, saveFile } from '../../core/api';
import { DatePipe, DateTimePipe, MoneyPipe, NumPipe } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { InvoiceDetail } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { actionContext } from '../../shared/crud/list-page';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';
import { WorkflowPanel } from '../../shared/workflow-panel';
import { registerPayment } from './register-payment';

/** SAT cancellation motive (01 needs the UUID that replaces the invoice). */
@Component({
  selector: 'br-cancel-invoice',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TPipe],
  template: `
    <h2 mat-dialog-title>{{ 'invoices.cancelTitle' | t: { folio: data.folio } }}</h2>
    <mat-dialog-content>
      <p class="cl-muted">{{ 'invoices.cancelMessage' | t }}</p>
      <mat-form-field class="br-full">
        <mat-label>{{ 'invoices.motive' | t }}</mat-label>
        <mat-select [formControl]="motive">
          @for (m of motives; track m) {
            <mat-option [value]="m">{{ m }} · {{ 'invoices.motives.m' + m | t }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      @if (motive.value === '01') {
        <mat-form-field class="br-full">
          <mat-label>{{ 'invoices.substitution' | t }}</mat-label>
          <input matInput [formControl]="substitution" />
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">{{ 'common.close' | t }}</button>
      <button mat-flat-button class="br-danger" type="button" [disabled]="motive.value === '01' && !substitution.value" (click)="ref.close({ motive: motive.value, substitutionUuid: substitution.value || null })">
        {{ 'invoices.cancel' | t }}
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CancelInvoiceDialog {
  protected readonly data = inject<{ folio: string }>(MAT_DIALOG_DATA);
  protected readonly ref = inject(MatDialogRef<CancelInvoiceDialog>);
  protected readonly motives = ['01', '02', '03', '04'];
  protected readonly motive = new FormControl('02', { nonNullable: true, validators: Validators.required });
  protected readonly substitution = new FormControl<string | null>(null);
}

/** Factura CFDI 4.0: receiver, concepts, totals, fiscal stamp, files, payments and cancellation. */
@Component({
  selector: 'br-invoice-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TPipe, EnumPipe, MoneyPipe, NumPipe, DatePipe, DateTimePipe, CanDirective, PageHeader, StatusChip, WorkflowPanel],
  template: `
    <div class="cl-page">
      @if (invoice.isLoading() || busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (invoice.value(); as inv) {
        <br-page-header [title]="folio()" icon="request_page" [subtitle]="inv.receiver.name">
          <a mat-button [routerLink]="nav.to('facturacion/facturas')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
          @if (inv.summary.status === 'draft') {
            <button *brCan="p.invoicingIssue" mat-flat-button type="button" (click)="stamp()" [disabled]="busy()"><mat-icon>verified</mat-icon>{{ 'invoices.stamp' | t }}</button>
            <button *brCan="p.invoicingIssue" mat-stroked-button type="button" (click)="removeDraft()" [disabled]="busy()"><mat-icon>delete</mat-icon>{{ 'invoices.deleteDraft' | t }}</button>
          }
          <button mat-stroked-button type="button" (click)="file(false, false)"><mat-icon>picture_as_pdf</mat-icon>{{ 'common.pdf' | t }}</button>
          @if (inv.summary.uuid) {
            <button mat-stroked-button type="button" (click)="file(true, true)"><mat-icon>code</mat-icon>{{ 'common.xml' | t }}</button>
          }
          @if (inv.summary.status === 'stamped' && inv.summary.balance > 0 && inv.summary.type === 'income') {
            <button *brCan="p.collectionsManage" mat-stroked-button type="button" (click)="pay()"><mat-icon>paid</mat-icon>{{ 'payments.register' | t }}</button>
          }
          @if (inv.summary.status === 'stamped' && inv.paidAmount === 0) {
            <button *brCan="p.invoicingCancel" mat-stroked-button type="button" (click)="cancel()" [disabled]="busy()"><mat-icon>cancel</mat-icon>{{ 'invoices.cancel' | t }}</button>
          }
        </br-page-header>

        @if (inv.pacProvider === 'Simulado') {
          <div class="br-warning-box br-warning-box--info"><mat-icon>science</mat-icon>{{ 'invoices.simulated' | t }}</div>
        }

        <section class="br-card">
          <div class="br-actions">
            <br-status enum="invoiceStatus" [value]="inv.summary.status" />
            <span class="cl-chip">{{ inv.summary.type | enum: 'cfdiType' }}</span>
            @if (inv.summary.isOverdue) {
              <span class="cl-chip cl-chip--error">{{ 'invoices.overdueDays' | t: { n: inv.summary.daysOverdue } }}</span>
            }
          </div>
          <dl class="br-dl" style="margin-top: 12px">
            <div><dt>{{ 'fields.uuid' | t }}</dt><dd class="br-code-text">{{ inv.summary.uuid ?? '—' }}</dd></div>
            <div><dt>{{ 'fields.issueDate' | t }}</dt><dd>{{ inv.summary.issueDate | d }}</dd></div>
            <div><dt>{{ 'fields.stampedAt' | t }}</dt><dd>{{ inv.stampedAt | dt }}</dd></div>
            <div><dt>{{ 'fields.dueOn' | t }}</dt><dd>{{ inv.summary.dueOn | d }}</dd></div>
            <div><dt>{{ 'fields.paymentMethod' | t }}</dt><dd>{{ inv.paymentMethod }} / {{ inv.paymentForm }}</dd></div>
            <div><dt>{{ 'fields.cfdiUse' | t }}</dt><dd>{{ inv.receiver.cfdiUse }}</dd></div>
            <div><dt>{{ 'fields.copade' | t }}</dt><dd>{{ inv.summary.copadeNumber ?? '—' }}</dd></div>
            <div><dt>{{ 'fields.pac' | t }}</dt><dd>{{ inv.pacProvider ?? '—' }}</dd></div>
          </dl>
        </section>

        <br-workflow-panel document="invoice" [documentId]="inv.summary.id" [version]="inv.summary.status" (changed)="refresh()" />

        <div class="br-grid br-grid--2">
          <section class="br-card">
            <h2>{{ 'invoices.receiver' | t }}</h2>
            <dl class="br-dl">
              <div><dt>{{ 'fields.legalName' | t }}</dt><dd>{{ inv.receiver.name }}</dd></div>
              <div><dt>{{ 'fields.rfc' | t }}</dt><dd>{{ inv.receiver.rfc }}</dd></div>
              <div><dt>{{ 'fields.taxRegime' | t }}</dt><dd>{{ inv.receiver.taxRegime ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.postalCode' | t }}</dt><dd>{{ inv.receiver.postalCode ?? '—' }}</dd></div>
            </dl>
          </section>
          <section class="br-card">
            <h2>{{ 'invoices.issuer' | t }}</h2>
            @if (inv.issuer; as issuer) {
              <dl class="br-dl">
                <div><dt>{{ 'fields.legalName' | t }}</dt><dd>{{ issuer.name }}</dd></div>
                <div><dt>{{ 'fields.rfc' | t }}</dt><dd>{{ issuer.rfc }}</dd></div>
                <div><dt>{{ 'fields.taxRegime' | t }}</dt><dd>{{ issuer.taxRegime }}</dd></div>
                <div><dt>{{ 'invoices.expedition' | t }}</dt><dd>{{ issuer.expeditionPostalCode }}</dd></div>
              </dl>
            } @else {
              <p class="cl-muted">{{ 'invoices.issuerOnStamp' | t }}</p>
            }
          </section>
        </div>

        <section class="br-card">
          <h2>{{ 'invoices.concepts' | t }}</h2>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'fields.satProductCode' | t }}</th>
                  <th>{{ 'fields.description' | t }}</th>
                  <th class="br-end">{{ 'fields.quantity' | t }}</th>
                  <th class="br-end">{{ 'fields.unitPrice' | t }}</th>
                  <th class="br-end">{{ 'fields.amount' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (line of inv.lines; track line.id) {
                  <tr>
                    <td>{{ line.satProductCode }}<div class="br-sub">{{ line.satUnitCode }} {{ line.unitName }}</div></td>
                    <td>
                      {{ line.description }}
                      @if (line.ticketId) {
                        <div class="br-sub"><a [routerLink]="nav.to('boletas', line.ticketId)">{{ 'invoices.viewTicket' | t }}</a></div>
                      }
                    </td>
                    <td class="br-end">{{ line.quantity | num: 3 }}</td>
                    <td class="br-end">{{ line.unitPrice | money: inv.summary.currency }}</td>
                    <td class="br-end">{{ line.amount | money: inv.summary.currency }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="br-totals">
            <span>{{ 'fields.subtotal' | t }}</span><strong>{{ inv.subtotal | money: inv.summary.currency }}</strong>
            @if (inv.discount > 0) {
              <span>{{ 'fields.discount' | t }}</span><strong>-{{ inv.discount | money: inv.summary.currency }}</strong>
            }
            <span>{{ 'fields.vat' | t }}</span><strong>{{ inv.vat | money: inv.summary.currency }}</strong>
            <span class="br-totals__grand">{{ 'fields.total' | t }}</span><strong class="br-totals__grand">{{ inv.summary.total | money: inv.summary.currency }}</strong>
            <span>{{ 'invoices.paid' | t }}</span><strong>{{ inv.paidAmount | money: inv.summary.currency }}</strong>
            <span>{{ 'fields.balance' | t }}</span><strong>{{ inv.summary.balance | money: inv.summary.currency }}</strong>
          </div>
        </section>

        <section class="br-card">
          <h2>{{ 'nav.payments' | t }}</h2>
          <table class="br-table">
            <tbody>
              @for (payment of inv.payments; track payment.paymentId) {
                <tr>
                  <td>{{ payment.paidAt | dt }}</td>
                  <td>{{ 'invoices.partiality' | t: { n: payment.partiality } }}</td>
                  <td class="br-wide">{{ payment.reference }}</td>
                  <td><br-status enum="paymentComplementStatus" [value]="payment.complementStatus" /></td>
                  <td class="br-end">{{ payment.amount | money: inv.summary.currency }}</td>
                </tr>
              } @empty {
                <tr><td class="br-table-empty">{{ 'invoices.noPayments' | t }}</td></tr>
              }
            </tbody>
          </table>
        </section>

        @if (inv.cancellationMotive) {
          <div class="br-warning-box br-warning-box--error">
            <mat-icon>cancel</mat-icon>
            {{ 'invoices.cancelledInfo' | t: { motive: inv.cancellationMotive, date: (inv.cancelledAt | dt) } }}
            @if (inv.substitutionUuid) {
              · {{ inv.substitutionUuid }}
            }
          </div>
        }
      }
    </div>
  `,
  styles: `
    .br-code-text { font-family: ui-monospace, Consolas, monospace; font-size: 0.9em; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InvoiceDetailPage {
  private readonly api = inject(BrocalApi);
  private readonly dialog = inject(MatDialog);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly injector = inject(Injector);
  protected readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;

  readonly id = input.required<string>();
  private readonly reload = signal(0);
  protected readonly busy = signal(false);

  protected readonly invoice = apiResource({
    params: () => ({ id: this.id(), reload: this.reload() }),
    stream: ({ params }) => this.api.get<InvoiceDetail>(`invoices/${params.id}`),
  });

  protected readonly folio = computed(() => {
    const summary = this.invoice.value()?.summary;
    return summary ? (summary.folio ? `${summary.series}-${summary.folio}` : this.i18n.t('invoices.draftOf', { series: summary.series })) : '';
  });

  protected async stamp(): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('invoices.stampTitle'), this.i18n.t('invoices.stampMessage'), this.i18n.t('invoices.stamp'))) {
      await this.run(async () => {
        await firstValueFrom(this.api.post(`invoices/${this.id()}/stamp`));
        this.dialogs.success(this.i18n.t('invoices.stamped'));
      });
    }
  }

  protected async removeDraft(): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('invoices.deleteDraft'), this.i18n.t('invoices.deleteDraftMessage'), this.i18n.t('common.delete'), true)) {
      await firstValueFrom(this.api.delete(`invoices/${this.id()}`));
      await this.nav.navigate(['facturacion/facturas']);
    }
  }

  protected async cancel(): Promise<void> {
    const result = await firstValueFrom(this.dialog.open(CancelInvoiceDialog, { data: { folio: this.folio() }, width: '520px' }).afterClosed());
    if (result) {
      await this.run(() => firstValueFrom(this.api.post(`invoices/${this.id()}/cancel`, result)));
    }
  }

  protected async pay(): Promise<void> {
    if (await registerPayment(actionContext(this.injector), this.invoice.value()?.summary.customerId ?? null)) {
      this.reload.update((r) => r + 1);
    }
  }

  protected file(xml: boolean, download: boolean): void {
    this.api.download(`invoices/${this.id()}/${xml ? 'xml' : 'pdf'}`, undefined, undefined, `${this.folio()}.${xml ? 'xml' : 'pdf'}`).subscribe((f) => (download ? saveFile(f) : openFile(f)));
  }

  /** A decision of its workflow (the stamp may be unlocked). */
  protected refresh(): void {
    this.reload.update((r) => r + 1);
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      this.reload.update((r) => r + 1);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
