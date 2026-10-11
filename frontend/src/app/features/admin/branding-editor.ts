import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { fileUrl } from '../../core/config';
import { TPipe } from '../../core/i18n';
import { Branding, ColorSchemePreference, CornerStyle, ThemePalette, UploadedFile } from '../../core/models';
import { BrocalTheme, CHRONOLITH_PALETTE } from '../../core/theme/brocal-theme';
import { FileUpload } from '../../shared/file-inputs';

const ROLES: (keyof ThemePalette)[] = ['primary', 'secondary', 'tertiary', 'neutral', 'error', 'success', 'warning', 'info'];
const FONTS = ['Inter Variable', 'Montserrat Variable', 'Roboto', 'Segoe UI', 'Open Sans', 'Source Sans 3', 'Lato'];

/** Payload of PUT company/branding and platform/tenants/{id}/branding. */
export type BrandingPayload = Omit<Branding, 'logoUrl' | 'logoDarkUrl'>;

/**
 * Look of a company: follow the style configured in ChronoLith or use its own palette (8 seed colors, the
 * same model as the ChronoLith themes), logos for light and dark mode, font, corners, density and the footer
 * of its documents. With `livePreview` the whole module shows the changes while editing.
 */
@Component({
  selector: 'br-branding-editor',
  imports: [ReactiveFormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatSlideToggleModule, TPipe, FileUpload],
  template: `
    <form [formGroup]="form" class="br-branding">
      <mat-slide-toggle formControlName="useChronoLithTheme">{{ 'branding.useChronoLith' | t }}</mat-slide-toggle>
      <p class="cl-muted">{{ (form.controls.useChronoLithTheme.value ? 'branding.useChronoLithHint' : 'branding.ownHint') | t }}</p>

      <div class="cl-form-grid">
        <mat-form-field>
          <mat-label>{{ 'branding.appName' | t }}</mat-label>
          <input matInput formControlName="appName" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'branding.tagline' | t }}</mat-label>
          <input matInput formControlName="tagline" />
        </mat-form-field>
      </div>

      @if (!form.controls.useChronoLithTheme.value) {
        <h3 class="br-form-section">{{ 'branding.palette' | t }}</h3>
        <div class="br-palette" formGroupName="palette">
          @for (role of roles; track role) {
            <label class="br-palette__item">
              <input type="color" [formControlName]="role" />
              <span>
                <strong>{{ 'branding.roles.' + role | t }}</strong>
                <small class="cl-muted">{{ form.controls.palette.controls[role].value }}</small>
              </span>
            </label>
          }
        </div>
        <button mat-button type="button" (click)="resetPalette()"><mat-icon>restart_alt</mat-icon>{{ 'branding.resetPalette' | t }}</button>

        <h3 class="br-form-section">{{ 'branding.style' | t }}</h3>
        <div class="cl-form-grid">
          <mat-form-field>
            <mat-label>{{ 'branding.font' | t }}</mat-label>
            <mat-select formControlName="fontFamily">
              @for (font of fonts; track font) {
                <mat-option [value]="font"><span [style.font-family]="font">{{ font }}</span></mat-option>
              }
            </mat-select>
          </mat-form-field>
          <div class="br-field-inline">
            <span class="cl-muted">{{ 'branding.corners' | t }}</span>
            <mat-button-toggle-group formControlName="corners">
              <mat-button-toggle value="rounded">{{ 'branding.cornerStyles.rounded' | t }}</mat-button-toggle>
              <mat-button-toggle value="soft">{{ 'branding.cornerStyles.soft' | t }}</mat-button-toggle>
              <mat-button-toggle value="square">{{ 'branding.cornerStyles.square' | t }}</mat-button-toggle>
            </mat-button-toggle-group>
          </div>
        </div>
      }

      <div class="cl-form-grid">
        <mat-form-field>
          <mat-label>{{ 'branding.density' | t }}</mat-label>
          <mat-select formControlName="density">
            <mat-option [value]="0">{{ 'branding.densities.0' | t }}</mat-option>
            <mat-option [value]="-1">{{ 'branding.densities.1' | t }}</mat-option>
            <mat-option [value]="-2">{{ 'branding.densities.2' | t }}</mat-option>
            <mat-option [value]="-3">{{ 'branding.densities.3' | t }}</mat-option>
          </mat-select>
          <mat-hint>{{ 'branding.standaloneOnly' | t }}</mat-hint>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'branding.colorScheme' | t }}</mat-label>
          <mat-select formControlName="defaultColorScheme">
            <mat-option value="system">{{ 'branding.schemes.system' | t }}</mat-option>
            <mat-option value="light">{{ 'branding.schemes.light' | t }}</mat-option>
            <mat-option value="dark">{{ 'branding.schemes.dark' | t }}</mat-option>
          </mat-select>
          <mat-hint>{{ 'branding.standaloneOnly' | t }}</mat-hint>
        </mat-form-field>
      </div>

      <h3 class="br-form-section">{{ 'branding.logos' | t }}</h3>
      <div class="br-logos">
        <div class="br-logo-box">
          @if (logoPreview(); as url) {
            <img [src]="url" alt="" />
          } @else {
            <mat-icon>image</mat-icon>
          }
          <br-file-upload category="logo" accept="image/png,image/svg+xml,image/jpeg,image/webp" [label]="'branding.logoLight'" (uploaded)="logoUploaded('logoFileId', $event)" />
        </div>
        <div class="br-logo-box br-logo-box--dark">
          @if (logoDarkPreview(); as url) {
            <img [src]="url" alt="" />
          } @else {
            <mat-icon>image</mat-icon>
          }
          <br-file-upload category="logo" accept="image/png,image/svg+xml,image/jpeg,image/webp" [label]="'branding.logoDark'" (uploaded)="logoUploaded('logoDarkFileId', $event)" />
        </div>
      </div>

      <mat-form-field class="br-full">
        <mat-label>{{ 'branding.documentFooter' | t }}</mat-label>
        <textarea matInput rows="2" formControlName="documentFooter"></textarea>
        <mat-hint>{{ 'branding.documentFooterHint' | t }}</mat-hint>
      </mat-form-field>

      <div class="br-actions">
        @if (livePreview()) {
          <span class="cl-muted"><mat-icon class="br-inline-icon">visibility</mat-icon>{{ 'branding.previewing' | t }}</span>
        }
        <span class="cl-spacer"></span>
        <button mat-flat-button type="button" (click)="emit()" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
      </div>
    </form>
  `,
  styles: `
    .br-branding { display: flex; flex-direction: column; gap: 12px; }
    .br-palette { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 10px; }
    .br-palette__item { display: flex; align-items: center; gap: 10px; padding: 8px; border-radius: var(--cl-radius-sm, 8px); border: 1px solid var(--mat-sys-outline-variant); cursor: pointer; }
    .br-palette__item input { width: 40px; height: 40px; border: none; padding: 0; background: none; cursor: pointer; }
    .br-palette__item span { display: flex; flex-direction: column; }
    .br-field-inline { display: flex; flex-direction: column; gap: 6px; }
    .br-logos { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px; }
    .br-logo-box { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 16px; border-radius: var(--cl-radius-md, 12px); border: 1px dashed var(--mat-sys-outline-variant); background: #fff; }
    .br-logo-box--dark { background: #1b1f24; }
    .br-logo-box img { max-height: 64px; max-width: 220px; object-fit: contain; }
    .br-logo-box mat-icon { font-size: 40px; width: 40px; height: 40px; color: #9aa4ae; }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class BrandingEditor implements OnInit, OnDestroy {
  private readonly theme = inject(BrocalTheme);

  readonly branding = input.required<Branding>();
  /** Re-themes Brocal live while editing (the company of the user). */
  readonly livePreview = input(false);
  readonly busy = input(false);
  readonly save = output<BrandingPayload>();

  protected readonly roles = ROLES;
  protected readonly fonts = FONTS;
  protected readonly logoPreview = signal<string | null>(null);
  protected readonly logoDarkPreview = signal<string | null>(null);

  protected readonly form = new FormGroup({
    useChronoLithTheme: new FormControl(true, { nonNullable: true }),
    appName: new FormControl('', { nonNullable: true }),
    tagline: new FormControl<string | null>(null),
    palette: new FormGroup(Object.fromEntries(ROLES.map((r) => [r, new FormControl(CHRONOLITH_PALETTE[r], { nonNullable: true })])) as Record<keyof ThemePalette, FormControl<string>>),
    logoFileId: new FormControl<string | null>(null),
    logoDarkFileId: new FormControl<string | null>(null),
    density: new FormControl(0, { nonNullable: true }),
    defaultColorScheme: new FormControl<ColorSchemePreference>('system', { nonNullable: true }),
    fontFamily: new FormControl('Inter Variable', { nonNullable: true }),
    corners: new FormControl<CornerStyle>('rounded', { nonNullable: true }),
    documentFooter: new FormControl<string | null>(null),
  });

  ngOnInit(): void {
    const b = this.branding();
    this.form.patchValue({ ...b, palette: { ...CHRONOLITH_PALETTE, ...b.palette }, fontFamily: b.fontFamily || 'Inter Variable' });
    this.logoPreview.set(fileUrl(b.logoUrl));
    this.logoDarkPreview.set(fileUrl(b.logoDarkUrl));
    if (this.livePreview()) {
      this.form.valueChanges.subscribe(() => this.theme.preview.set({ ...this.branding(), ...this.payload(), logoUrl: this.branding().logoUrl, logoDarkUrl: this.branding().logoDarkUrl }));
    }
  }

  ngOnDestroy(): void {
    if (this.livePreview()) {
      this.theme.preview.set(null);
    }
  }

  protected resetPalette(): void {
    this.form.controls.palette.setValue({ ...CHRONOLITH_PALETTE });
  }

  protected logoUploaded(key: 'logoFileId' | 'logoDarkFileId', file: UploadedFile | null): void {
    this.form.controls[key].setValue(file?.id ?? null);
    const url = file ? fileUrl(file.url) : null;
    if (key === 'logoFileId') {
      this.logoPreview.set(url);
    } else {
      this.logoDarkPreview.set(url);
    }
  }

  private payload(): BrandingPayload {
    const v = this.form.getRawValue();
    return {
      useChronoLithTheme: v.useChronoLithTheme,
      palette: v.palette as ThemePalette,
      appName: v.appName.trim() || 'Brocal',
      tagline: v.tagline?.trim() || null,
      logoFileId: v.logoFileId,
      logoDarkFileId: v.logoDarkFileId,
      density: v.density,
      defaultColorScheme: v.defaultColorScheme,
      fontFamily: v.fontFamily,
      corners: v.corners,
      documentFooter: v.documentFooter?.trim() || null,
    };
  }

  protected emit(): void {
    this.save.emit(this.payload());
  }
}
