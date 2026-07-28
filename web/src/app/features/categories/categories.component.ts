import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CategoryService, CategoryWritePayload } from '../../core/api/category.service';
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

    @if (draft(); as current) {
      <form class="editor mc-panel" (ngSubmit)="save()">
        <h2>{{ editingId() ? '編輯品類' : '新增品類' }}</h2>

        <label>名稱<input [(ngModel)]="current.name" name="name" required /></label>
        <label>圖示<input [(ngModel)]="current.icon" name="icon" /></label>

        <label>
          類型
          <select [(ngModel)]="current.kind" name="kind">
            <option value="Physical">實體</option>
            <option value="Digital">數位</option>
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

            <select [(ngModel)]="field.type" [name]="'type' + $index" [attr.aria-label]="'欄位 ' + ($index + 1) + ' 類型'">
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

        <div class="editor__actions">
          <button type="submit" class="button--primary">儲存</button>
          <button type="button" (click)="draft.set(null)">取消</button>
          @if (editingId()) {
            <button type="button" class="button--danger" (click)="remove()">刪除品類</button>
          }
        </div>
      </form>
    }
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
    .editor { display: grid; gap: 0.75rem; max-width: 52rem; margin-top: 1.5rem; }
    .editor > label { display: grid; gap: 0.25rem; }
    .editor__field { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.5rem; align-items: center; }
    .editor__field legend { grid-column: 1 / -1; }
    .editor__field label { display: flex; gap: 0.35rem; align-items: center; }
    .editor__actions { display: flex; gap: 0.5rem; }
    @media (max-width: 42rem) {
      .categories__header, .category-row { align-items: stretch; flex-direction: column; }
      .editor__field { grid-template-columns: 1fr; }
    }
  `,
})
export class CategoriesComponent {
  private readonly api = inject(CategoryService);
  private readonly notifications = inject(NotificationService);

  readonly fieldTypes = FIELD_TYPES;
  readonly categories = signal<CategoryDto[]>([]);
  readonly draft = signal<CategoryWritePayload | null>(null);
  readonly editingId = signal<string | null>(null);

  constructor() {
    this.reload();
  }

  startNew(): void {
    this.editingId.set(null);
    this.draft.set({ name: '', icon: 'box', kind: 'Physical', fields: [] });
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
      fields: category.fields.map((f) => ({ ...f, options: f.options ? [...f.options] : null })),
    });
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
    if (!payload) {
      return;
    }

    const id = this.editingId();
    const request = id ? this.api.update(id, payload) : this.api.create(payload);

    request.subscribe(() => {
      this.notifications.success('已儲存品類。');
      this.draft.set(null);
      this.reload();
    });
  }

  remove(): void {
    const id = this.editingId();
    if (!id) {
      return;
    }

    this.api.remove(id).subscribe(() => {
      this.notifications.success('已刪除品類。');
      this.draft.set(null);
      this.reload();
    });
  }

  private reload(): void {
    this.api.list().subscribe((categories) => this.categories.set(categories));
  }
}
