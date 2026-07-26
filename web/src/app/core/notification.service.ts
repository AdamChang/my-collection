import { Injectable, signal } from '@angular/core';

export interface Notification {
  id: number;
  kind: 'error' | 'success';
  message: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;

  readonly notifications = signal<Notification[]>([]);

  error(message: string): void {
    this.push('error', message);
  }

  success(message: string): void {
    this.push('success', message);
  }

  dismiss(id: number): void {
    this.notifications.update((all) => all.filter((n) => n.id !== id));
  }

  private push(kind: Notification['kind'], message: string): void {
    const notification: Notification = { id: this.nextId++, kind, message };
    this.notifications.update((all) => [...all, notification]);
    setTimeout(() => this.dismiss(notification.id), 6000);
  }
}
