import { ChangeDetectionStrategy, Component, Injector, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { BrocalApi } from '../../core/api';
import { fileUrl } from '../../core/config';
import { DatePipe, DateTimePipe, NumPipe, nowLocalInput } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { Kpi, UnitOverview, UnitStatus, UploadedFile } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { actionContext } from '../../shared/crud/list-page';
import { KpiCard, PageHeader, StatusChip } from '../../shared/ui';
import { UNITS } from '../catalogs/catalog-configs';
import { FUEL_DIALOG } from './fleet.config';

const HISTORY_ICONS: Record<string, string> = { meter: 'speed', status: 'swap_horiz', ticket: 'receipt_long', maintenance: 'build', fuel: 'local_gas_station' };

/** Ficha de la unidad: status, meters, permits with traffic lights and history (tickets, workshop, fuel, readings). */
@Component({
  selector: 'br-unit-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatProgressBarModule, TPipe, EnumPipe, NumPipe, DatePipe, DateTimePipe, CanDirective, KpiCard, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      @if (overview.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (overview.value(); as o) {
        <br-page-header [title]="o.unit.code + ' · ' + o.unit.name" icon="local_shipping" [subtitle]="(o.unit.type | enum: 'unitType') + (o.unit.plates ? ' · ' + o.unit.plates : '')">
          <a mat-button [routerLink]="nav.to('flota/unidades')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
          <button *brCan="[p.fleetMeters, p.fleetManage]" mat-stroked-button type="button" (click)="meters()"><mat-icon>speed</mat-icon>{{ 'units.recordMeters' | t }}</button>
          <button *brCan="[p.fleetManage, p.maintenanceManage]" mat-stroked-button type="button" [matMenuTriggerFor]="statusMenu"><mat-icon>swap_horiz</mat-icon>{{ 'units.changeStatus' | t }}</button>
          <mat-menu #statusMenu="matMenu">
            @for (status of statuses; track status) {
              <button mat-menu-item type="button" [disabled]="status === o.unit.status" (click)="changeStatus(status)">{{ 'enums.unitStatus.' + status | t }}</button>
            }
          </mat-menu>
          <button *brCan="[p.fuelCapture, p.fuelManage]" mat-stroked-button type="button" (click)="fuel()"><mat-icon>local_gas_station</mat-icon>{{ 'fuel.new' | t }}</button>
          <a *brCan="p.maintenanceManage" mat-stroked-button [routerLink]="nav.to('mantenimiento/ordenes/nueva')" [queryParams]="{ unidad: o.unit.id }"><mat-icon>build</mat-icon>{{ 'maintenance.newOrder' | t }}</a>
          <button *brCan="p.fleetManage" mat-flat-button type="button" (click)="edit()"><mat-icon>edit</mat-icon>{{ 'common.edit' | t }}</button>
        </br-page-header>

        <div class="br-grid br-grid--2">
          <section class="br-card br-unit">
            @if (photo(); as photo) {
              <img class="br-unit__photo" [src]="photo" alt="" />
            }
            <div class="br-actions"><br-status enum="unitStatus" [value]="o.unit.status" /></div>
            <dl class="br-dl" style="margin-top: 12px">
              <div><dt>{{ 'fields.odometer' | t }}</dt><dd>{{ o.unit.currentOdometer | num: 0 }} km</dd></div>
              <div><dt>{{ 'fields.hourMeter' | t }}</dt><dd>{{ o.unit.currentHours | num: 1 }} h</dd></div>
              <div><dt>{{ 'units.metersUpdated' | t }}</dt><dd>{{ o.unit.metersUpdatedAt | dt }}</dd></div>
              <div><dt>{{ 'fields.brand' | t }}</dt><dd>{{ o.unit.brand ?? '—' }} {{ o.unit.model ?? '' }} {{ o.unit.year ?? '' }}</dd></div>
              <div><dt>{{ 'fields.capacity' | t }}</dt><dd>{{ o.unit.capacity === null ? '—' : (o.unit.capacity | num) + ' ' + (o.unit.capacityUnit ?? '') }}</dd></div>
              <div><dt>{{ 'fields.expectedEfficiency' | t }}</dt><dd>{{ o.unit.expectedEfficiency ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.defaultOperator' | t }}</dt><dd>{{ o.unit.defaultOperatorName ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.sctPermitNumber' | t }}</dt><dd>{{ o.unit.sctPermitType ?? '' }} {{ o.unit.sctPermitNumber ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.insurancePolicy' | t }}</dt><dd>{{ o.unit.insurerName ?? '' }} {{ o.unit.insurancePolicy ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.vehicleConfig' | t }}</dt><dd>{{ o.unit.vehicleConfig ?? '—' }}</dd></div>
              @if (o.unit.lastPositionAt) {
                <div>
                  <dt>{{ 'units.lastPosition' | t }}</dt>
                  <dd><a [href]="'https://www.google.com/maps?q=' + o.unit.lastLatitude + ',' + o.unit.lastLongitude" target="_blank" rel="noopener">{{ o.unit.lastLatitude }}, {{ o.unit.lastLongitude }}</a><br /><small class="cl-muted">{{ o.unit.lastPositionAt | dt }}</small></dd>
                </div>
              }
            </dl>
          </section>

          <section class="br-card">
            <div class="br-card__header">
              <h2>{{ 'units.documents' | t }}</h2>
              <a mat-button [routerLink]="nav.to('permisos/unidades')" [queryParams]="{ subjectId: o.unit.id }">{{ 'common.manage' | t }}</a>
            </div>
            <table class="br-table">
              <tbody>
                @for (doc of o.documents; track doc.documentTypeId) {
                  <tr>
                    <td>{{ doc.documentType }}<div class="br-sub">{{ doc.number }}</div></td>
                    <td>{{ doc.expiresOn | d }}</td>
                    <td><br-status enum="complianceStatus" [value]="doc.status" /></td>
                  </tr>
                } @empty {
                  <tr><td class="br-table-empty">{{ 'units.noDocuments' | t }}</td></tr>
                }
              </tbody>
            </table>
          </section>
        </div>

        <div class="br-kpis">
          @for (kpi of o.kpis; track kpi.label) {
            <br-kpi [label]="kpi.label" [value]="kpi.value" [format]="formatOf(kpi)" />
          }
        </div>

        <section class="br-card">
          <h2>{{ 'units.history' | t }}</h2>
          <ul class="br-timeline">
            @for (item of o.history; track $index) {
              <li>
                <mat-icon>{{ icons[item.kind] ?? 'history' }}</mat-icon>
                <div>
                  @if (item.link) {
                    <a class="cl-row-link" [routerLink]="nav.to(item.link)">{{ item.title }}</a>
                  } @else {
                    <strong>{{ item.title }}</strong>
                  }
                  @if (item.detail) {
                    <small>{{ item.detail }}</small>
                  }
                  <small>{{ item.at | dt }}{{ item.by ? ' · ' + item.by : '' }}</small>
                </div>
              </li>
            } @empty {
              <li class="cl-muted br-timeline__empty">{{ 'units.noHistory' | t }}</li>
            }
          </ul>
        </section>
      }
    </div>
  `,
  styles: `
    .br-unit__photo { width: 100%; max-height: 220px; object-fit: cover; border-radius: var(--cl-radius-sm, 8px); margin-bottom: 12px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnitDetailPage {
  private readonly api = inject(BrocalApi);
  private readonly injector = inject(Injector);
  private readonly i18n = inject(I18n);
  protected readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;
  protected readonly icons = HISTORY_ICONS;
  protected readonly statuses: UnitStatus[] = ['available', 'operating', 'workshop', 'outOfService'];

  readonly id = input.required<string>();
  private readonly reload = signal(0);

  protected readonly overview = apiResource({
    params: () => ({ id: this.id(), reload: this.reload() }),
    stream: ({ params }) => this.api.get<UnitOverview>(`fleet/units/${params.id}/overview`, { days: 180 }),
  });

  private readonly photoFile = apiResource({
    params: () => this.overview.value()?.unit.photoFileId ?? undefined,
    stream: ({ params }) => this.api.get<UploadedFile>(`files/${params}`),
  });

  protected readonly photo = computed(() => fileUrl(this.photoFile.value()?.url));

  protected formatOf(kpi: Kpi): 'money' | 'number' {
    return /costo|ingreso|cost|revenue/i.test(kpi.label) ? 'money' : 'number';
  }

  protected async meters(): Promise<void> {
    const unit = this.overview.value()?.unit;
    if (!unit) {
      return;
    }

    const ctx = actionContext(this.injector);
    const saved = await ctx.form(
      {
        title: this.i18n.t('units.recordMetersTitle', { code: unit.code }),
        fields: [
          { key: 'odometer', label: 'fields.odometer', type: 'number', min: 0, hint: 'units.currentOdometer' },
          { key: 'hours', label: 'fields.hourMeter', type: 'number', min: 0 },
          { key: 'recordedAt', label: 'units.recordedAt', type: 'datetime' },
          { key: 'isCorrection', label: 'units.isCorrection', type: 'checkbox', hint: 'units.correctionHint' },
          { key: 'notes', label: 'fields.notes', type: 'textarea' },
        ],
        save: (api, v) => api.post(`fleet/units/${unit.id}/meters`, { ...v, recordedAt: v['recordedAt'] ? new Date(v['recordedAt'] as string).toISOString() : null, isCorrection: !!v['isCorrection'] }),
        width: '560px',
      },
      { odometer: unit.currentOdometer, hours: unit.currentHours, recordedAt: nowLocalInput(), isCorrection: false },
    );
    if (saved) {
      this.reload.update((r) => r + 1);
    }
  }

  protected async changeStatus(status: UnitStatus): Promise<void> {
    const ctx = actionContext(this.injector);
    const reason = await ctx.dialogs.prompt(this.i18n.t('units.changeStatusTitle', { status: this.i18n.enum('unitStatus', status) }), this.i18n.t('fields.reason'), { required: false });
    if (reason === null && status === 'outOfService') {
      return;
    }

    this.api.post(`fleet/units/${this.id()}/status`, { status, reason }).subscribe(() => this.reload.update((r) => r + 1));
  }

  protected async fuel(): Promise<void> {
    const ctx = actionContext(this.injector);
    const unit = this.overview.value()?.unit;
    if (await ctx.form(FUEL_DIALOG, { ...FUEL_DIALOG.defaults!(), unitId: unit?.id, driverId: unit?.defaultOperatorId, odometer: unit?.currentOdometer })) {
      this.reload.update((r) => r + 1);
    }
  }

  protected async edit(): Promise<void> {
    const ctx = actionContext(this.injector);
    const unit = this.overview.value()?.unit;
    if (!unit) {
      return;
    }

    const saved = await ctx.form({ title: `${unit.code} · ${unit.name}`, fields: UNITS.fields, save: (api, v) => api.put(`fleet/units/${unit.id}`, v), width: '940px' }, unit as unknown as Record<string, unknown>);
    if (saved) {
      this.reload.update((r) => r + 1);
    }
  }
}
