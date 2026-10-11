import { ChangeDetectionStrategy, Component, Injector, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { fileUrl } from '../../core/config';
import { DatePipe, MoneyPipe, fromLocalInput, toLocalInput, today } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { AttendanceRow, AttendanceStatus, ComplianceDocument, Employee, Lookup, PagedResult, UploadedFile } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { actionContext } from '../../shared/crud/list-page';
import { Dialogs } from '../../shared/dialogs';
import { LookupSelect } from '../../shared/lookup-select';
import { PageHeader, StatusChip } from '../../shared/ui';
import { EMPLOYEES } from '../catalogs/catalog-configs';
import { documentDialog } from '../compliance/compliance.config';

/** Expediente del personal: data, license, documents with expiry traffic lights and courses. */
@Component({
  selector: 'br-employee-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, EnumPipe, DatePipe, MoneyPipe, CanDirective, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      @if (employee.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (employee.value(); as e) {
        <br-page-header [title]="e.name" icon="badge" [subtitle]="e.code + (e.position ? ' · ' + e.position : '')">
          <a mat-button [routerLink]="nav.to('personal/empleados')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
          <button *brCan="p.personnelManage" mat-stroked-button type="button" (click)="addDocument()"><mat-icon>note_add</mat-icon>{{ 'compliance.newDocument' | t }}</button>
          <button *brCan="p.personnelManage" mat-flat-button type="button" (click)="edit()"><mat-icon>edit</mat-icon>{{ 'common.edit' | t }}</button>
        </br-page-header>

        <div class="br-grid br-grid--2">
          <section class="br-card">
            @if (photo(); as photo) {
              <img class="br-person__photo" [src]="photo" alt="" />
            }
            <dl class="br-dl">
              <div><dt>{{ 'fields.category' | t }}</dt><dd>{{ e.category | enum: 'employeeCategory' }}</dd></div>
              <div><dt>{{ 'fields.hiredOn' | t }}</dt><dd>{{ e.hiredOn | d }}</dd></div>
              <div><dt>{{ 'fields.curp' | t }}</dt><dd>{{ e.curp ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.rfc' | t }}</dt><dd>{{ e.rfc ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.nss' | t }}</dt><dd>{{ e.nss ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.phone' | t }}</dt><dd>{{ e.phone ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.email' | t }}</dt><dd>{{ e.email ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.emergencyContact' | t }}</dt><dd>{{ e.emergencyContact ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.bloodType' | t }}</dt><dd>{{ e.bloodType ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.licenseNumber' | t }}</dt><dd>{{ e.licenseType ?? '' }} {{ e.licenseNumber ?? '—' }}</dd></div>
              <div><dt>{{ 'fields.licenseExpiresOn' | t }}</dt><dd>{{ e.licenseExpiresOn | d }}</dd></div>
              <div><dt>{{ 'fields.hourlyCost' | t }}</dt><dd>{{ e.hourlyCost | money }}</dd></div>
            </dl>
          </section>
          <section class="br-card">
            <div class="br-card__header">
              <h2>{{ 'employees.file' | t }}</h2>
              <a mat-button [routerLink]="nav.to('personal/matriz')">{{ 'compliance.matrix' | t }}</a>
            </div>
            <table class="br-table">
              <tbody>
                @for (doc of documents.value()?.items ?? []; track doc.id) {
                  <tr>
                    <td>
                      @if (doc.fileUrl) {
                        <a [href]="url(doc.fileUrl)" target="_blank" rel="noopener">{{ doc.documentTypeName }}</a>
                      } @else {
                        {{ doc.documentTypeName }}
                      }
                      <div class="br-sub">{{ doc.issuer }}{{ doc.hours ? ' · ' + doc.hours + ' h' : '' }}</div>
                    </td>
                    <td>{{ doc.expiresOn | d }}</td>
                    <td><br-status enum="complianceStatus" [value]="doc.status" /></td>
                  </tr>
                } @empty {
                  <tr><td class="br-table-empty">{{ 'employees.noDocuments' | t }}</td></tr>
                }
              </tbody>
            </table>
          </section>
        </div>
        @if (e.notes) {
          <section class="br-card"><h2>{{ 'fields.notes' | t }}</h2><p class="br-pre">{{ e.notes }}</p></section>
        }
      }
    </div>
  `,
  styles: `
    .br-person__photo { width: 120px; height: 120px; object-fit: cover; border-radius: 50%; margin-bottom: 12px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmployeeDetailPage {
  private readonly api = inject(BrocalApi);
  private readonly injector = inject(Injector);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;

  readonly id = input.required<string>();
  private readonly reload = signal(0);

  protected readonly employee = apiResource({
    params: () => ({ id: this.id(), reload: this.reload() }),
    stream: ({ params }) => this.api.get<Employee>(`personnel/employees/${params.id}`),
  });

  protected readonly documents = apiResource({
    params: () => ({ id: this.id(), reload: this.reload() }),
    stream: ({ params }) => this.api.get<PagedResult<ComplianceDocument>>('compliance/documents', { subject: 'employee', subjectId: params.id, pageSize: 100 }),
  });

  private readonly photoFile = apiResource({
    params: () => this.employee.value()?.photoFileId ?? undefined,
    stream: ({ params }) => this.api.get<UploadedFile>(`files/${params}`),
  });

  protected readonly photo = computed(() => fileUrl(this.photoFile.value()?.url));

  protected url(relative: string): string {
    return fileUrl(relative) ?? '';
  }

  protected async addDocument(): Promise<void> {
    if (await actionContext(this.injector).form(documentDialog('employee'), { subjectId: this.id() })) {
      this.reload.update((r) => r + 1);
    }
  }

  protected async edit(): Promise<void> {
    const e = this.employee.value();
    if (!e) {
      return;
    }

    const saved = await actionContext(this.injector).form({ title: e.name, fields: EMPLOYEES.fields, save: (api, v) => api.put(`personnel/employees/${e.id}`, v), width: '900px' }, e as unknown as Record<string, unknown>);
    if (saved) {
      this.reload.update((r) => r + 1);
    }
  }
}

const ATTENDANCE: AttendanceStatus[] = ['present', 'absent', 'rest', 'sickLeave', 'vacation', 'travel', 'training'];

interface SheetRow extends AttendanceRow {
  checkIn: string;
  checkOut: string;
  dirty: boolean;
}

/** Lista de asistencia del día por cuadrilla (input for the payroll and for the job costing of tickets). */
@Component({
  selector: 'br-attendance',
  imports: [FormsModule, RouterLink, MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TPipe, LookupSelect, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.attendance' | t" icon="how_to_reg" [subtitle]="'attendance.hint' | t">
        <a mat-stroked-button [routerLink]="nav.to('cuadrillas/prenomina')"><mat-icon>payments</mat-icon>{{ 'nav.payroll' | t }}</a>
        <button mat-flat-button type="button" (click)="save()" [disabled]="busy() || !dirty()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
      </br-page-header>
      <div class="cl-toolbar-row br-filters">
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.date' | t }}</mat-label>
          <input matInput [matDatepicker]="picker" [ngModel]="date()" (ngModelChange)="date.set($event)" />
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-datepicker #picker />
        </mat-form-field>
        <br-lookup class="br-filter--lookup" catalog="crews" [label]="'fields.crew' | t" (selected)="crew.set(idOf($event))" />
        <span class="cl-spacer"></span>
        <button mat-stroked-button type="button" (click)="markAll('present')"><mat-icon>done_all</mat-icon>{{ 'attendance.allPresent' | t }}</button>
      </div>
      @if (sheet.isLoading() || busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <div class="cl-table-wrapper">
        <table class="br-table">
          <thead>
            <tr>
              <th>{{ 'fields.employee' | t }}</th>
              <th>{{ 'fields.status' | t }}</th>
              <th>{{ 'attendance.checkIn' | t }}</th>
              <th>{{ 'attendance.checkOut' | t }}</th>
              <th class="br-end">{{ 'payroll.hours' | t }}</th>
              <th class="br-end">{{ 'payroll.overtime' | t }}</th>
              <th class="br-wide">{{ 'fields.notes' | t }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.employeeId; let i = $index) {
              <tr>
                <td>{{ row.employeeName }}<div class="br-sub">{{ row.employeeCode }} {{ row.position ? '· ' + row.position : '' }}</div></td>
                <td class="br-input-cell">
                  <mat-form-field subscriptSizing="dynamic">
                    <mat-select [ngModel]="row.status" (ngModelChange)="update(i, { status: $event })" [placeholder]="'attendance.notCaptured' | t">
                      @for (s of statuses; track s) {
                        <mat-option [value]="s">{{ 'enums.attendanceStatus.' + s | t }}</mat-option>
                      }
                    </mat-select>
                  </mat-form-field>
                </td>
                <td class="br-input-cell"><input class="br-time" type="time" [ngModel]="row.checkIn" (ngModelChange)="update(i, { checkIn: $event })" /></td>
                <td class="br-input-cell"><input class="br-time" type="time" [ngModel]="row.checkOut" (ngModelChange)="update(i, { checkOut: $event })" /></td>
                <td class="br-end br-input-cell"><input class="br-num" type="number" min="0" max="24" [ngModel]="row.hours" (ngModelChange)="update(i, { hours: $event })" /></td>
                <td class="br-end br-input-cell"><input class="br-num" type="number" min="0" max="24" [ngModel]="row.overtimeHours" (ngModelChange)="update(i, { overtimeHours: $event })" /></td>
                <td class="br-wide br-input-cell"><input class="br-text" [ngModel]="row.notes" (ngModelChange)="update(i, { notes: $event })" /></td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="br-table-empty">{{ 'attendance.empty' | t }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: `
    .br-time, .br-num, .br-text { box-sizing: border-box; padding: 8px; border-radius: 6px; border: 1px solid var(--mat-sys-outline-variant); background: var(--mat-sys-surface-container-lowest); color: var(--mat-sys-on-surface); font: var(--mat-sys-body-medium); }
    .br-num { width: 72px; text-align: right; }
    .br-text { width: 100%; min-width: 140px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttendancePage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  protected readonly nav = inject(BrocalNav);
  protected readonly session = inject(BrocalSession);
  protected readonly statuses = ATTENDANCE;

  protected readonly date = signal(today());
  protected readonly crew = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly rows = signal<SheetRow[]>([]);
  protected readonly dirty = computed(() => this.rows().some((r) => r.dirty));

  protected readonly sheet = apiResource({
    params: () => ({ date: this.date(), crewId: this.crew() }),
    stream: ({ params }) => this.api.get<AttendanceRow[]>('attendance', params),
  });

  constructor() {
    // The sheet comes pre-filled with the people of the crew (and the orders of the day).
    effect(() => {
      const rows = this.sheet.value();
      if (rows) {
        this.rows.set(rows.map((r) => ({ ...r, checkIn: toLocalInput(r.checkInAt).slice(11), checkOut: toLocalInput(r.checkOutAt).slice(11), dirty: false })));
      }
    });
  }

  protected update(index: number, change: Partial<SheetRow>): void {
    this.rows.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...change, dirty: true, hours: change.status === 'present' && !r.hours ? 8 : (change.hours ?? r.hours) } : r)));
  }

  protected markAll(status: AttendanceStatus): void {
    this.rows.update((rows) => rows.map((r) => (r.status ? r : { ...r, status, hours: status === 'present' ? 8 : 0, dirty: true })));
  }

  protected idOf(selected: Lookup | Lookup[] | null): string | null {
    return Array.isArray(selected) ? null : (selected?.id ?? null);
  }

  protected async save(): Promise<void> {
    const changed = this.rows().filter((r) => r.dirty && r.status);
    if (changed.length === 0) {
      return;
    }

    this.busy.set(true);
    try {
      const at = (time: string) => (time ? fromLocalInput(`${this.date()}T${time}`) : null);
      const saved = await firstValueFrom(
        this.api.post<number>('attendance', {
          records: changed.map((r) => ({
            employeeId: r.employeeId,
            date: this.date(),
            status: r.status,
            crewId: r.crewId ?? this.crew(),
            workOrderId: r.workOrderId,
            ticketId: r.ticketId,
            siteId: r.siteId,
            checkInAt: at(r.checkIn),
            checkOutAt: at(r.checkOut),
            hours: r.hours === null || r.hours === undefined ? null : Number(r.hours),
            overtimeHours: r.overtimeHours === null || r.overtimeHours === undefined ? null : Number(r.overtimeHours),
            location: null,
            notes: r.notes || null,
          })),
        }),
      );
      this.dialogs.success(this.i18n.t('attendance.saved', { n: saved }));
      this.sheet.reload();
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
