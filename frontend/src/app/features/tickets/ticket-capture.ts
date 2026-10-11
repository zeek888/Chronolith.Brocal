import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, FormRecord, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, debounceTime, firstValueFrom, of } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { fileUrl } from '../../core/config';
import { MoneyPipe, fromLocalInput, money, toLocalInput, today } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { currentPosition, newGuid } from '../../core/images';
import { Lookups } from '../../core/lookups';
import { FormFieldDefinition, Lookup, ResolvedPrice, SaveTicketResult, ServiceType, TicketData, TicketDetail, TicketDraft, TicketPhoto } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { TicketOutbox } from '../../core/offline/ticket-outbox';
import { P } from '../../core/permissions';
import { BrocalSession } from '../../core/session';
import { Dialogs } from '../../shared/dialogs';
import { PhotoPicker, PickedPhoto } from '../../shared/file-inputs';
import { LookupSelect } from '../../shared/lookup-select';
import { SignaturePad } from '../../shared/signature-pad';
import { PageHeader } from '../../shared/ui';

type LineGroup = FormGroup<{
  serviceId: FormControl<string | null>;
  quantity: FormControl<number | null>;
  unitPrice: FormControl<number | null>;
  description: FormControl<string | null>;
}>;

type MeterGroup = FormGroup<{ unitId: FormControl<string>; odometer: FormControl<number | null>; hours: FormControl<number | null> }>;

interface NewPhoto extends PickedPhoto {
  caption: string;
}

/**
 * Captura de boleta en campo. Works on a phone without signal: the form keeps its catalogs cached, and
 * when there is no connection the ticket (with its photos and the customer's signature) is queued on the
 * device with an id generated here, so it is sent exactly once when the signal returns.
 */
@Component({
  selector: 'br-ticket-capture',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTooltipModule,
    TPipe,
    MoneyPipe,
    LookupSelect,
    PageHeader,
    PhotoPicker,
    SignaturePad,
  ],
  templateUrl: './ticket-capture.html',
  styleUrl: './ticket-capture.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TicketCapturePage implements OnInit {
  private readonly api = inject(BrocalApi);
  private readonly lookups = inject(Lookups);
  private readonly outbox = inject(TicketOutbox);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);

  readonly id = input<string | null>(null);

  private readonly signaturePad = viewChild(SignaturePad);
  private clientId = newGuid();

  protected readonly form = new FormGroup({
    customerId: new FormControl<string | null>(null, Validators.required),
    contractId: new FormControl<string | null>(null),
    siteId: new FormControl<string | null>(null, Validators.required),
    serviceTypeId: new FormControl<string | null>(null, Validators.required),
    workOrderId: new FormControl<string | null>(null),
    serviceDate: new FormControl<string | null>(today(), Validators.required),
    startedAt: new FormControl<string | null>(null),
    finishedAt: new FormControl<string | null>(null),
    unitIds: new FormControl<string[]>([], { nonNullable: true }),
    crewId: new FormControl<string | null>(null),
    employeeIds: new FormControl<string[]>([], { nonNullable: true }),
    customerRepresentative: new FormControl<string | null>(null, Validators.maxLength(150)),
    description: new FormControl<string | null>(null, Validators.maxLength(4000)),
    observations: new FormControl<string | null>(null, Validators.maxLength(4000)),
  });

  protected readonly answers = new FormRecord<FormControl<string | boolean | null>>({});
  protected readonly lines = new FormArray<LineGroup>([]);
  protected readonly meters = new FormArray<MeterGroup>([]);
  protected readonly signer = new FormGroup({
    name: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(150)]),
    position: new FormControl<string | null>(null, Validators.maxLength(150)),
  });

  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly serviceType = signal<ServiceType | null>(null);
  protected readonly prices = signal<Map<string, ResolvedPrice>>(new Map());
  protected readonly newPhotos = signal<NewPhoto[]>([]);
  protected readonly existingPhotos = signal<TicketPhoto[]>([]);
  protected readonly signNow = signal(false);
  protected readonly folio = signal<string | null>(null);
  protected readonly unitNames = signal<Map<string, string>>(new Map());
  private readonly lineValues = signal<{ serviceId: string | null; quantity: number | null; unitPrice: number | null }[]>([]);

  protected readonly canOverride = computed(() => this.session.can(P.ticketsPriceOverride));
  protected readonly canSign = computed(() => this.session.can(P.ticketsSign));
  protected readonly editing = computed(() => !!this.id());
  protected readonly minPhotos = computed(() => Math.max(this.serviceType()?.minPhotos ?? 0, this.session.tenant()?.settings.minTicketPhotos ?? 0));
  protected readonly photoCount = computed(() => this.newPhotos().length + this.existingPhotos().length);
  protected readonly siteFilter = computed(() => {
    const customer = this.customerId();
    return (item: Lookup) => !customer || !item.parentId || item.parentId === customer;
  });
  private readonly customerId = signal<string | null>(null);

  protected readonly totals = computed(() => {
    const prices = this.prices();
    let subtotal = 0;
    let vat = 0;
    for (const line of this.lineValues()) {
      const resolved = line.serviceId ? prices.get(line.serviceId) : undefined;
      const price = line.unitPrice ?? resolved?.unitPrice ?? 0;
      const amount = Math.round((line.quantity ?? 0) * price * 100) / 100;
      subtotal += amount;
      vat += Math.round(amount * (resolved?.vatRate ?? 0.16) * 100) / 100;
    }

    return { subtotal, vat, total: subtotal + vat };
  });

  async ngOnInit(): Promise<void> {
    this.form.controls.serviceTypeId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => void this.loadServiceType(id));
    this.form.controls.customerId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.customerId.set(id));
    this.form.controls.unitIds.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((ids) => this.syncMeters(ids));
    this.lines.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.lineValues.set(this.lines.getRawValue()));
    this.form.valueChanges
      .pipe(debounceTime(400), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.resolvePrices());
    this.lines.valueChanges.pipe(debounceTime(400), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.resolvePrices());

    this.loading.set(true);
    try {
      const id = this.id();
      const orderId = this.route.snapshot.queryParamMap.get('orden');
      if (id) {
        await this.loadTicket(id);
      } else if (orderId) {
        const draft = await firstValueFrom(this.api.get<TicketDraft>(`work-orders/${orderId}/ticket-draft`));
        this.applyData(draft.data);
        draft.lines.forEach((l) => this.addLine(l.serviceId, l.quantity, null, l.description));
        this.prices.set(new Map(draft.prices.map((p) => [p.serviceId, p])));
      } else {
        const employeeId = this.session.tenant()?.membership.employeeId;
        if (employeeId) {
          this.form.controls.employeeIds.setValue([employeeId]);
        }

        this.form.controls.startedAt.setValue(toLocalInput(new Date().toISOString()));
      }
    } catch {
      // Without signal the capture starts empty; the catalogs come from the device cache.
    } finally {
      this.loading.set(false);
    }
  }

  private async loadTicket(id: string): Promise<void> {
    const ticket = await firstValueFrom(this.api.get<TicketDetail>(`tickets/${id}`));
    this.clientId = ticket.clientId ?? this.clientId;
    this.folio.set(ticket.folio);
    this.applyData({
      customerId: ticket.customerId,
      contractId: ticket.contractId,
      siteId: ticket.siteId,
      serviceTypeId: ticket.serviceTypeId,
      workOrderId: ticket.workOrderId,
      serviceDate: ticket.serviceDate,
      startedAt: ticket.startedAt,
      finishedAt: ticket.finishedAt,
      unitIds: ticket.units.map((u) => u.id),
      crewId: ticket.crewId,
      employeeIds: ticket.employees.map((e) => e.id),
      description: ticket.description,
      observations: ticket.observations,
      customerRepresentative: ticket.customerRepresentative,
      answers: ticket.answers,
      captureLocation: null,
    });
    ticket.lines.forEach((l) => this.addLine(l.serviceId, l.quantity, l.priceSource === 'manual' ? l.unitPrice : null, l.description));
    this.existingPhotos.set(ticket.photos);
  }

  private applyData(data: TicketData): void {
    this.pendingAnswers = data.answers ?? {};
    this.form.patchValue({
      customerId: data.customerId,
      contractId: data.contractId,
      siteId: data.siteId,
      serviceTypeId: data.serviceTypeId,
      workOrderId: data.workOrderId,
      serviceDate: data.serviceDate,
      startedAt: toLocalInput(data.startedAt),
      finishedAt: toLocalInput(data.finishedAt),
      unitIds: data.unitIds ?? [],
      crewId: data.crewId,
      employeeIds: data.employeeIds ?? [],
      description: data.description,
      observations: data.observations,
      customerRepresentative: data.customerRepresentative,
    });
    if (data.customerRepresentative) {
      this.signer.controls.name.setValue(data.customerRepresentative);
    }
  }

  private pendingAnswers: Record<string, string | null> = {};

  /** Builds the dynamic form of the service type (cached for offline use) and suggests its default services. */
  private async loadServiceType(id: string | null): Promise<void> {
    if (!id) {
      this.serviceType.set(null);
      return;
    }

    let type: ServiceType | null = null;
    try {
      type = await firstValueFrom(this.lookups.cached<ServiceType>(`pricing/service-types/${id}`));
    } catch {
      type = null;
    }

    this.serviceType.set(type);
    const current = { ...this.pendingAnswers, ...(this.answers.getRawValue() as Record<string, unknown>) };
    for (const key of Object.keys(this.answers.controls)) {
      this.answers.removeControl(key);
    }

    for (const field of type?.fields ?? []) {
      const value = current[field.key] ?? null;
      const initial = field.type === 'checkbox' ? value === true || value === 'true' : (value as string | null);
      this.answers.addControl(field.key, new FormControl<string | boolean | null>(initial, field.required && field.type !== 'checkbox' ? Validators.required : []));
    }

    if (type && this.lines.length === 0) {
      type.defaultServiceIds.forEach((serviceId) => this.addLine(serviceId, null, null, null));
    }
  }

  protected answerFields(): FormFieldDefinition[] {
    return [...(this.serviceType()?.fields ?? [])].sort((a, b) => a.order - b.order);
  }

  protected answer(key: string): FormControl<string | boolean | null> {
    return this.answers.controls[key];
  }

  protected addLine(serviceId: string | null = null, quantity: number | null = null, unitPrice: number | null = null, description: string | null = null): void {
    this.lines.push(
      new FormGroup({
        serviceId: new FormControl<string | null>(serviceId, Validators.required),
        quantity: new FormControl<number | null>(quantity, [Validators.min(0)]),
        unitPrice: new FormControl<number | null>(unitPrice, [Validators.min(0)]),
        description: new FormControl<string | null>(description, Validators.maxLength(500)),
      }),
    );
  }

  protected removeLine(index: number): void {
    this.lines.removeAt(index);
  }

  protected priceOf(index: number): ResolvedPrice | undefined {
    const serviceId = this.lineValues()[index]?.serviceId;
    return serviceId ? this.prices().get(serviceId) : undefined;
  }

  protected amountOf(index: number): string {
    const line = this.lineValues()[index];
    const price = line?.unitPrice ?? this.priceOf(index)?.unitPrice ?? null;
    return price === null || !line?.quantity ? '—' : money(Math.round(line.quantity * price * 100) / 100);
  }

  private resolvePrices(): void {
    const value = this.form.getRawValue();
    const serviceIds = [...new Set(this.lines.getRawValue().map((l) => l.serviceId).filter((s): s is string => !!s))];
    if (!value.customerId || serviceIds.length === 0 || !navigator.onLine) {
      return;
    }

    this.api
      .get<ResolvedPrice[]>('pricing/resolve', { customerId: value.customerId, contractId: value.contractId, date: value.serviceDate, serviceIds })
      .pipe(catchError(() => of([] as ResolvedPrice[])))
      .subscribe((prices) => {
        if (prices.length > 0) {
          this.prices.set(new Map(prices.map((p) => [p.serviceId, p])));
        }
      });
  }

  protected unitsSelected(selected: Lookup | Lookup[] | null): void {
    const list = Array.isArray(selected) ? selected : selected ? [selected] : [];
    this.unitNames.set(new Map(list.map((u) => [u.id, `${u.code} · ${u.name}`])));
  }

  private syncMeters(ids: string[]): void {
    const current = new Map(this.meters.controls.map((g) => [g.controls.unitId.value, g]));
    this.meters.clear();
    for (const id of ids) {
      this.meters.push(
        current.get(id) ??
          new FormGroup({
            unitId: new FormControl(id, { nonNullable: true }),
            odometer: new FormControl<number | null>(null, Validators.min(0)),
            hours: new FormControl<number | null>(null, Validators.min(0)),
          }),
      );
    }
  }

  protected photosPicked(photos: PickedPhoto[]): void {
    this.newPhotos.update((list) => [...list, ...photos.map((p) => ({ ...p, caption: '' }))]);
  }

  protected removeNewPhoto(index: number): void {
    this.newPhotos.update((list) => {
      URL.revokeObjectURL(list[index].previewUrl);
      return list.filter((_, i) => i !== index);
    });
  }

  protected setCaption(index: number, caption: string): void {
    this.newPhotos.update((list) => list.map((p, i) => (i === index ? { ...p, caption } : p)));
  }

  protected async removeExistingPhoto(photo: TicketPhoto): Promise<void> {
    if (!(await this.dialogs.confirm(this.i18n.t('tickets.removePhotoTitle'), this.i18n.t('tickets.removePhotoMessage'), this.i18n.t('common.remove'), true))) {
      return;
    }

    await firstValueFrom(this.api.delete(`tickets/${this.id()}/photos/${photo.id}`));
    this.existingPhotos.update((list) => list.filter((p) => p.id !== photo.id));
  }

  protected url(relative: string): string {
    return fileUrl(relative) ?? '';
  }

  protected toggleSign(value: boolean): void {
    this.signNow.set(value);
    if (value && !this.signer.controls.name.value) {
      this.signer.controls.name.setValue(this.form.controls.customerRepresentative.value);
    }
  }

  async save(sign: boolean): Promise<void> {
    this.form.markAllAsTouched();
    this.answers.markAllAsTouched();
    this.lines.markAllAsTouched();
    if (this.form.invalid || this.lines.invalid) {
      this.dialogs.info(this.i18n.t('tickets.completeRequired'));
      return;
    }

    let signature: Blob | null = null;
    if (sign) {
      this.signer.markAllAsTouched();
      if (this.answers.invalid) {
        this.dialogs.info(this.i18n.t('tickets.completeForm'));
        return;
      }

      if (this.photoCount() < this.minPhotos()) {
        this.dialogs.info(this.i18n.t('tickets.missingPhotos', { n: this.minPhotos() }));
        return;
      }

      signature = (await this.signaturePad()?.toBlob()) ?? null;
      if (this.signer.invalid || !signature) {
        this.dialogs.info(this.i18n.t('tickets.signatureRequired'));
        return;
      }
    }

    this.saving.set(true);
    const location = await currentPosition(5000);
    const value = this.form.getRawValue();
    const answers: Record<string, string | null> = {};
    for (const [key, control] of Object.entries(this.answers.controls)) {
      const v = control.value;
      answers[key] = typeof v === 'boolean' ? String(v) : v === null || v === '' ? null : String(v);
    }

    const data: TicketData = {
      customerId: value.customerId!,
      contractId: value.contractId,
      siteId: value.siteId!,
      serviceTypeId: value.serviceTypeId!,
      workOrderId: value.workOrderId,
      serviceDate: value.serviceDate!,
      startedAt: fromLocalInput(value.startedAt),
      finishedAt: fromLocalInput(value.finishedAt),
      unitIds: value.unitIds,
      crewId: value.crewId,
      employeeIds: value.employeeIds,
      description: value.description,
      observations: value.observations,
      customerRepresentative: value.customerRepresentative,
      answers,
      captureLocation: location,
    };
    const lines = this.lines
      .getRawValue()
      .filter((l) => l.serviceId && (l.quantity ?? 0) > 0)
      .map((l) => ({ serviceId: l.serviceId!, quantity: l.quantity!, unitPrice: this.canOverride() ? l.unitPrice : null, description: l.description }));
    const meters = this.meters
      .getRawValue()
      .filter((m) => m.odometer !== null || m.hours !== null)
      .map((m) => ({ unitId: m.unitId, odometer: m.odometer, hours: m.hours }));
    const body = { clientId: this.clientId, data, lines, meters };
    const signerName = this.signer.controls.name.value?.trim() ?? '';
    const signerPosition = this.signer.controls.position.value?.trim() || null;

    try {
      if (!navigator.onLine && !this.editing()) {
        await this.queue(body, signature, signerName, signerPosition, location);
        return;
      }

      let id = this.id();
      if (id) {
        await firstValueFrom(this.api.put<SaveTicketResult>(`tickets/${id}`, body));
      } else {
        const result = await firstValueFrom(this.api.post<SaveTicketResult>('tickets', body));
        id = result.id;
      }

      for (const photo of this.newPhotos()) {
        const file = await firstValueFrom(this.api.upload(photo.blob, photo.fileName, 'photo'));
        await firstValueFrom(this.api.post(`tickets/${id}/photos`, { fileId: file.id, caption: photo.caption || null, takenAt: photo.takenAt, location }));
      }

      this.newPhotos.set([]);
      if (signature) {
        const file = await firstValueFrom(this.api.upload(signature, 'firma.png', 'signature'));
        await firstValueFrom(this.api.post(`tickets/${id}/sign`, { signerName, signerPosition, signatureFileId: file.id, signedAt: new Date().toISOString(), location }));
      }

      this.dialogs.success(this.i18n.t(signature ? 'tickets.signedOk' : 'tickets.savedOk'));
      await this.nav.navigate(['boletas', id]);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 0 && !this.editing()) {
        await this.queue(body, signature, signerName, signerPosition, location);
      }
    } finally {
      this.saving.set(false);
    }
  }

  private async queue(
    body: { clientId: string; data: TicketData; lines: { serviceId: string; quantity: number; unitPrice: number | null; description: string | null }[]; meters: { unitId: string; odometer: number | null; hours: number | null }[] },
    signature: Blob | null,
    signerName: string,
    signerPosition: string | null,
    location: TicketData['captureLocation'],
  ): Promise<void> {
    await this.outbox.enqueue({
      clientId: this.clientId,
      tenant: this.session.tenant()!.slug,
      body,
      photos: this.newPhotos().map((p) => ({ blob: p.blob, fileName: p.fileName, caption: p.caption || null, takenAt: p.takenAt, location })),
      signature: signature ? { blob: signature, signerName, signerPosition, signedAt: new Date().toISOString(), location } : null,
      label: `${body.data.serviceDate} · ${this.serviceType()?.name ?? ''}`,
    });
    this.dialogs.info(this.i18n.t('tickets.queuedOffline'));
    await this.nav.navigate(['boletas', 'pendientes']);
  }

  protected meterUnit(index: number): string {
    const id = this.meters.at(index).controls.unitId.value;
    return this.unitNames().get(id) ?? id.slice(0, 8);
  }
}
