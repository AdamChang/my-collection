import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';

const SESSION = JSON.stringify({
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
});

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('exposes the Neon Grid design tokens', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const styles = getComputedStyle(document.documentElement);
    expect(styles.getPropertyValue('--mc-bg').trim()).toBe('#05070d');
    expect(styles.getPropertyValue('--mc-cyan').trim()).toBe('#20e7ff');
    expect(styles.getPropertyValue('--mc-magenta').trim()).toBe('#ff2f8b');
  });

  it('renders the authenticated Neon Grid shell and brand', () => {
    localStorage.setItem('mycollection.session', SESSION);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-app-shell]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toContain('MY//COLLECTION');
    expect(fixture.nativeElement.querySelector('.nav__links')).toBeTruthy();
  });

  it('gives the brand mark a box so its dimensions and rotation render', () => {
    localStorage.setItem('mycollection.session', SESSION);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const brand: HTMLElement = fixture.nativeElement.querySelector('.brand');
    const mark: HTMLElement = fixture.nativeElement.querySelector('.brand__mark');
    brand.style.display = 'block';

    expect(getComputedStyle(mark).display).toBe('inline-block');
  });

  it('hides the navigation while unauthenticated', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('nav')).toBeNull();
  });

  it('renders a navigation link for every shell route once authenticated', () => {
    localStorage.setItem('mycollection.session', SESSION);
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const hrefs = Array.from(
      fixture.nativeElement.querySelectorAll('nav a') as NodeListOf<HTMLAnchorElement>,
    ).map((a) => a.getAttribute('href'));

    expect(hrefs).toEqual(['/', '/catalog', '/categories', '/settings']);
  });

  it('renders a router outlet for the active page', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('router-outlet')).toBeTruthy();
  });

  it('renders notifications as toasts', () => {
    const notifications = TestBed.inject(NotificationService);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    notifications.error('同步失敗');
    fixture.detectChanges();

    const toast: HTMLElement = fixture.nativeElement.querySelector('.toast');
    expect(toast.textContent).toContain('同步失敗');
    expect(toast.classList).toContain('toast--error');
  });
});
