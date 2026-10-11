import {
  DynamicScheme,
  Hct,
  MaterialDynamicColors,
  TonalPalette,
  Variant,
  argbFromHex,
  hexFromArgb,
} from '@material/material-color-utilities';
import { CornerStyle, ThemePalette } from '../models';

/** Material system tokens consumed by Angular Material (`--mat-sys-*`), same set as the ChronoLith Shell. */
const SYSTEM_TOKENS = new Set([
  'background', 'on_background',
  'surface', 'surface_dim', 'surface_bright',
  'surface_container_lowest', 'surface_container_low', 'surface_container', 'surface_container_high', 'surface_container_highest',
  'on_surface', 'on_surface_variant', 'outline', 'outline_variant',
  'inverse_surface', 'inverse_on_surface', 'inverse_primary',
  'primary', 'on_primary', 'primary_container', 'on_primary_container',
  'primary_fixed', 'primary_fixed_dim', 'on_primary_fixed', 'on_primary_fixed_variant',
  'secondary', 'on_secondary', 'secondary_container', 'on_secondary_container',
  'secondary_fixed', 'secondary_fixed_dim', 'on_secondary_fixed', 'on_secondary_fixed_variant',
  'tertiary', 'on_tertiary', 'tertiary_container', 'on_tertiary_container',
  'tertiary_fixed', 'tertiary_fixed_dim', 'on_tertiary_fixed', 'on_tertiary_fixed_variant',
  'error', 'on_error', 'error_container', 'on_error_container',
]);

type BrandRole = 'primary' | 'secondary' | 'tertiary';

const dynamicColors = new MaterialDynamicColors();

/** Corner radius of each style (Material 3 shape scale). */
const CORNERS: Record<CornerStyle, Record<string, string>> = {
  rounded: { 'extra-small': '4px', small: '8px', medium: '12px', large: '16px', 'extra-large': '28px', sm: '8px', md: '12px', lg: '20px' },
  soft: { 'extra-small': '2px', small: '4px', medium: '8px', large: '10px', 'extra-large': '16px', sm: '6px', md: '8px', lg: '12px' },
  square: { 'extra-small': '0px', small: '2px', medium: '2px', large: '4px', 'extra-large': '4px', sm: '2px', md: '2px', lg: '4px' },
};

const TYPE_SCALE = ['display', 'headline', 'title', 'body', 'label'].flatMap((role) => ['large', 'medium', 'small'].map((size) => `${role}-${size}`));

export interface BrandingStyle {
  palette: ThemePalette;
  corners?: CornerStyle;
  fontFamily?: string | null;
}

/**
 * CSS that re-themes Angular Material from the eight seed colors of a company (same algorithm as the
 * ChronoLith Shell, light and dark values together with `light-dark()`), scoped to `selector`: the whole
 * document in standalone mode, or the Brocal area and its overlays inside ChronoLith.
 */
export function buildThemeCss(style: BrandingStyle, selector: string): string {
  const palette = style.palette;
  const light = createScheme(palette, false);
  const dark = createScheme(palette, true);
  const tokens = new Map<string, [string, string]>();

  for (const color of dynamicColors.allColors) {
    if (SYSTEM_TOKENS.has(color.name)) {
      tokens.set(`--mat-sys-${color.name.replace(/_/g, '-')}`, [hexFromArgb(color.getArgb(light)), hexFromArgb(color.getArgb(dark))]);
    }
  }

  tokens.set('--mat-sys-surface-variant', tokens.get('--mat-sys-surface-container-highest')!);
  tokens.set('--mat-sys-surface-tint', tokens.get('--mat-sys-primary')!);

  applyBrandFidelity(tokens, 'primary', palette.primary);
  applyBrandFidelity(tokens, 'secondary', palette.secondary);
  applyBrandFidelity(tokens, 'tertiary', palette.tertiary);

  const declarations = [...tokens].map(([name, [l, d]]) => `${name}: light-dark(${l}, ${d});`);
  declarations.push(
    `--cl-brand-primary: ${palette.primary};`,
    `--cl-brand-secondary: ${palette.secondary};`,
    `--cl-brand-accent: ${palette.tertiary};`,
    ...semanticTokens('success', palette.success),
    ...semanticTokens('warning', palette.warning),
    ...semanticTokens('info', palette.info),
  );

  if (style.corners) {
    const corners = CORNERS[style.corners];
    for (const size of ['extra-small', 'small', 'medium', 'large', 'extra-large']) {
      declarations.push(`--mat-sys-corner-${size}: ${corners[size]};`);
    }

    declarations.push(`--cl-radius-sm: ${corners['sm']};`, `--cl-radius-md: ${corners['md']};`, `--cl-radius-lg: ${corners['lg']};`);
  }

  const font = style.fontFamily?.trim();
  if (font) {
    const family = `'${font.replace(/'/g, '')}', 'Segoe UI', Roboto, sans-serif`;
    declarations.push(`--br-font-family: ${family};`, `font-family: ${family};`);
    for (const role of TYPE_SCALE) {
      declarations.push(`--mat-sys-${role}-font: ${family};`);
    }
  }

  return `${selector} {\n  ${declarations.join('\n  ')}\n}\n`;
}

function createScheme(palette: ThemePalette, isDark: boolean): DynamicScheme {
  const neutral = Hct.fromInt(argbFromHex(palette.neutral));

  return new DynamicScheme({
    sourceColorHct: Hct.fromInt(argbFromHex(palette.primary)),
    variant: Variant.TONAL_SPOT,
    contrastLevel: 0,
    isDark,
    primaryPalette: TonalPalette.fromInt(argbFromHex(palette.primary)),
    secondaryPalette: TonalPalette.fromInt(argbFromHex(palette.secondary)),
    tertiaryPalette: TonalPalette.fromInt(argbFromHex(palette.tertiary)),
    neutralPalette: TonalPalette.fromHueAndChroma(neutral.hue, Math.min(neutral.chroma, 8)),
    neutralVariantPalette: TonalPalette.fromHueAndChroma(neutral.hue, Math.min(neutral.chroma + 4, 16)),
    errorPalette: TonalPalette.fromInt(argbFromHex(palette.error)),
  });
}

/** Keeps the exact brand color in light mode where accessibility allows it (same rule as the Shell). */
function applyBrandFidelity(tokens: Map<string, [string, string]>, role: BrandRole, seed: string): void {
  const argb = argbFromHex(seed);
  const tone = Hct.fromInt(argb).tone;
  const brand = hexFromArgb(argb);

  if (tone <= 50) {
    tokens.set(`--mat-sys-${role}`, [brand, tokens.get(`--mat-sys-${role}`)![1]]);
    tokens.set(`--mat-sys-on-${role}`, ['#ffffff', tokens.get(`--mat-sys-on-${role}`)![1]]);
  } else if (tone >= 60) {
    const onContainer = hexFromArgb(TonalPalette.fromInt(argb).tone(10));
    tokens.set(`--mat-sys-${role}-container`, [brand, tokens.get(`--mat-sys-${role}-container`)![1]]);
    tokens.set(`--mat-sys-on-${role}-container`, [onContainer, tokens.get(`--mat-sys-on-${role}-container`)![1]]);
  }
}

function semanticTokens(name: string, seed: string): string[] {
  const palette = TonalPalette.fromInt(argbFromHex(seed));
  const tone = (t: number) => hexFromArgb(palette.tone(t));

  return [
    `--cl-${name}: light-dark(${tone(40)}, ${tone(80)});`,
    `--cl-on-${name}: light-dark(${tone(100)}, ${tone(20)});`,
    `--cl-${name}-container: light-dark(${tone(90)}, ${tone(30)});`,
    `--cl-on-${name}-container: light-dark(${tone(10)}, ${tone(90)});`,
  ];
}
