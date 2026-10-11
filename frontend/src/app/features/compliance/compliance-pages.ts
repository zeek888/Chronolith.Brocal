import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, saveFile } from '../../core/api';
import { DatePipe } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { ComplianceMatrix, ComplianceStatus, DocumentSubject, Lookup } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { apiResource } from '../../core/resource';
import { Dialogs } from '../../shared/dialogs';
import { LookupSelect } from '../../shared/lookup-select';
import { PageHeader } from '../../shared/ui';

/** Semáforo de cumplimiento: people or units against the documents required (all mandatory or of a contract). */
@Component({
  selector: 'br-compliance-matrix',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TPipe, EnumPipe, DatePipe, LookupSelect, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="(subject() === 'unit' ? 'nav.unitMatrix' : 'nav.personnelMatrix') | t" icon="grid_on" [subtitle]="'compliance.matrixHint' | t">
        <a mat-stroked-button [routerLink]="nav.to(subject() === 'unit' ? 'permisos/unidades' : 'personal/documentos')"><mat-icon>list</mat-icon>{{ 'compliance.documents' | t }}</a>
        <a mat-stroked-button [routerLink]="nav.to('personal/paquetes')"><mat-icon>folder_zip</mat-icon>{{ 'nav.packages' | t }}</a>
      </br-page-header>
      <div class="cl-toolbar-row br-filters">
        <br-lookup class="br-filter--lookup" catalog="compliance/requirements" [label]="'compliance.requirement' | t" (selected)="requirement.set(idOf($event))" [hint]="'compliance.requirementHint' | t" />
        <span class="cl-spacer"></span>
        @if (matrix.value(); as m) {
          @for (status of statuses; track status) {
            <span class="cl-chip" [class]="'cl-chip cl-chip--' + tone(status)">{{ status | enum: 'complianceStatus' }}: {{ m.totals[status] ?? 0 }}</span>
          }
        }
      </div>
      @if (matrix.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (matrix.value(); as m) {
        <div class="cl-table-wrapper">
          <table class="br-table br-matrix">
            <thead>
              <tr>
                <th>{{ (subject() === 'unit' ? 'fields.unit' : 'fields.employee') | t }}</th>
                @for (type of m.documentTypes; track type.id) {
                  <th class="br-cell" [matTooltip]="type.name">{{ type.code }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of m.rows; track row.subjectId) {
                <tr>
                  <td>
                    <a class="cl-row-link" [routerLink]="nav.to(subject() === 'unit' ? 'flota/unidades' : 'personal/empleados', row.subjectId)">{{ row.code }}</a> {{ row.name }}
                    <div class="br-sub">{{ row.detail }}</div>
                  </td>
                  @for (cell of row.cells; track cell.documentTypeId) {
                    <td class="br-cell" [matTooltip]="(cell.status | enum: 'complianceStatus') + (cell.expiresOn ? ' · ' + (cell.expiresOn | d) : '')">
                      <span class="br-dot" [class]="'br-dot br-dot--' + cell.status"></span>
                    </td>
                  }
                </tr>
              } @empty {
                <tr><td [attr.colspan]="m.documentTypes.length + 1" class="br-table-empty">{{ 'common.noRecords' | t }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComplianceMatrixPage {
  private readonly api = inject(BrocalApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly nav = inject(BrocalNav);
  protected readonly statuses: ComplianceStatus[] = ['valid', 'expiringSoon', 'expired', 'missing'];

  readonly subjectInput = input<DocumentSubject | null>(null, { alias: 'subject' });
  protected readonly subject = computed<DocumentSubject>(() => this.subjectInput() ?? (this.route.snapshot.data['subject'] as DocumentSubject) ?? 'employee');
  protected readonly requirement = signal<string | null>(null);

  protected readonly matrix = apiResource({
    params: () => ({ subject: this.subject(), requirementId: this.requirement() }),
    stream: ({ params }) => this.api.get<ComplianceMatrix>('compliance/matrix', params),
  });

  protected idOf(selected: Lookup | Lookup[] | null): string | null {
    return Array.isArray(selected) ? null : (selected?.id ?? null);
  }

  protected tone(status: ComplianceStatus): string {
    return status === 'valid' ? 'success' : status === 'expiringSoon' ? 'warning' : 'error';
  }
}

/** Paquete documental por contrato o cliente: cover page plus the files of the selected people and units, in a ZIP. */
@Component({
  selector: 'br-packages',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TPipe, LookupSelect, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.packages' | t" icon="folder_zip" [subtitle]="'compliance.packageHint' | t" />
      <section class="br-card">
        <div class="cl-form-grid">
          <br-lookup [formControl]="requirement" catalog="compliance/requirements" [label]="'compliance.requirement' | t" [hint]="'compliance.packageRequirementHint' | t" />
          <mat-form-field>
            <mat-label>{{ 'compliance.packageTitle' | t }}</mat-label>
            <input matInput [formControl]="title" />
          </mat-form-field>
          <br-lookup class="cl-full-row" [formControl]="employees" catalog="personnel/employees" [label]="'fields.personnel' | t" [multiple]="true" />
          <br-lookup class="cl-full-row" [formControl]="units" catalog="fleet/units" [label]="'fields.units' | t" [multiple]="true" />
        </div>
        @if (busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <div class="br-actions" style="margin-top: 12px">
          <span class="cl-spacer"></span>
          <button mat-flat-button type="button" (click)="generate()" [disabled]="busy() || (employees.value.length === 0 && units.value.length === 0)">
            <mat-icon>download</mat-icon>{{ 'compliance.generate' | t }}
          </button>
        </div>
      </section>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PackagesPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);

  protected readonly requirement = new FormControl<string | null>(null);
  protected readonly title = new FormControl<string | null>(null);
  protected readonly employees = new FormControl<string[]>([], { nonNullable: true });
  protected readonly units = new FormControl<string[]>([], { nonNullable: true });
  protected readonly busy = signal(false);

  protected async generate(): Promise<void> {
    this.busy.set(true);
    try {
      const file = await firstValueFrom(
        this.api.download('compliance/package', undefined, { requirementId: this.requirement.value, employeeIds: this.employees.value, unitIds: this.units.value, title: this.title.value }, 'paquete.zip'),
      );
      saveFile(file);
      this.dialogs.success(this.i18n.t('compliance.packageReady'));
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
