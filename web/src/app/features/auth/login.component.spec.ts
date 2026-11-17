import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  it('renders the login form only and links to registration', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: AuthService, useValue: { login: () => Promise.resolve() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('.login__terminal')).toBeTruthy();
    expect(element.querySelector('input[name="displayName"]')).toBeNull();
    expect(element.querySelector('a.login__link')?.getAttribute('href')).toBe('/register');
  });

  it('keeps login actions at least 44px tall at 390px', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: AuthService, useValue: { login: () => Promise.resolve() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    const frame = document.createElement('iframe');
    frame.style.width = '390px';
    frame.style.height = '844px';
    frame.style.border = '0';
    document.body.append(frame);

    try {
      const frameDocument = frame.contentDocument!;
      const styles = frameDocument.createElement('style');
      styles.textContent = Array.from(document.styleSheets)
        .flatMap((sheet) => Array.from(sheet.cssRules))
        .map((rule) => rule.cssText)
        .join('\n');
      frameDocument.head.append(styles);
      frameDocument.body.append(fixture.nativeElement.cloneNode(true));

      const targets = frameDocument.querySelectorAll<HTMLElement>(
        'button[type="submit"], .login__link',
      );
      expect(targets.length).toBe(2);
      targets.forEach((target) => {
        expect(parseFloat(frame.contentWindow!.getComputedStyle(target).height))
          .withContext(target.textContent?.trim() ?? target.tagName)
          .toBeGreaterThanOrEqual(44);
      });
    } finally {
      frame.remove();
    }
  });
});
