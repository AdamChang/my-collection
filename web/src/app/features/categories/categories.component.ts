import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { CategoryService, CategoryWritePayload } from '../../core/api/category.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto, CategoryFieldDto, FieldType } from '../../core/models';

const FIELD_TYPES: FieldType[] = ['Text', 'Number', 'Date', 'Select', 'Bool', 'Url'];

@Component({
  selector: 'app-categories',
  imports: [FormsModule],
  template: `
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
        <li
          class="category-row mc-panel"
          [attr.data-system-category]="category.isSystem ? '' : null"
          [attr.data-custom-category]="category.isSystem ? null : ''"
        >
          <div class="category-row__summary">
            <span class="category-row__icon" aria-hidden="true">{{ category.icon }}</span>
            <div>
              <strong>{{ category.name }}</strong>
              <small>{{ category.kind === 'Physical' ? '實體' : '數位' }} · {{ category.fields.length }} 欄位</small>
            </div>
          </div>
          @if (category.isSystem) {
            <span class="mc-badge">SYSTEM / 唯讀</span>
          } @else {
            <button type="button" (click)="edit(category)">編輯 schema</button>
          }
        </li>
      }
    </ul>

    <dialog
      #editorDialog
      class="editor-dialog"
      data-category-editor
      aria-labelledby="category-editor-title"
      (cancel)="onCancelKey($event)"
      (close)="draft.set(null)"
    >
      @if (draft(); as current) {
        <form class="editor" (ngSubmit)="save()">
          <header class="editor__header">
            <h2 id="category-editor-title">{{ editingId() ? '編輯品類' : '新增品類' }}</h2>
            <button type="button" class="editor__close" aria-label="關閉" [disabled]="busy()" (click)="cancel()">
              ✕
            </button>
          </header>

          <div class="editor__body">
            <label>名稱<input [(ngModel)]="current.name" name="name" required /></label>
            <label>圖示<input [(ngModel)]="current.icon" name="icon" /></label>

            <label>
              類型
              <select [(ngModel)]="current.kind" name="kind">
                <option value="Physical">實體</option>
                <option value="Digital">數位</option>
              </select>
            </label>

            <label>
              精選牆預設展示模式
              <select [(ngModel)]="current.defaultDisplayMode" name="defaultDisplayMode">
                <option value="List">List（清單／網格）</option>
                <option value="Hero">Hero（焦點展品）</option>
                <option value="Stats">Stats（數據看板）</option>
              </select>
            </label>

            <h3>欄位</h3>
            @for (field of current.fields; track $index) {
              <fieldset class="editor__field">
                <legend>欄位 {{ $index + 1 }}</legend>
                <input
                  [(ngModel)]="field.key"
                  [name]="'key' + $index"
                  [attr.aria-label]="'欄位 ' + ($index + 1) + ' key'"
                  placeholder="key（camelCase）"
                  required
                />
                <input
                  [(ngModel)]="field.label"
                  [name]="'label' + $index"
                  [attr.aria-label]="'欄位 ' + ($index + 1) + ' 顯示名稱'"
                  placeholder="顯示名稱"
                  required
                />

                <select
                  [(ngModel)]="field.type"
                  [name]="'type' + $index"
                  [attr.aria-label]="'欄位 ' + ($index + 1) + ' 類型'"
                >
                  @for (type of fieldTypes; track type) {
                    <option [value]="type">{{ type }}</option>
                  }
                </select>

                @if (field.type === 'Select') {
                  <input
                    [ngModel]="(field.options ?? []).join(',')"
                    (ngModelChange)="setOptions(field, $event)"
                    [name]="'options' + $index"
                    [attr.aria-label]="'欄位 ' + ($index + 1) + ' 選項'"
                    placeholder="選項，以逗號分隔"
                  />
                }

                <label><input type="checkbox" [(ngModel)]="field.required" [name]="'required' + $index" /> 必填</label>
                <label><input type="checkbox" [(ngModel)]="field.searchable" [name]="'searchable' + $index" /> 可搜尋</label>
                <label><input type="checkbox" [(ngModel)]="field.showOnCard" [name]="'card' + $index" /> 顯示於卡片</label>

                <button type="button" (click)="removeField($index)">移除</button>
              </fieldset>
            }

            <button type="button" (click)="addField()">新增欄位</button>
          </div>

          <footer class="editor__actions">
            <button type="submit" class="button--primary" [disabled]="busy()">
              {{ saving() ? '儲存中…' : '儲存' }}
            </button>
            <button type="button" data-category-editor-cancel [disabled]="busy()" (click)="cancel()">取消</button>
            @if (editingId()) {
              <button type="button" class="button--danger" [disabled]="busy()" (click)="remove()">
                {{ removing() ? '刪除中…' : '刪除品類' }}
              </button>
            }
          </footer>
        </form>
      }
    </dialog>
  `,
  styles: `
    .categories__header { display: flex; justify-content: space-between; gap: 1rem; align-items: center; margin-bottom: 1.5rem; }
    .categories__header h1 { margin: 0.25rem 0; }
    .categories__header p { margin: 0; }
    .categories { list-style: none; padding: 0; display: grid; gap: 0.75rem; }
    .category-row { display: flex; justify-content: space-between; gap: 1rem; align-items: center; }
    .category-row__summary { display: flex; gap: 0.75rem; align-items: center; }
    .category-row__summary strong, .category-row__summary small { display: block; }
    .category-row__summary small { margin-top: 0.2rem; color: var(--mc-text-muted); }
    .category-row__icon { color: var(--mc-cyan); font-family: Consolas, monospace; }
    .editor-dialog {
      width: min(52rem, 92vw);
      max-height: min(85vh, 52rem);
      padding: 0;
      border: 1px solid var(--mc-border-strong);
      background: var(--mc-surface);
      color: inherit;
      box-shadow: var(--mc-shadow);
      overflow: hidden;
    }
    .editor-dialog::backdrop { background: rgb(0 0 0 / 0.65); }
    .editor { display: grid; grid-template-rows: auto minmax(0, 1fr) auto; max-height: min(85vh, 52rem); }
    .editor__header { display: flex; justify-content: space-between; gap: 1rem; align-items: center; padding: 1rem clamp(1rem, 2.5vw, 1.5rem); border-bottom: 1px solid var(--mc-border); }
    .editor__header h2 { margin: 0; font-size: 1.05rem; }
    .editor__close { min-block-size: auto; padding: 0.3rem 0.6rem; clip-path: none; }
    .editor__body { display: grid; gap: 0.75rem; align-content: start; overflow-y: auto; padding: clamp(1rem, 2.5vw, 1.5rem); }
    .editor__body > label { display: grid; gap: 0.25rem; }
    .editor__field { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.5rem; align-items: center; }
    .editor__field legend { grid-column: 1 / -1; }
    .editor__field label { display: flex; gap: 0.35rem; align-items: center; }
    .editor__actions { display: flex; gap: 0.5rem; padding: 1rem clamp(1rem, 2.5vw, 1.5rem); border-top: 1px solid var(--mc-border); background: var(--mc-surface-raised); }
    @media (max-width: 42rem) {
      .categories__header, .category-row { align-items: stretch; flex-direction: column; }
      .editor__field { grid-template-columns: 1fr; }
      .editor__actions { flex-wrap: wrap; }
    }
  `,
})
export class CategoriesComponent {
  private readonly api = inject(CategoryService);
  private readonly notifications = inject(NotificationService);

  private readonly editorDialog = viewChild.required<ElementRef<HTMLDialogElement>>('editorDialog');

  readonly fieldTypes = FIELD_TYPES;
  readonly categories = signal<CategoryDto[]>([]);
  readonly draft = signal<CategoryWritePayload | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly saving = signal(false);
  readonly removing = signal(false);

  /** 儲存與刪除不該並行，任一進行中就鎖住兩顆。 */
  readonly busy = computed(() => this.saving() || this.removing());

  constructor() {
    this.reload();
  }

  startNew(): void {
    this.editingId.set(null);
    this.draft.set({ name: '', icon: 'box', kind: 'Physical', defaultDisplayMode: 'List', fields: [] });
    this.openEditor();
  }

  edit(category: CategoryDto): void {
    if (category.isSystem) {
      this.notifications.error('系統內建品類無法編輯。');
      return;
    }

    this.editingId.set(category.id);
    this.draft.set({
      name: category.name,
      icon: category.icon,
      kind: category.kind,
      defaultDisplayMode: category.defaultDisplayMode,
      fields: category.fields.map((f) => ({ ...f, options: f.options ? [...f.options] : null })),
    });
    this.openEditor();
  }

  /**
   * 表單走 modal 而不是頁面下方的區塊：以前按下「編輯 schema」時表單長在視窗外，
   * 使用者看不到任何變化，會當成按鈕壞了。showModal() 進 top layer 就一定在視野正中央。
   */
  private openEditor(): void {
    const dialog = this.editorDialog().nativeElement;
    if (!dialog.open) {
      dialog.showModal();
    }
  }

  /** 儲存／刪除進行中就不讓使用者關掉——關掉會連同 draft 一起清空，回來的結果將無處可去。 */
  cancel(): void {
    if (this.busy()) {
      return;
    }

    this.dismiss();
  }

  /** Esc 由瀏覽器直接關閉對話框，元件唯一能擋下的時機就是 cancel 事件。 */
  protected onCancelKey(event: Event): void {
    if (this.busy()) {
      event.preventDefault();
    }
  }

  /**
   * 這裡同步清掉 draft，不能只靠模板上的 (close)：瀏覽器把 close 事件排進獨立的任務佇列，
   * 呼叫端回到下一行時狀態還是舊的。(close) 留著是為了 Esc——那條路徑元件收不到別的通知。
   */
  private dismiss(): void {
    this.draft.set(null);

    const dialog = this.editorDialog().nativeElement;
    if (dialog.open) {
      dialog.close();
    }
  }

  addField(): void {
    this.draft.update((current) =>
      current
        ? {
            ...current,
            fields: [
              ...current.fields,
              { key: '', label: '', type: 'Text', options: null, required: false, searchable: false, showOnCard: false },
            ],
          }
        : current,
    );
  }

  removeField(index: number): void {
    this.draft.update((current) =>
      current ? { ...current, fields: current.fields.filter((_, i) => i !== index) } : current,
    );
  }

  setOptions(field: CategoryFieldDto, raw: string): void {
    field.options = raw
      .split(',')
      .map((o) => o.trim())
      .filter((o) => o.length > 0);
  }

  save(): void {
    const payload = this.draft();
    if (!payload || this.busy()) {
      return;
    }

    const id = this.editingId();
    const request = id ? this.api.update(id, payload) : this.api.create(payload);

    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success('已儲存品類。');
        this.dismiss();
        this.reload();
      },
      error: IGNORE_HANDLED_BY_INTERCEPTOR,
    });
  }

  remove(): void {
    const id = this.editingId();
    if (!id || this.busy()) {
      return;
    }

    this.removing.set(true);
    this.api
      .remove(id)
      .pipe(finalize(() => this.removing.set(false)))
      .subscribe({
        next: () => {
          this.notifications.success('已刪除品類。');
          this.draft.set(null);
          this.reload();
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  private reload(): void {
    this.api.list().subscribe((categories) => this.categories.set(categories));
  }
}
