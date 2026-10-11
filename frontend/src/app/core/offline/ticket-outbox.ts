import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, TenantHeader } from '../api';
import { GeoPoint, SaveTicketResult, TicketData, TicketLineInput, UnitMeterInput } from '../models';
import { OUTBOX_STORE, idbAll, idbDelete, idbPut } from './idb';

export interface PendingPhoto {
  blob: Blob;
  fileName: string;
  caption: string | null;
  takenAt: string;
  location: GeoPoint | null;
}

export interface PendingSignature {
  blob: Blob;
  signerName: string;
  signerPosition: string | null;
  signedAt: string;
  location: GeoPoint | null;
}

/** A ticket captured without signal: data, photos and signature wait on the device until they can be sent. */
export interface PendingTicket {
  clientId: string;
  tenant: string;
  body: { clientId: string; data: TicketData; lines: TicketLineInput[]; meters: UnitMeterInput[] };
  photos: PendingPhoto[];
  signature: PendingSignature | null;
  /** Progress of the synchronization (it resumes where it stopped). */
  ticketId: string | null;
  folio: string | null;
  uploadedPhotos: number;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  label: string;
}

/**
 * Offline queue of field captures. The ticket carries the id generated on the device (`clientId`), so the
 * API never duplicates it even if the connection drops in the middle of a synchronization.
 */
@Injectable({ providedIn: 'root' })
export class TicketOutbox {
  private readonly api = inject(BrocalApi);
  private readonly tenant = inject(TenantHeader);
  private readonly items = signal<PendingTicket[]>([]);

  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly syncing = signal(false);
  readonly pending = computed(() => this.items().filter((i) => i.tenant === this.tenant.slug()));
  readonly count = computed(() => this.pending().length);

  constructor() {
    const onOnline = () => {
      this.online.set(true);
      void this.sync();
    };
    const onOffline = () => this.online.set(false);
    window.addEventListener('online', onOnline);
    window.addEventListener('offline', onOffline);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('online', onOnline);
      window.removeEventListener('offline', onOffline);
    });
    void this.reload();
  }

  async enqueue(item: Omit<PendingTicket, 'ticketId' | 'folio' | 'uploadedPhotos' | 'attempts' | 'lastError' | 'createdAt'>): Promise<void> {
    await idbPut<PendingTicket>(OUTBOX_STORE, { ...item, ticketId: null, folio: null, uploadedPhotos: 0, attempts: 0, lastError: null, createdAt: new Date().toISOString() });
    await this.reload();
  }

  async discard(clientId: string): Promise<void> {
    await idbDelete(OUTBOX_STORE, clientId);
    await this.reload();
  }

  /** Sends the captures of the selected company; stops at the first connection error. */
  async sync(): Promise<number> {
    if (this.syncing() || !this.online()) {
      return 0;
    }

    this.syncing.set(true);
    let sent = 0;
    try {
      for (const item of this.pending()) {
        try {
          await this.process(item);
          await idbDelete(OUTBOX_STORE, item.clientId);
          sent++;
        } catch (error) {
          if (error instanceof HttpErrorResponse && error.status === 0) {
            this.online.set(false);
            break;
          }

          item.attempts++;
          item.lastError = error instanceof HttpErrorResponse ? ((error.error as { detail?: string } | null)?.detail ?? error.message) : String(error);
          await idbPut(OUTBOX_STORE, item);
        }
      }
    } finally {
      this.syncing.set(false);
      await this.reload();
    }

    return sent;
  }

  private async process(item: PendingTicket): Promise<void> {
    if (!item.ticketId) {
      const result = await firstValueFrom(this.api.post<SaveTicketResult>('tickets', item.body));
      item.ticketId = result.id;
      item.folio = result.folio;
      await idbPut(OUTBOX_STORE, item);
    }

    for (let index = item.uploadedPhotos; index < item.photos.length; index++) {
      const photo = item.photos[index];
      const file = await firstValueFrom(this.api.upload(photo.blob, photo.fileName, 'photo'));
      await firstValueFrom(this.api.post(`tickets/${item.ticketId}/photos`, { fileId: file.id, caption: photo.caption, takenAt: photo.takenAt, location: photo.location }));
      item.uploadedPhotos = index + 1;
      await idbPut(OUTBOX_STORE, item);
    }

    if (item.signature) {
      const file = await firstValueFrom(this.api.upload(item.signature.blob, `firma-${item.clientId}.png`, 'signature'));
      await firstValueFrom(
        this.api.post(`tickets/${item.ticketId}/sign`, {
          signerName: item.signature.signerName,
          signerPosition: item.signature.signerPosition,
          signatureFileId: file.id,
          signedAt: item.signature.signedAt,
          location: item.signature.location,
        }),
      );
    }
  }

  private async reload(): Promise<void> {
    try {
      this.items.set(await idbAll<PendingTicket>(OUTBOX_STORE));
    } catch {
      this.items.set([]);
    }
  }
}
