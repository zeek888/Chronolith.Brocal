import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, input, viewChild } from '@angular/core';
import { money, num } from '../core/format';

type ECharts = import('echarts/core').ECharts;

export interface ChartSeries {
  name: string;
  data: number[];
  type?: 'bar' | 'line';
  stack?: string;
}

export type ChartSpec =
  | { kind: 'bar' | 'line'; categories: string[]; series: ChartSeries[]; money?: boolean; horizontal?: boolean }
  | { kind: 'pie'; items: { name: string; value: number }[]; money?: boolean };

let echartsModule: Promise<typeof import('echarts/core')> | null = null;

/** ECharts is loaded on demand (only pages with charts download it) with just the pieces used. */
function loadEcharts(): Promise<typeof import('echarts/core')> {
  echartsModule ??= Promise.all([import('echarts/core'), import('echarts/charts'), import('echarts/components'), import('echarts/renderers')]).then(
    ([core, charts, components, renderers]) => {
      core.use([charts.BarChart, charts.LineChart, charts.PieChart, components.GridComponent, components.TooltipComponent, components.LegendComponent, renderers.CanvasRenderer]);
      return core;
    },
  );
  return echartsModule;
}

/** Resolves a theme token (`light-dark()` included) to the actual color of the element. */
function resolveColor(host: HTMLElement, token: string, fallback: string): string {
  const probe = document.createElement('span');
  probe.style.color = `var(${token}, ${fallback})`;
  probe.style.display = 'none';
  host.appendChild(probe);
  const color = getComputedStyle(probe).color;
  probe.remove();
  return color || fallback;
}

/** Bar, line and pie charts colored with the theme of the company (and its dark mode). */
@Component({
  selector: 'br-chart',
  template: `<div #canvas class="br-chart" [style.height]="height()"></div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chart implements AfterViewInit, OnDestroy {
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private chart: ECharts | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private themeObserver: MutationObserver | null = null;

  readonly spec = input.required<ChartSpec>();
  readonly height = input('280px');

  constructor() {
    effect(() => {
      const spec = this.spec();
      if (this.chart) {
        this.render(spec);
      }
    });
  }

  async ngAfterViewInit(): Promise<void> {
    const echarts = await loadEcharts();
    this.chart = echarts.init(this.canvas().nativeElement, null, { renderer: 'canvas' });
    this.render(this.spec());
    this.resizeObserver = new ResizeObserver(() => this.chart?.resize());
    this.resizeObserver.observe(this.canvas().nativeElement);
    // Dark mode switches (class on <html>) recolor the chart.
    this.themeObserver = new MutationObserver(() => this.render(this.spec()));
    this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'style'] });
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.themeObserver?.disconnect();
    this.chart?.dispose();
  }

  private render(spec: ChartSpec): void {
    if (!this.chart) {
      return;
    }

    const host = this.canvas().nativeElement;
    const colors = [
      resolveColor(host, '--mat-sys-primary', '#0b365b'),
      resolveColor(host, '--mat-sys-tertiary', '#e58e38'),
      resolveColor(host, '--cl-info', '#2c9ec8'),
      resolveColor(host, '--cl-success', '#5a9e34'),
      resolveColor(host, '--cl-warning', '#e0a82e'),
      resolveColor(host, '--mat-sys-secondary', '#2874a6'),
      resolveColor(host, '--mat-sys-error', '#d64532'),
    ];
    const text = resolveColor(host, '--mat-sys-on-surface-variant', '#5e6872');
    const grid = resolveColor(host, '--mat-sys-outline-variant', '#d9d9d9');
    const format = (value: number) => (spec.money ? money(value) : num(value));
    const short = (value: number) => (spec.money ? compact(value) : num(value, 1));

    if (spec.kind === 'pie') {
      this.chart.setOption(
        {
          color: colors,
          tooltip: { trigger: 'item', valueFormatter: (v: unknown) => format(Number(v)) },
          legend: { bottom: 0, textStyle: { color: text }, type: 'scroll' },
          series: [{ type: 'pie', radius: ['45%', '72%'], center: ['50%', '44%'], itemStyle: { borderRadius: 4, borderColor: 'transparent' }, label: { color: text }, data: spec.items }],
        },
        true,
      );
      return;
    }

    const categoryAxis = { type: 'category', data: spec.categories, axisLabel: { color: text }, axisLine: { lineStyle: { color: grid } } };
    const valueAxis = { type: 'value', axisLabel: { color: text, formatter: short }, splitLine: { lineStyle: { color: grid } } };
    this.chart.setOption(
      {
        color: colors,
        tooltip: { trigger: 'axis', valueFormatter: (v: unknown) => format(Number(v)) },
        legend: spec.series.length > 1 ? { top: 0, textStyle: { color: text } } : undefined,
        grid: { left: 8, right: 16, top: spec.series.length > 1 ? 32 : 12, bottom: 8, containLabel: true },
        xAxis: spec.horizontal ? valueAxis : categoryAxis,
        yAxis: spec.horizontal ? categoryAxis : valueAxis,
        series: spec.series.map((s) => ({ name: s.name, type: s.type ?? spec.kind, data: s.data, stack: s.stack, smooth: true, barMaxWidth: 36, itemStyle: { borderRadius: 3 } })),
      },
      true,
    );
  }
}

function compact(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1_000_000) {
    return `${num(value / 1_000_000, 1)} M`;
  }

  return abs >= 1_000 ? `${num(value / 1_000, 0)} k` : num(value, 0);
}
