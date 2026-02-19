import { Injectable, signal } from '@angular/core';
import { SettingsService } from './settings.service';

type Theme = 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private _effectiveTheme = signal<Theme>('dark');
  readonly effectiveTheme = this._effectiveTheme.asReadonly();

  constructor(private settings: SettingsService) {}

  applyTheme(theme?: string): void {
    const selected = theme ?? this.settings.currentTheme;
    let effective: Theme;

    if (selected === 'system') {
      effective = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    } else {
      effective = selected as Theme;
    }

    this._effectiveTheme.set(effective);
    document.documentElement.setAttribute('data-theme', effective);
    document.documentElement.classList.toggle('dark', effective === 'dark');
    void this.applyNativeWindowTheme(effective);
  }

  private async applyNativeWindowTheme(theme: Theme): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      await getCurrentWindow().setTheme(theme);
    } catch {
      // Not running in Tauri desktop context (e.g. browser dev mode)
    }
  }

  async toggleTheme(): Promise<void> {
    const current = this._effectiveTheme();
    const next: Theme = current === 'dark' ? 'light' : 'dark';
    await this.settings.save({ theme: next });
    this.applyTheme(next);
  }

  watchSystemTheme(): void {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (this.settings.currentTheme === 'system') {
        this.applyTheme();
      }
    });
  }
}
