import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  it('renders the authentication terminal and retains mode switching', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: AuthService, useValue: { login: () => Promise.resolve(), register: () => Promise.resolve() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.login__terminal')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('input[name="displayName"]')).toBeNull();

    fixture.componentInstance.toggle();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[name="displayName"]')).toBeTruthy();
  });

  it('keeps login actions at least 44px tall at 390px', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: AuthService, useValue: { login: () => Promise.resolve(), register: () => Promise.resolve() } },
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
        'button[type="submit"], .login__toggle',
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
