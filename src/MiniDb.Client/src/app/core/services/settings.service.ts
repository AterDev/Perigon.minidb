import { Injectable, signal } from '@angular/core';
import { TauriService } from './tauri.service';
import { AppSettings } from '../models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private _settings = signal<AppSettings>({
    theme: 'system',
    language: 'en',
    enableMica: true,
  });

  readonly settings = this._settings.asReadonly();

  constructor(private tauri: TauriService) {}

  async load(): Promise<void> {
    try {
      const settings = await this.tauri.getSettings();
      this._settings.set(settings);
    } catch {
      // Use defaults
    }
  }

  async save(settings: Partial<AppSettings>): Promise<void> {
    const updated = { ...this._settings(), ...settings };
    await this.tauri.saveSettings(updated);
    this._settings.set(updated);
  }

  get currentLanguage(): string {
    return this._settings().language;
  }

  get currentTheme(): string {
    return this._settings().theme;
  }
}
