import { ChangeDetectionStrategy, Component, computed, inject, input, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, openFile, saveFile } from '../../core/api';
import { fileUrl } from '../../core/config';
import { DatePipe, DateTimePipe, MoneyPipe, NumPipe } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { currentPosition } from '../../core/images';
import { Lookups } from '../../core/lookups';
import { ServiceType, TicketCost, TicketDetail } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { Dialogs } from '../../shared/dialogs';
import { SignaturePad } from '../../shared/signature-pad';
import { KpiCard, PageHeader, StatusChip } from '../../shared/ui';
import { WorkflowPanel } from '../../shared/workflow-panel';

export interface SignResult {
  blob: Blob;
  signerName: string;
  signerPosition: string | null;
}

/** Customer's signature on the device of the field crew. */
@Component({
  selector: 'br-sign-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TPipe, SignaturePad],
  template: `
    <h2 mat-dialog-title>{{ 'tickets.signTitle' | t: { folio: data.folio } }}</h2>
    <mat-dialog-content>
      <p class="cl-muted">{{ 'tickets.signatureLegal' | t }}</p>
      <div class="cl-form-grid">
        <mat-form-field>
          <mat-label>{{ 'fields.signerName' | t }}</mat-label>
          <input matInput [formControl]="name" />
          <mat-error>{{ 'validation.required' | t }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.signerPosition' | t }}</mat-label>
          <input matInput [formControl]="position" />
        </mat-form-field>
      </div>
      <br-signature-pad (changed)="signed.set($event)" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | t }}</button>
      <button mat-flat-button type="button" [disabled]="!signed() || name.invalid" (click)="accept()">{{ 'tickets.confirmSignature' | t }}</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignDialog {
  protected readonly data = inject<{ folio: string; signer: string | null }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<SignDialog, SignResult>);
  private readonly pad = viewChild.required(SignaturePad);
  protected readonly name = new FormControl(this.data.signer ?? '', [Validators.required, Validators.maxLength(150)]);
  protected readonly position = new FormControl('', Validators.maxLength(150));
  protected readonly signed = signal(false);

  protected async accept(): Promise<void> {
    const blob = await this.pad().toBlob();
    if (blob && this.name.valid) {
      this.ref.close({ blob, signerName: this.name.value!.trim(), signerPosition: this.position.value?.trim() || null });
    }
  }
}

/**
 * Boleta: everything captured in the field (form of the service type, concepts with frozen prices,
 * personnel, units, photos, signature with time and position) and its life cycle: sign, validate,
 * invoice, cancel or reopen with a reason (both audited), PDF with the company branding and job costing.
 */
@Component({
  selector: 'br-ticket-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TPipe, EnumPipe, MoneyPipe, NumPipe, DatePipe, DateTimePipe, CanDirective, KpiCard, PageHeader, StatusChip, WorkflowPanel],
  templateUrl: './ticket-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TicketDetailPage {
  private readonly api = inject(BrocalApi);
  private readonly dialog = inject(MatDialog);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly lookups = inject(Lookups);
  protected readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;

  readonly id = input.required<string>();
  private readonly reload = signal(0);
  protected readonly busy = signal(false);
  protected readonly cost = signal<TicketCost | null>(null);
  /** A validation request in progress: its approvers validate the ticket. */
  protected readonly workflowPending = signal(false);

  protected readonly ticket = apiResource({
    params: () => ({ id: this.id(), reload: this.reload() }),
    stream: ({ params }) => this.api.get<TicketDetail>(`tickets/${params.id}`),
  });

  protected readonly serviceType = apiResource({
    params: () => this.ticket.value()?.serviceTypeId,
    stream: ({ params }) => this.lookups.cached<ServiceType>(`pricing/service-types/${params}`),
  });

  protected readonly answers = computed(() => {
    const ticket = this.ticket.value();
    const type = this.serviceType.value();
    if (!ticket) {
      return [];
    }

    const fields = [...(type?.fields ?? [])].sort((a, b) => a.order - b.order);
    const known = fields.map((f) => ({ label: f.label, value: format(ticket.answers[f.key], f.type, this.i18n), unit: f.unit }));
    const extra = Object.entries(ticket.answers)
      .filter(([key]) => !fields.some((f) => f.key === key))
      .map(([key, value]) => ({ label: key, value: value ?? '—', unit: null as string | null }));
    return [...known, ...extra];
  });

  protected readonly isDraft = computed(() => this.ticket.value()?.status === 'draft');
  protected readonly canInvoice = computed(() => {
    const status = this.ticket.value()?.status;
    return status === 'approved' || (status === 'signed' && !this.session.tenant()?.settings.requireApprovalBeforeInvoice);
  });

  protected url(relative: string | null): string | null {
    return fileUrl(relative);
  }

  protected async sign(): Promise<void> {
    const ticket = this.ticket.value();
    if (!ticket) {
      return;
    }

    const result = await firstValueFrom(this.dialog.open(SignDialog, { data: { folio: ticket.folio, signer: ticket.customerRepresentative }, width: '640px', maxWidth: '96vw' }).afterClosed());
    if (!result) {
      return;
    }

    const signature = result as SignResult;
    await this.run(async () => {
      const location = await currentPosition(5000);
      const file = await firstValueFrom(this.api.upload(signature.blob, 'firma.png', 'signature'));
      await firstValueFrom(
        this.api.post(`tickets/${ticket.id}/sign`, { signerName: signature.signerName, signerPosition: signature.signerPosition, signatureFileId: file.id, signedAt: new Date().toISOString(), location }),
      );
      this.dialogs.success(this.i18n.t('tickets.signedOk'));
    });
  }

  protected async closeWithoutSignature(): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('tickets.closeTitle'), this.i18n.t('tickets.closeMessage'))) {
      await this.run(() => firstValueFrom(this.api.post(`tickets/${this.id()}/close`)));
    }
  }

  protected async approve(): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.post('tickets/approve', { ids: [this.id()] }));
      this.dialogs.success(this.i18n.t('tickets.approvedOk'));
    });
  }

  protected async cancel(): Promise<void> {
    const reason = await this.dialogs.prompt(this.i18n.t('tickets.cancelTitle'), this.i18n.t('fields.reason'), { message: this.i18n.t('tickets.cancelMessage'), confirm: this.i18n.t('tickets.cancel') });
    if (reason) {
      await this.run(() => firstValueFrom(this.api.post(`tickets/${this.id()}/cancel`, { reason })));
    }
  }

  protected async reopen(): Promise<void> {
    const reason = await this.dialogs.prompt(this.i18n.t('tickets.reopenTitle'), this.i18n.t('fields.reason'), { message: this.i18n.t('tickets.reopenMessage'), confirm: this.i18n.t('tickets.reopen') });
    if (reason) {
      await this.run(() => firstValueFrom(this.api.post(`tickets/${this.id()}/reopen`, { reason })));
    }
  }

  protected async remove(): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('tickets.deleteTitle'), this.i18n.t('tickets.deleteMessage'), this.i18n.t('common.delete'), true)) {
      await firstValueFrom(this.api.delete(`tickets/${this.id()}`));
      await this.nav.navigate(['boletas']);
    }
  }

  protected pdf(download: boolean): void {
    this.api.download(`tickets/${this.id()}/pdf`, { regenerate: false }, undefined, `${this.ticket.value()?.folio ?? 'boleta'}.pdf`).subscribe((file) => (download ? saveFile(file) : openFile(file)));
  }

  protected loadCost(): void {
    this.api.get<TicketCost>(`tickets/${this.id()}/cost`).subscribe((cost) => this.cost.set(cost));
  }

  /** A decision of its validation workflow can validate the ticket. */
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

function format(value: string | null | undefined, type: string, i18n: I18n): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }

  if (type === 'checkbox') {
    return i18n.t(value === 'true' ? 'common.yes' : 'common.no');
  }

  return value;
}
