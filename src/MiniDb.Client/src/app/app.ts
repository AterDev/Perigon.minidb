import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

// Angular Material
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';

import { SettingsService } from './core/services/settings.service';
import { ThemeService } from './core/services/theme.service';
import { ConnectionService } from './core/services/connection.service';
import { TauriService } from './core/services/tauri.service';

import { ConnectionsPageComponent } from './features/connections/connections-page.component';
import { DatabasePageComponent } from './features/database/database-page.component';

type StatusLevel = 'info' | 'success' | 'error';

interface ConnectionStateEventDetail {
  isConnected: boolean;
  id?: string;
  name?: string;
}

interface StatusEventDetail {
  level?: StatusLevel;
  key?: string;
  params?: Record<string, unknown>;
  message?: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    TranslateModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    ConnectionsPageComponent,
    DatabasePageComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  readonly i18nEpoch = signal(0);
  readonly isConnectionsManagerWindow = signal(false);
  readonly isMaximized = signal(false);
  readonly isConnected = signal(false);
  readonly connectedId = signal('');
  readonly connectedName = signal('');
  readonly showConnectionsManager = signal(false);

  readonly statusLevel = signal<StatusLevel>('info');
  readonly statusKey = signal('status.ready');
  readonly statusParams = signal<Record<string, unknown> | undefined>(undefined);
  readonly statusText = signal('');
  private unlistenWindowResize?: () => void;
  private unlistenTauriConnectionState?: () => void;
  private unlistenTauriThemeChanged?: () => void;

  private readonly disconnectHandler = () => {
    this.applyConnectionState(false, '', '');
    this.pushStatus('info', 'status.disconnected');
  };

  private readonly connectionStateHandler = (event: Event) => {
    const detail = (event as CustomEvent<ConnectionStateEventDetail>).detail;
    if (!detail) return;

    this.applyConnectionState(detail.isConnected, detail.id ?? '', detail.name ?? '');

    if (detail.isConnected) {
      this.pushStatus('success', 'status.connectedAs', { name: detail.name ?? '' });
      window.dispatchEvent(new CustomEvent('minidb:refresh-request'));
    } else {
      this.pushStatus('info', 'status.disconnected');
    }
  };

  private readonly statusHandler = (event: Event) => {
    const detail = (event as CustomEvent<StatusEventDetail>).detail;
    if (!detail) return;
    this.pushStatus(detail.level ?? 'info', detail.key ?? 'status.ready', detail.params, detail.message ?? '');
  };

  constructor(
    private settings: SettingsService,
    private theme: ThemeService,
    private connections: ConnectionService,
    private tauri: TauriService,
    private translate: TranslateService,
  ) {}

  async ngOnInit(): Promise<void> {
    await this.detectWindowRole();
    await this.settings.load();
    await this.connections.load();

    window.addEventListener('minidb:disconnect', this.disconnectHandler);
    window.addEventListener('minidb:connection-state', this.connectionStateHandler as EventListener);
    window.addEventListener('minidb:status', this.statusHandler as EventListener);

    const lang = this.settings.currentLanguage;
    this.translate.addLangs(['en', 'zh-CN']);
    this.translate.setDefaultLang('en');
    await firstValueFrom(this.translate.use(lang));
    this.i18nEpoch.update(v => v + 1);

    this.theme.applyTheme();
    this.theme.watchSystemTheme();

    await this.initializeTauriConnectionStateSync();
    await this.initializeTauriThemeSync();

    if (this.isConnectionsManagerWindow()) {
      return;
    }

    await this.initializeWindowStateSync();
  }

  ngOnDestroy(): void {
    window.removeEventListener('minidb:disconnect', this.disconnectHandler);
    window.removeEventListener('minidb:connection-state', this.connectionStateHandler as EventListener);
    window.removeEventListener('minidb:status', this.statusHandler as EventListener);
    this.unlistenWindowResize?.();
    this.unlistenTauriConnectionState?.();
    this.unlistenTauriThemeChanged?.();
  }

  async openConnectionsManager(): Promise<void> {
    try {
      await this.tauri.openConnectionsManagerWindow();
    } catch {
      // Browser fallback: still use in-page overlay.
      this.showConnectionsManager.set(true);
    }
  }

  closeConnectionsManager(): void {
    this.showConnectionsManager.set(false);
  }

  onConnected(event: { isConnected: boolean; id?: string; name: string }): void {
    this.applyConnectionState(event.isConnected, event.id ?? '', event.name);
    if (event.isConnected) {
      this.pushStatus('success', 'status.connectedAs', { name: event.name });
      window.dispatchEvent(new CustomEvent('minidb:refresh-request'));
    }

    if (this.isConnectionsManagerWindow()) {
      void this.closeWindow();
    } else {
      this.showConnectionsManager.set(false);
    }
  }

  navigateToDatabase(): void {
    this.showConnectionsManager.set(false);
  }

  async refreshDatabaseFromMenu(): Promise<void> {
    if (!this.isConnected()) {
      this.pushStatus('info', 'status.notConnectedHint');
      return;
    }
    this.pushStatus('info', 'status.refreshingDatabase');
    window.dispatchEvent(new CustomEvent('minidb:refresh-request'));
  }

  async switchLanguage(lang: string): Promise<void> {
    await firstValueFrom(this.translate.use(lang));
    this.i18nEpoch.update(v => v + 1);
    await this.settings.save({ language: lang as 'en' | 'zh-CN' });
  }

  get currentLanguage(): string {
    return this.settings.currentLanguage;
  }

  get currentTheme(): string {
    return this.settings.currentTheme;
  }

  async setTheme(theme: 'light' | 'dark' | 'system'): Promise<void> {
    await this.settings.save({ theme });
    this.theme.applyTheme(theme);

    try {
      const { emit } = await import('@tauri-apps/api/event');
      await emit('minidb:theme-changed', { theme });
    } catch {
      // Ignore in non-Tauri context.
    }
  }

  showAboutDialog(): void {
    window.alert(`${this.translate.instant('app.title')}\n${this.translate.instant('status.stackInfo')}`);
  }

  async minimizeWindow(): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      await getCurrentWindow().minimize();
    } catch (error) {
      console.error('minimizeWindow failed:', error);
    }
  }

  async toggleMaximizeWindow(): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      const appWindow = getCurrentWindow();
      await appWindow.toggleMaximize();
      this.isMaximized.set(await appWindow.isMaximized());
    } catch (error) {
      console.error('toggleMaximizeWindow failed:', error);
    }
  }

  async closeWindow(): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      await getCurrentWindow().close();
    } catch (error) {
      console.error('closeWindow failed:', error);
    }
  }

  get statusIcon(): string {
    return this.statusLevel() === 'error'
      ? 'error_outline'
      : this.statusLevel() === 'success'
        ? 'check_circle'
        : 'info';
  }

  private applyConnectionState(isConnected: boolean, id: string, name: string): void {
    this.isConnected.set(isConnected);
    this.connectedId.set(isConnected ? id : '');
    this.connectedName.set(isConnected ? name : '');
  }

  private pushStatus(
    level: StatusLevel,
    key: string,
    params?: Record<string, unknown>,
    message = ''
  ): void {
    this.statusLevel.set(level);
    this.statusKey.set(key);
    this.statusParams.set(params);
    this.statusText.set(message);
  }

  private async initializeWindowStateSync(): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      const appWindow = getCurrentWindow();
      this.isMaximized.set(await appWindow.isMaximized());
      this.unlistenWindowResize = await appWindow.onResized(async () => {
        this.isMaximized.set(await appWindow.isMaximized());
      });
    } catch {
      // Ignore in non-Tauri context.
    }
  }

  private async detectWindowRole(): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      this.isConnectionsManagerWindow.set(getCurrentWindow().label === 'connections-manager');
    } catch {
      this.isConnectionsManagerWindow.set(false);
    }
  }

  private async initializeTauriConnectionStateSync(): Promise<void> {
    try {
      const { getCurrentWindow } = await import('@tauri-apps/api/window');
      if (getCurrentWindow().label !== 'main') return;

      const { listen } = await import('@tauri-apps/api/event');
      this.unlistenTauriConnectionState = await listen<ConnectionStateEventDetail>(
        'minidb:connection-state',
        (event) => {
          const detail = event.payload;
          if (!detail) return;
          this.applyConnectionState(detail.isConnected, detail.id ?? '', detail.name ?? '');
          if (detail.isConnected) {
            this.pushStatus('success', 'status.connectedAs', { name: detail.name ?? '' });
            window.dispatchEvent(new CustomEvent('minidb:refresh-request'));
          } else {
            this.pushStatus('info', 'status.disconnected');
          }
        }
      );
    } catch {
      // Ignore in non-Tauri context.
    }
  }

  private async initializeTauriThemeSync(): Promise<void> {
    try {
      const { listen } = await import('@tauri-apps/api/event');
      this.unlistenTauriThemeChanged = await listen<{ theme?: 'light' | 'dark' | 'system' }>(
        'minidb:theme-changed',
        async (event) => {
          const theme = event.payload?.theme;
          if (!theme) return;
          this.theme.applyTheme(theme);
        }
      );
    } catch {
      // Ignore in non-Tauri context.
    }
  }
}
