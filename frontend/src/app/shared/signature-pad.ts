import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, inject, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TPipe } from '../core/i18n';

/**
 * Customer signature on screen (finger, stylus or mouse). Pointer events keep the stroke smooth on
 * tablets; the PNG keeps a transparent background so it prints cleanly on the ticket PDF.
 */
@Component({
  selector: 'br-signature-pad',
  imports: [MatButtonModule, MatIconModule, TPipe],
  template: `
    <div class="br-signature">
      <canvas #canvas (pointerdown)="start($event)" (pointermove)="move($event)" (pointerup)="end()" (pointerleave)="end()" (pointercancel)="end()"></canvas>
      <div class="br-signature__line">
        <span>{{ 'tickets.signHere' | t }}</span>
      </div>
      <button mat-button type="button" class="br-signature__clear" (click)="clear()" [disabled]="empty()">
        <mat-icon>ink_eraser</mat-icon> {{ 'common.clear' | t }}
      </button>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignaturePad implements AfterViewInit, OnDestroy {
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject(ElementRef<HTMLElement>);
  private context: CanvasRenderingContext2D | null = null;
  private drawing = false;
  private last: { x: number; y: number } | null = null;
  private resizeObserver: ResizeObserver | null = null;

  readonly empty = signal(true);
  readonly changed = output<boolean>();

  ngAfterViewInit(): void {
    this.resize();
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  clear(): void {
    const canvas = this.canvas().nativeElement;
    this.context?.clearRect(0, 0, canvas.width, canvas.height);
    this.empty.set(true);
    this.changed.emit(false);
  }

  /** PNG of the signature, or null if nothing was drawn. */
  toBlob(): Promise<Blob | null> {
    if (this.empty()) {
      return Promise.resolve(null);
    }

    return new Promise((resolve) => this.canvas().nativeElement.toBlob(resolve, 'image/png'));
  }

  protected start(event: PointerEvent): void {
    event.preventDefault();
    this.canvas().nativeElement.setPointerCapture(event.pointerId);
    this.drawing = true;
    this.last = this.point(event);
  }

  protected move(event: PointerEvent): void {
    if (!this.drawing || !this.context || !this.last) {
      return;
    }

    event.preventDefault();
    const next = this.point(event);
    this.context.beginPath();
    this.context.moveTo(this.last.x, this.last.y);
    this.context.lineTo(next.x, next.y);
    this.context.stroke();
    this.last = next;
    if (this.empty()) {
      this.empty.set(false);
      this.changed.emit(true);
    }
  }

  protected end(): void {
    this.drawing = false;
    this.last = null;
  }

  private point(event: PointerEvent): { x: number; y: number } {
    const rect = this.canvas().nativeElement.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }

  private resize(): void {
    const canvas = this.canvas().nativeElement;
    const rect = canvas.getBoundingClientRect();
    const ratio = window.devicePixelRatio || 1;
    if (rect.width === 0 || (canvas.width === Math.round(rect.width * ratio) && canvas.height === Math.round(rect.height * ratio))) {
      return;
    }

    // Resizing clears the canvas: only allowed before signing.
    if (!this.empty()) {
      return;
    }

    canvas.width = Math.round(rect.width * ratio);
    canvas.height = Math.round(rect.height * ratio);
    this.context = canvas.getContext('2d');
    if (this.context) {
      this.context.scale(ratio, ratio);
      this.context.lineWidth = 2.4;
      this.context.lineCap = 'round';
      this.context.lineJoin = 'round';
      this.context.strokeStyle = '#0b1f33';
    }
  }
}
