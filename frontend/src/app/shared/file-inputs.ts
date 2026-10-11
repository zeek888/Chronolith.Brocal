import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../core/api';
import { TPipe } from '../core/i18n';
import { compressImage } from '../core/images';
import { FileCategory, UploadedFile } from '../core/models';

export interface PickedPhoto {
  blob: Blob;
  fileName: string;
  previewUrl: string;
  takenAt: string;
}

/** Takes photos with the camera of the device (or picks them from the gallery), already compressed. */
@Component({
  selector: 'br-photo-picker',
  imports: [MatButtonModule, MatIconModule, TPipe],
  template: `
    <input #input type="file" accept="image/*" [attr.capture]="camera() ? 'environment' : null" [multiple]="multiple()" hidden (change)="picked(input)" />
    <button mat-stroked-button type="button" (click)="input.click()" [disabled]="busy()">
      <mat-icon>{{ camera() ? 'photo_camera' : 'add_photo_alternate' }}</mat-icon>
      {{ (label() ?? (camera() ? 'common.takePhoto' : 'common.addPhoto')) | t }}
    </button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PhotoPicker {
  readonly camera = input(true);
  readonly multiple = input(true);
  readonly label = input<string | null>(null);
  readonly photos = output<PickedPhoto[]>();
  protected readonly busy = signal(false);

  protected async picked(input: HTMLInputElement): Promise<void> {
    const files = [...(input.files ?? [])];
    input.value = '';
    if (files.length === 0) {
      return;
    }

    this.busy.set(true);
    try {
      const result: PickedPhoto[] = [];
      for (const file of files) {
        const blob = await compressImage(file);
        const name = file.name.replace(/\.[^.]+$/, '') + (blob.type === 'image/jpeg' ? '.jpg' : file.name.slice(file.name.lastIndexOf('.')));
        result.push({ blob, fileName: name, previewUrl: URL.createObjectURL(blob), takenAt: new Date(file.lastModified || Date.now()).toISOString() });
      }

      this.photos.emit(result);
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Uploads one document (permit, certificate, receipt, logo) right away and reports the stored file;
 * shows its name with a link once uploaded.
 */
@Component({
  selector: 'br-file-upload',
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TPipe],
  template: `
    <input #input type="file" [accept]="accept()" hidden (change)="picked(input)" />
    <div class="br-file-upload">
      <button mat-stroked-button type="button" (click)="input.click()" [disabled]="busy()">
        @if (busy()) {
          <mat-spinner diameter="18" />
        } @else {
          <mat-icon>upload_file</mat-icon>
        }
        {{ (label() ?? 'common.attachFile') | t }}
      </button>
      @if (fileName() ?? (cleared() ? null : currentName()); as name) {
        @if (url() ?? (fileName() || cleared() ? null : currentUrl()); as url) {
          <a [href]="url" target="_blank" rel="noopener" class="br-file-upload__name"><mat-icon>attach_file</mat-icon>{{ name }}</a>
        } @else {
          <span class="br-file-upload__name"><mat-icon>attach_file</mat-icon>{{ name }}</span>
        }
        <button mat-icon-button type="button" (click)="clear()" [attr.aria-label]="'common.remove' | t"><mat-icon>close</mat-icon></button>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FileUpload {
  private readonly api = inject(BrocalApi);
  private readonly input = viewChild<ElementRef<HTMLInputElement>>('input');

  readonly category = input<FileCategory>('document');
  readonly accept = input('application/pdf,image/*');
  readonly label = input<string | null>(null);
  /** Already stored file (editing). */
  readonly currentName = input<string | null>(null);
  readonly currentUrl = input<string | null>(null);

  readonly uploaded = output<UploadedFile | null>();

  protected readonly busy = signal(false);
  protected readonly fileName = signal<string | null>(null);
  protected readonly url = signal<string | null>(null);
  protected readonly cleared = signal(false);

  protected async picked(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    this.busy.set(true);
    try {
      const content = file.type.startsWith('image/') ? await compressImage(file, 2200, 0.85) : file;
      const stored = await firstValueFrom(this.api.upload(content, file.name, this.category()));
      this.fileName.set(file.name);
      this.url.set(null);
      this.uploaded.emit(stored);
    } catch {
      // The HTTP error is shown by the error interceptor of the host.
    } finally {
      this.busy.set(false);
    }
  }

  protected clear(): void {
    this.fileName.set(null);
    this.url.set(null);
    this.cleared.set(true);
    const element = this.input()?.nativeElement;
    if (element) {
      element.value = '';
    }

    this.uploaded.emit(null);
  }
}
