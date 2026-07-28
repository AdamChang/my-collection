# Neon Grid Frontend Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the approved Neon Grid Cyberpunk visual system to every Angular screen without changing routes, API contracts, or existing business behavior.

**Architecture:** Global design tokens and base controls live in `web/src/styles.css`. The application shell owns navigation, page background, and notifications. Feature and shared components keep focused layout styles and add semantic wrappers that can be protected by shallow Angular tests.

**Tech Stack:** Angular 20.3 standalone components, signals, template control flow, TypeScript 5.9, CSS custom properties, Karma, Jasmine, ChromeHeadless

## Global Constraints

- Implement the approved design in `docs/superpowers/specs/2026-07-28-system-categories-neon-grid-design.md`.
- Use Neon Grid direction A: deep blue-black, cyan primary, restrained magenta, fine grid, clipped corners.
- Do not add an Angular UI framework, icon package, remote font, image asset, or npm dependency.
- Keep every existing route, API DTO, service call, and user workflow unchanged.
- Do not invent statistics that are not already present in API responses.
- Preserve `DynamicFormComponent`'s one-way initial-value pattern; do not bind live `attributes` back into `[value]`.
- Every interactive control needs visible `:focus-visible`; status must not rely on color alone.
- Respect `prefers-reduced-motion`.
- Desktop and mobile layouts must not create horizontal page overflow.
- Do not overwrite or stage the existing user change in `web/angular.json`.
- Use TDD for new semantic structure and regression behavior; use browser inspection for visual layout.

---

## File Structure

- Modify `web/src/styles.css`: tokens, reset, controls, panels, badges, tables, focus, motion, responsive base.
- Modify `web/src/app/app.ts` and `web/src/app/app.spec.ts`: authenticated shell, brand, navigation, toasts.
- Modify `web/src/app/features/auth/login.component.ts`: terminal-style authentication screen.
- Create `web/src/app/features/auth/login.component.spec.ts`.
- Modify `web/src/app/features/showcase/showcase.component.ts`: archive header, count, collection wall, empty state.
- Create `web/src/app/features/showcase/showcase.component.spec.ts`.
- Modify `web/src/app/features/catalog/catalog.component.ts`: control-panel filters and results terminal.
- Create `web/src/app/features/catalog/catalog.component.spec.ts`.
- Modify `web/src/app/shared/item-card/item-card.component.ts` and its existing spec.
- Modify `web/src/app/features/item-detail/item-detail.component.ts`.
- Create `web/src/app/features/item-detail/item-detail.component.spec.ts`.
- Modify `web/src/app/shared/dynamic-form/dynamic-form.component.ts`.
- Modify `web/src/app/shared/tag-input/tag-input.component.ts`.
- Modify `web/src/app/shared/image-uploader/image-uploader.component.ts`.
- Modify `web/src/app/features/categories/categories.component.ts`.
- Create `web/src/app/features/categories/categories.component.spec.ts`.
- Modify `web/src/app/features/settings/settings.component.ts`.
- Create `web/src/app/features/settings/settings.component.spec.ts`.
- Modify `web/src/app/features/public/public-share.component.ts`.
- Create `web/src/app/features/public/public-share.component.spec.ts`.

### Task 1: Global Tokens and Authenticated Application Shell

**Files:**
- Modify: `web/src/styles.css`
- Modify: `web/src/app/app.ts`
- Modify: `web/src/app/app.spec.ts`

**Interfaces:**
- Produces global tokens `--mc-bg`, `--mc-surface`, `--mc-border`, `--mc-text`, `--mc-cyan`, `--mc-magenta`.
- Produces shell hooks `[data-app-shell]`, `.brand`, `.nav__links`, `.shell`.
- Preserves routes `/`, `/catalog`, `/categories`, `/settings` and `auth.logout()`.

- [ ] **Step 1: Write failing shell and token tests**

Extend `app.spec.ts`:

```typescript
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
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
cd web
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/app.spec.ts
```

Expected: token and shell tests fail because the custom properties and semantic shell elements do not exist.

- [ ] **Step 3: Build the global visual foundation**

Replace the current one-comment `styles.css` with the following foundation. Later tasks add their named selectors to this file only when the selector is shared by more than one component:

```css
:root {
  color-scheme: dark;
  --mc-bg: #05070d;
  --mc-surface: #09111a;
  --mc-surface-raised: #0d1824;
  --mc-border: #17384a;
  --mc-border-strong: #26647a;
  --mc-text: #e9f7ff;
  --mc-text-muted: #7f9aae;
  --mc-cyan: #20e7ff;
  --mc-cyan-soft: rgb(32 231 255 / 14%);
  --mc-magenta: #ff2f8b;
  --mc-warning: #f4d35e;
  --mc-danger: #ff4d6d;
  --mc-success: #46f2a5;
  --mc-cut: 10px;
  --mc-shadow: 0 18px 48px rgb(0 0 0 / 38%);
  font-family: "Segoe UI", "Noto Sans TC", system-ui, sans-serif;
  background: var(--mc-bg);
  color: var(--mc-text);
}

* { box-sizing: border-box; }

html { min-width: 320px; background: var(--mc-bg); }

body {
  margin: 0;
  min-height: 100vh;
  background:
    linear-gradient(rgb(32 231 255 / 3%) 1px, transparent 1px),
    linear-gradient(90deg, rgb(32 231 255 / 3%) 1px, transparent 1px),
    radial-gradient(circle at 10% 0%, rgb(32 231 255 / 9%), transparent 32rem),
    var(--mc-bg);
  background-size: 36px 36px, 36px 36px, auto, auto;
  color: var(--mc-text);
}

button, input, select, textarea { font: inherit; }

a { color: var(--mc-cyan); text-underline-offset: 0.22em; }

button {
  min-height: 2.65rem;
  border: 1px solid var(--mc-border-strong);
  padding: 0.58rem 0.95rem;
  background: var(--mc-surface-raised);
  color: var(--mc-text);
  cursor: pointer;
  clip-path: polygon(0 0, calc(100% - 8px) 0, 100% 8px, 100% 100%, 0 100%);
}

button[type="submit"], .button--primary {
  border-color: var(--mc-cyan);
  background: var(--mc-cyan);
  color: #031015;
  font-weight: 800;
}

.button--danger { border-color: var(--mc-danger); color: var(--mc-danger); }

input:not([type="checkbox"]):not([type="file"]),
select,
textarea {
  width: 100%;
  min-height: 2.7rem;
  border: 1px solid var(--mc-border);
  border-radius: 2px;
  padding: 0.62rem 0.72rem;
  background: #07101a;
  color: var(--mc-text);
}

:where(a, button, input, select, textarea):focus-visible {
  outline: 2px solid var(--mc-cyan);
  outline-offset: 3px;
}

button:disabled { cursor: not-allowed; opacity: 0.5; }

.mc-panel {
  border: 1px solid var(--mc-border);
  padding: clamp(1rem, 2.5vw, 1.5rem);
  background: rgb(9 17 26 / 92%);
  box-shadow: var(--mc-shadow);
  clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%);
}

.mc-eyebrow {
  color: var(--mc-cyan);
  font: 700 0.72rem/1.4 Consolas, monospace;
  letter-spacing: 0.18em;
  text-transform: uppercase;
}

.mc-muted { color: var(--mc-text-muted); }
.mc-badge { border: 1px solid var(--mc-border-strong); padding: 0.18rem 0.45rem; font-size: 0.72rem; }
.mc-empty { border: 1px dashed var(--mc-border-strong); padding: 2rem; color: var(--mc-text-muted); text-align: center; }

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    scroll-behavior: auto !important;
    transition-duration: 0.01ms !important;
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
  }
}
```

- [ ] **Step 4: Refactor the application shell template**

Use this structure in `app.ts` while preserving the existing signal and click bindings:

```html
@if (auth.isAuthenticated()) {
  <header class="app-header" data-app-shell>
    <a class="brand" routerLink="/" aria-label="MyCollection 首頁">
      <span class="brand__mark" aria-hidden="true"></span>
      <span>MY//COLLECTION</span>
    </a>
    <nav class="nav" aria-label="主要導覽">
      <div class="nav__links">
        <a routerLink="/" routerLinkActive="nav--active"
           [routerLinkActiveOptions]="{ exact: true }">精選</a>
        <a routerLink="/catalog" routerLinkActive="nav--active">庫存</a>
        <a routerLink="/categories" routerLinkActive="nav--active">品類</a>
        <a routerLink="/settings" routerLinkActive="nav--active">設定</a>
      </div>
      <button type="button" class="nav__logout" (click)="auth.logout()">登出</button>
    </nav>
  </header>
}

<div class="toasts" aria-live="polite">
  @for (notification of notifications.notifications(); track notification.id) {
    <div class="toast" [class.toast--error]="notification.kind === 'error'">
      <span class="toast__status">{{ notification.kind === 'error' ? 'ERROR' : 'OK' }}</span>
      {{ notification.message }}
    </div>
  }
</div>

<main class="shell" [class.shell--public]="!auth.isAuthenticated()">
  <router-outlet />
</main>
```

Add these component styles in `app.ts`:

```css
:host { display: block; min-height: 100vh; }
.app-header { position: sticky; top: 0; z-index: 20; display: flex; align-items: center;
  justify-content: space-between; gap: 1rem; min-height: 4rem; padding: 0.65rem 1rem;
  border-bottom: 1px solid var(--mc-border); background: rgb(5 7 13 / 88%);
  backdrop-filter: blur(16px); }
.brand { display: inline-flex; align-items: center; gap: 0.65rem; color: var(--mc-text);
  font: 800 0.84rem/1 Consolas, monospace; letter-spacing: 0.12em; text-decoration: none; }
.brand__mark { width: 1.2rem; height: 1.2rem; border: 2px solid var(--mc-cyan);
  transform: rotate(45deg); box-shadow: 0 0 14px var(--mc-cyan-soft); }
.nav, .nav__links { display: flex; align-items: center; gap: 0.35rem; }
.nav a { padding: 0.7rem 0.8rem; color: var(--mc-text-muted); text-decoration: none; }
.nav a:hover, .nav a.nav--active { color: var(--mc-cyan); background: var(--mc-cyan-soft); }
.nav__logout { margin-left: 0.5rem; }
.shell { width: min(84rem, 100%); margin: 0 auto; padding: clamp(1rem, 3vw, 2rem); }
.shell--public { width: 100%; padding: 0; }
.toasts { position: fixed; top: 4.75rem; right: 1rem; z-index: 30; display: grid;
  gap: 0.5rem; width: min(24rem, calc(100vw - 2rem)); }
.toast { display: grid; grid-template-columns: auto 1fr; gap: 0.65rem; padding: 0.8rem 1rem;
  border: 1px solid var(--mc-success); background: var(--mc-surface-raised); color: var(--mc-text); }
.toast--error { border-color: var(--mc-danger); }
.toast__status { color: var(--mc-success); font: 800 0.72rem/1.4 Consolas, monospace; }
.toast--error .toast__status { color: var(--mc-danger); }
@media (max-width: 700px) {
  .app-header { align-items: flex-start; flex-direction: column; }
  .nav { width: 100%; justify-content: space-between; }
  .nav__links { overflow-x: auto; }
}
```

- [ ] **Step 5: Run shell tests and verify GREEN**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/app.spec.ts
```

Expected: all `App` tests pass, including unchanged authentication and route assertions.

- [ ] **Step 6: Commit Task 1**

```powershell
git add web/src/styles.css web/src/app/app.ts web/src/app/app.spec.ts
git commit -m "feat(web): add neon grid foundation and shell"
```

### Task 2: Authentication, Showcase, Catalog, and Item Cards

**Files:**
- Modify: `web/src/app/features/auth/login.component.ts`
- Modify: `web/src/app/features/showcase/showcase.component.ts`
- Create: `web/src/app/features/showcase/showcase.component.spec.ts`
- Modify: `web/src/app/features/catalog/catalog.component.ts`
- Create: `web/src/app/features/catalog/catalog.component.spec.ts`
- Modify: `web/src/app/shared/item-card/item-card.component.ts`
- Modify: `web/src/app/shared/item-card/item-card.component.spec.ts`

**Interfaces:**
- Preserves authentication submission and mode switching.
- Preserves showcase paging.
- Preserves catalog search, category, schema attribute, tag, and paging behavior.
- Adds hooks `[data-showcase-terminal]`, `[data-catalog-controls]`, `[data-item-card]`.

- [ ] **Step 1: Write failing structural tests**

Create `login.component.spec.ts`:

```typescript
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
});
```

Create `showcase.component.spec.ts`:

```typescript
import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { ShowcaseComponent } from './showcase.component';

describe('ShowcaseComponent', () => {
  it('renders the archive terminal and useful empty state', async () => {
    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: { showcase: () => of({ items: [], total: 0, page: 1, pageSize: 24 }) },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-showcase-terminal]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.showcase__empty a')?.getAttribute('href'))
      .toBe('/catalog');
  });
});
```

Create `catalog.component.spec.ts`:

```typescript
import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CatalogComponent } from './catalog.component';

describe('CatalogComponent', () => {
  it('renders filters as a control panel and keeps the create action', async () => {
    await TestBed.configureTestingModule({
      imports: [CatalogComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            tags: () => of([]),
            search: () => of({ items: [], total: 0, page: 1, pageSize: 24 }),
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CatalogComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-catalog-controls]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('a[href="/items/new"]')).toBeTruthy();
  });
});
```

Add to `item-card.component.spec.ts`:

```typescript
it('exposes an accessible clickable card contract', () => {
  render(item());

  const card: HTMLAnchorElement = fixture.nativeElement.querySelector('[data-item-card]');
  expect(card).toBeTruthy();
  expect(card.getAttribute('aria-label')).toBe('查看 初音ミク 1/8');
});
```

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/auth/login.component.spec.ts --include src/app/features/showcase/showcase.component.spec.ts --include src/app/features/catalog/catalog.component.spec.ts --include src/app/shared/item-card/item-card.component.spec.ts
```

Expected: new data hooks and the item-card accessible label are missing.

- [ ] **Step 3: Redesign authentication**

Restructure `login.component.ts` around:

```html
<main class="login">
  <section class="login__terminal mc-panel">
    <div class="mc-eyebrow">PRIVATE ARCHIVE / AUTH GATE</div>
    <h1>MY//COLLECTION</h1>
    <p class="mc-muted">跨越實體與數位世界，建立你的私人收藏座標。</p>
    <form (ngSubmit)="submit()">
      @if (mode() === 'register') {
        <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required /></label>
      }
      <label>Email<input name="email" type="email" [(ngModel)]="email" required /></label>
      <label>密碼<input name="password" type="password" [(ngModel)]="password"
                     required minlength="8" /></label>
      <button type="submit" [disabled]="busy()">
        {{ busy() ? '連線中…' : mode() === 'login' ? '登入系統' : '建立帳號' }}
      </button>
    </form>
    <button type="button" class="login__toggle" (click)="toggle()">
      {{ mode() === 'login' ? '還沒有帳號？註冊' : '已經有帳號？登入' }}
    </button>
  </section>
</main>
```

Keep the original three controls verbatim inside the form. Style the screen as a centered terminal with a subtle grid and a maximum content width of `26rem`.

- [ ] **Step 4: Redesign showcase and catalog**

For showcase, replace its header with:

```html
<header class="showcase__header" data-showcase-terminal>
  <div>
    <div class="mc-eyebrow">CURATED ARCHIVE / ONLINE</div>
    <h1>精選收藏</h1>
    <p class="mc-muted">{{ total() }} 件已編入精選展示</p>
  </div>
  <a class="showcase__all" routerLink="/catalog">OPEN CATALOG →</a>
</header>
```

Leave the current loading branch, empty-state paragraph, `@for (item of items(); track item.id)` loop, and load-more button immediately after this header without changing their bindings. Set `.showcase__wall` to `grid-template-columns: repeat(auto-fill, minmax(220px, 1fr))`, `gap: 1rem`, and `grid-template-columns: 1fr` below `520px`.

For catalog, apply `mc-panel` and `data-catalog-controls` to the `<aside>`, add an eyebrow and `h1` to the results header, and preserve every existing binding:

```html
<aside class="catalog__filters mc-panel" data-catalog-controls>
  <div class="mc-eyebrow">FILTER MATRIX</div>
  <h2>篩選控制台</h2>
</aside>
```

Move the current search label, category select, `searchableFields()` loop, and tag fieldset between the `h2` and closing `aside` without changing any binding. The results header must still show `{{ total() }} 件` and link to `/items/new`. On screens below `760px`, change `.catalog` to one column and remove sticky positioning.

- [ ] **Step 5: Redesign the reusable card without changing its data behavior**

Change the root link:

```html
<a class="card" data-item-card
   [attr.aria-label]="'查看 ' + item().name"
   [routerLink]="['/items', item().id]">
```

Retain the existing image selection, placeholder, showcased badge, card fields, and tags. Replace legacy white/gray styles with global tokens, a clipped corner, cyan hover border, image overlay, and a non-looping `transform 160ms ease`.

- [ ] **Step 6: Run focused tests and verify GREEN**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/auth/login.component.spec.ts --include src/app/features/showcase/showcase.component.spec.ts --include src/app/features/catalog/catalog.component.spec.ts --include src/app/shared/item-card/item-card.component.spec.ts
```

Expected: all focused tests pass.

- [ ] **Step 7: Commit Task 2**

```powershell
git add web/src/app/features/auth/login.component.ts web/src/app/features/auth/login.component.spec.ts web/src/app/features/showcase/showcase.component.ts web/src/app/features/showcase/showcase.component.spec.ts web/src/app/features/catalog/catalog.component.ts web/src/app/features/catalog/catalog.component.spec.ts web/src/app/shared/item-card/item-card.component.ts web/src/app/shared/item-card/item-card.component.spec.ts
git commit -m "feat(web): redesign core collection screens"
```

### Task 3: Item Editor and Shared Form Controls

**Files:**
- Modify: `web/src/app/features/item-detail/item-detail.component.ts`
- Create: `web/src/app/features/item-detail/item-detail.component.spec.ts`
- Modify: `web/src/app/shared/dynamic-form/dynamic-form.component.ts`
- Modify: `web/src/app/shared/tag-input/tag-input.component.ts`
- Modify: `web/src/app/shared/image-uploader/image-uploader.component.ts`
- Modify: `web/src/app/shared/dynamic-form/dynamic-form.component.spec.ts`

**Interfaces:**
- Adds item-editor panels `[data-item-core]`, `[data-item-schema]`, `[data-item-acquisition]`.
- Preserves save eligibility, category changes, metadata fetch, tags, acquisition mapping, and image events.
- Preserves the `initialAttributes` versus live `attributes` boundary.

- [ ] **Step 1: Write the failing item editor structure test**

Create `item-detail.component.spec.ts` with empty service responses:

```typescript
import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { IngestionService } from '../../core/api/ingestion.service';
import { NotificationService } from '../../core/notification.service';
import { ItemDetailComponent } from './item-detail.component';

describe('ItemDetailComponent', () => {
  it('groups a new item into terminal panels', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-item-core]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.detail__fetch')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeTruthy();
  });
});
```

Add to `dynamic-form.component.spec.ts`:

```typescript
it('labels the generated form as a schema field matrix', () => {
  render([field({ key: 'brand' })]);

  expect(fixture.nativeElement.querySelector('[data-schema-fields]')).toBeTruthy();
});
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/item-detail/item-detail.component.spec.ts --include src/app/shared/dynamic-form/dynamic-form.component.spec.ts
```

Expected: the item core and schema field hooks do not exist.

- [ ] **Step 3: Group the item editor into focused panels**

Keep the single outer `<form>`. Add the following opening and closing wrappers around the exact existing control groups:

```html
<form class="detail" (ngSubmit)="save()">
  <header class="detail__header">
    <div>
      <div class="mc-eyebrow">OBJECT EDITOR</div>
      <h1>{{ itemId() ? '編輯品項' : '新增品項' }}</h1>
    </div>
    <div class="detail__actions">
      <button type="submit" [disabled]="!canSave()">儲存</button>
      @if (itemId()) {
        <button type="button" class="button--danger" (click)="remove()">刪除</button>
      }
    </div>
  </header>

  @if (!itemId()) {
    <fieldset class="detail__fetch mc-panel">
      <legend>從商品網址自動填表</legend>
      <input type="url" [(ngModel)]="fetchUrl" name="fetchUrl" placeholder="https://…" />
      <button type="button" (click)="fetchMetadata()" [disabled]="!fetchUrl">擷取</button>
    </fieldset>
  }

  <section class="detail__panel mc-panel" data-item-core>
    <div class="mc-eyebrow">CORE METADATA</div>
    <label>
      品類
      <select [(ngModel)]="categoryId" name="categoryId"
              (ngModelChange)="onCategoryChanged()" required>
        <option value="">請選擇</option>
        @for (category of categories(); track category.id) {
          <option [value]="category.id">{{ category.name }}</option>
        }
      </select>
    </label>
    <label>名稱<input [(ngModel)]="name" name="name" required /></label>
    <label>描述<textarea [(ngModel)]="description" name="description" rows="3"></textarea></label>
    <label class="detail__checkbox">
      <input type="checkbox" [(ngModel)]="isShowcased" name="isShowcased" />
      設為精選（顯示在首頁牆面）
    </label>
    <app-tag-input [tags]="tags()" (tagsChange)="tags.set($event)" />
  </section>

  @if (selectedCategory(); as category) {
    @if (category.fields.length) {
      <section class="detail__panel mc-panel" data-item-schema>
        <h2>{{ category.name }} 專屬欄位</h2>
        <app-dynamic-form
          [fields]="category.fields"
          [value]="initialAttributes()"
          (valueChange)="attributes.set($event)"
          (validityChange)="attributesValid.set($event)"
        />
      </section>
    }
    @if (category.kind === 'Physical') {
      <fieldset class="detail__acquisition mc-panel" data-item-acquisition>
        <legend>購入資訊</legend>
        <label>日期<input type="date" [(ngModel)]="acquiredAt" name="acquiredAt" /></label>
        <label>金額<input type="number" [(ngModel)]="price" name="price" /></label>
        <label>幣別<input [(ngModel)]="currency" name="currency" /></label>
        <label>通路<input [(ngModel)]="vendor" name="vendor" /></label>
      </fieldset>
    }
  }

  @if (itemId(); as id) {
    <section class="detail__panel mc-panel">
      <h2>圖片</h2>
      <app-image-uploader
        [images]="item()?.images ?? []"
        (upload)="uploadImages(id, $event)"
        (remove)="removeImage(id, $event)"
        (setPrimary)="setPrimaryImage(id, $event)"
      />
    </section>
  }
</form>
```

Do not change the `app-dynamic-form` inputs and outputs.

- [ ] **Step 4: Apply shared control hooks and styles**

Change the dynamic form root:

```html
<form [formGroup]="form" class="dynamic-form" data-schema-fields>
```

Use tokens for the dynamic form required marker and errors. Replace the legacy component colors with these component-level declarations while retaining each component's current event bindings and methods:

```css
/* TagInputComponent */
.tags { display: flex; flex-wrap: wrap; gap: 0.4rem; align-items: center;
  border: 1px solid var(--mc-border); padding: 0.45rem; background: #07101a; }
.tags__chip { display: inline-flex; align-items: center; gap: 0.25rem;
  border: 1px solid var(--mc-cyan); padding-left: 0.5rem; color: var(--mc-cyan); }
.tags__chip button { min-width: 44px; min-height: 44px; border: 0; padding: 0; background: transparent; }
.tags input { flex: 1; min-width: 9rem; border: 0 !important; outline: 0; }

/* ImageUploaderComponent */
.uploader { display: grid; gap: 0.8rem; }
.uploader__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(130px, 1fr)); gap: 0.7rem; }
.uploader__item { margin: 0; border: 1px solid var(--mc-border); padding: 0.4rem; background: var(--mc-surface); }
.uploader__item img { width: 100%; aspect-ratio: 1; object-fit: cover; }
.uploader__item--primary { border-color: var(--mc-warning); }
.uploader__drop { display: grid; place-items: center; min-height: 8rem; border: 1px dashed var(--mc-cyan);
  padding: 1rem; background: var(--mc-cyan-soft); color: var(--mc-cyan); cursor: pointer; }
```

- [ ] **Step 5: Run focused and existing shared tests**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/item-detail/item-detail.component.spec.ts --include src/app/shared/dynamic-form/dynamic-form.component.spec.ts --include src/app/shared/item-card/item-card.component.spec.ts
```

Expected: all tests pass, including all dynamic value coercion and rebuilding tests.

- [ ] **Step 6: Commit Task 3**

```powershell
git add web/src/app/features/item-detail/item-detail.component.ts web/src/app/features/item-detail/item-detail.component.spec.ts web/src/app/shared/dynamic-form/dynamic-form.component.ts web/src/app/shared/dynamic-form/dynamic-form.component.spec.ts web/src/app/shared/tag-input/tag-input.component.ts web/src/app/shared/image-uploader/image-uploader.component.ts
git commit -m "feat(web): redesign item editing workflow"
```

### Task 4: Category Management and System Read-Only Presentation

**Files:**
- Modify: `web/src/app/features/categories/categories.component.ts`
- Create: `web/src/app/features/categories/categories.component.spec.ts`

**Interfaces:**
- System categories render with `[data-system-category]` and no edit button.
- Custom categories render with `[data-custom-category]` and remain editable.
- Preserves create, field editing, save, and delete behavior for custom categories.

- [ ] **Step 1: Write the failing read-only presentation test**

Create `categories.component.spec.ts`:

```typescript
import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { CategoryService } from '../../core/api/category.service';
import { NotificationService } from '../../core/notification.service';
import { CategoriesComponent } from './categories.component';

describe('CategoriesComponent', () => {
  it('renders system categories as read-only and custom categories as editable', async () => {
    await TestBed.configureTestingModule({
      imports: [CategoriesComponent],
      providers: [
        {
          provide: CategoryService,
          useValue: {
            list: () => of([
              { id: 's1', name: '實體遊戲', icon: 'gamepad-2', kind: 'Physical', isSystem: true, fields: [] },
              { id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false, fields: [] },
            ]),
          },
        },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CategoriesComponent);
    fixture.detectChanges();

    const system = fixture.nativeElement.querySelector('[data-system-category]');
    const custom = fixture.nativeElement.querySelector('[data-custom-category]');

    expect(system.textContent).toContain('唯讀');
    expect(system.querySelector('button')).toBeNull();
    expect(custom.querySelector('button')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/categories/categories.component.spec.ts
```

Expected: the system/custom hooks and explicit read-only presentation do not exist.

- [ ] **Step 3: Redesign the category page**

Use:

```html
<header class="categories__header">
  <div>
    <div class="mc-eyebrow">SCHEMA REGISTRY</div>
    <h1>品類</h1>
    <p class="mc-muted">系統品類提供常用欄位；自訂品類可建立自己的 schema。</p>
  </div>
  <button type="button" class="button--primary" (click)="startNew()">新增品類</button>
</header>

<ul class="categories">
  @for (category of categories(); track category.id) {
    <li class="category-row mc-panel"
        [attr.data-system-category]="category.isSystem ? '' : null"
        [attr.data-custom-category]="category.isSystem ? null : ''">
      <div>
        <span class="category-row__icon">{{ category.icon }}</span>
        <strong>{{ category.name }}</strong>
        <small>{{ category.kind === 'Physical' ? '實體' : '數位' }} · {{ category.fields.length }} 欄位</small>
      </div>
      @if (category.isSystem) {
        <span class="mc-badge">SYSTEM / 唯讀</span>
      } @else {
        <button type="button" (click)="edit(category)">編輯 schema</button>
      }
    </li>
  }
</ul>
```

Keep the existing editor bindings. Wrap the editor in `mc-panel`, render each field as a grid row at desktop and a stacked row on mobile, and add `searchable` to the visible checkbox controls because it is part of `CategoryFieldDto` and currently cannot be edited:

```html
<label>
  <input type="checkbox" [(ngModel)]="field.searchable" [name]="'searchable' + $index" />
  可搜尋
</label>
```

The delete button must use `button--danger`. Although `edit()` retains its system guard as defense in depth, the template must not offer a system edit button.

- [ ] **Step 4: Run the category test and verify GREEN**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/categories/categories.component.spec.ts
```

Expected: 1 passed, 0 failed.

- [ ] **Step 5: Commit Task 4**

```powershell
git add web/src/app/features/categories/categories.component.ts web/src/app/features/categories/categories.component.spec.ts
git commit -m "feat(web): redesign category schema registry"
```

### Task 5: Settings and Public Share Screens

**Files:**
- Modify: `web/src/app/features/settings/settings.component.ts`
- Create: `web/src/app/features/settings/settings.component.spec.ts`
- Modify: `web/src/app/features/public/public-share.component.ts`
- Create: `web/src/app/features/public/public-share.component.spec.ts`

**Interfaces:**
- Settings sections expose `[data-settings-panel]`.
- Sync statuses retain visible text and add stable status classes.
- Public share exposes `[data-public-terminal]`.
- Preserves linking, syncing, sharing, and public error behavior.

- [ ] **Step 1: Write failing screen contract tests**

Create `settings.component.spec.ts`:

```typescript
import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { IngestionService } from '../../core/api/ingestion.service';
import { ShareService } from '../../core/api/share.service';
import { NotificationService } from '../../core/notification.service';
import { SettingsComponent } from './settings.component';

describe('SettingsComponent', () => {
  it('renders account, sync, and sharing terminal panels', async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), jobs: () => of([]) },
        },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-settings-panel]').length).toBe(3);
  });
});
```

Create `public-share.component.spec.ts`:

```typescript
import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { ShareService } from '../../core/api/share.service';
import { PublicShareComponent } from './public-share.component';

describe('PublicShareComponent', () => {
  it('renders the public archive terminal and item count', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicShareComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'demo' } } },
        },
        {
          provide: ShareService,
          useValue: {
            getPublic: () => of({
              ownerDisplayName: 'Adam',
              scope: 'Showcase',
              items: [],
            }),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PublicShareComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-public-terminal]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('0 件');
  });
});
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/settings/settings.component.spec.ts --include src/app/features/public/public-share.component.spec.ts
```

Expected: terminal panel hooks are missing.

- [ ] **Step 3: Redesign settings**

Add a page header with `mc-eyebrow`, then apply `class="settings__panel mc-panel"` and `data-settings-panel` to exactly three sections: Steam account, sync history, and share links.

Keep every existing binding and handler. Use:

```html
<td>
  <span class="sync-status"
        [class.sync-status--ok]="job.status === 'Succeeded'"
        [class.sync-status--error]="job.status === 'Failed'">
    {{ job.status }}
  </span>
</td>
```

Retain the error detail in an accessible adjacent element or `aria-label`; do not rely only on the current `title`. Wrap the table in `.settings__table-scroll { overflow-x: auto; }` so the page itself never overflows on mobile.

- [ ] **Step 4: Redesign public sharing**

Add `data-public-terminal` to the successful public `<main>`, use an eyebrow and count in the header, and style cards with the same clipped-corner language as private item cards. Add an initial-letter placeholder when no public image exists:

```html
@if (imageUrl(item.images); as url) {
  <img [src]="url" [alt]="item.name" loading="lazy" />
} @else {
  <div class="public__placeholder" aria-hidden="true">{{ item.name.charAt(0) }}</div>
}
```

Render not-found state in `<main class="public public--error mc-panel">` with a visible `ERROR / SHARE UNAVAILABLE` label. Keep the existing `notFound` signal and service error callback.

- [ ] **Step 5: Run focused tests and verify GREEN**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless --include src/app/features/settings/settings.component.spec.ts --include src/app/features/public/public-share.component.spec.ts
```

Expected: all tests pass.

- [ ] **Step 6: Commit Task 5**

```powershell
git add web/src/app/features/settings/settings.component.ts web/src/app/features/settings/settings.component.spec.ts web/src/app/features/public/public-share.component.ts web/src/app/features/public/public-share.component.spec.ts
git commit -m "feat(web): redesign settings and public archive"
```

### Task 6: Full Frontend Verification and Visual QA

**Files:**
- Modify only files from Tasks 1–5 if verification exposes a defect.
- Do not create production-only debug routes or fixtures.

**Interfaces:**
- Verifies all Angular behavior, production compilation, desktop/mobile layout, focus, and reduced motion.

- [ ] **Step 1: Run the complete Angular test suite**

```powershell
cd web
npm test -- --watch=false --browsers=ChromeHeadless
```

Expected: all tests pass with 0 failures.

- [ ] **Step 2: Run the production build**

```powershell
npm run build
```

Expected: exit code 0 with no Angular template warnings or CSS budget failures.

- [ ] **Step 3: Start or reuse the local frontend and inspect desktop**

Run, if the user does not already have Angular dev server running:

```powershell
npm start
```

Use the in-app browser at `http://localhost:4200`. At a desktop viewport around 1440×900, verify:

1. `/login`: terminal centered; form labels and focus visible.
2. `/`: header, total count, empty state or card wall; no clipped text.
3. `/catalog`: sticky filter panel, result header, item cards, dynamic filters.
4. `/items/new`: URL fetch, core fields, schema fields, acquisition layout.
5. `/categories`: four system rows visibly read-only; custom editor remains usable.
6. `/settings`: three panels; table and share links readable.
7. `/p/{valid-slug}` when available: public archive has no authenticated navigation.

- [ ] **Step 4: Inspect mobile and reduced motion**

At a viewport around 390×844:

- Navigation remains reachable and does not overflow the page.
- Catalog filters stack above results.
- Item editor panels and acquisition fields use one column.
- Category editor field rows stack without clipped controls.
- Settings table scrolls inside its own wrapper.
- Public cards use one column or a readable narrow grid.

Emulate `prefers-reduced-motion: reduce` and confirm hover/transition effects stop or become effectively immediate.

- [ ] **Step 5: Verify keyboard focus**

Use Tab and Shift+Tab through login, navigation, catalog filters, item editor, categories, and settings. Every focused link, button, input, select, textarea, and checkbox must have a visible cyan focus indicator.

- [ ] **Step 6: Run final automated verification after visual fixes**

```powershell
npm test -- --watch=false --browsers=ChromeHeadless
npm run build
git diff --check
git status --short
```

Expected:

- Tests: 0 failures.
- Build: exit code 0.
- `git diff --check`: no output.
- `web/angular.json` remains a separate user-owned modification.
- `.superpowers/` is not staged.

- [ ] **Step 7: Commit visual QA fixes only if files changed**

If QA required code changes:

```powershell
git add web/src/styles.css web/src/app
git commit -m "fix(web): polish neon grid responsive layout"
```

Do not create an empty commit when no fix was required.
