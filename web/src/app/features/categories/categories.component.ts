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
    <h1>品類</h1>

    <ul class="categories">
      @for (category of categories(); track category.id) {
        <li>
          <button type="button" (click)="edit(category)">{{ category.name }}</button>
          @if (category.isSystem) { <em>系統內建</em> }
        </li>
      }
    </ul>

    <button type="button" (click)="startNew()">新增品類</button>

    @if (draft(); as current) {
      <form class="editor" (ngSubmit)="save()">
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
            <input [(ngModel)]="field.key" [name]="'key' + $index" placeholder="key（camelCase）" required />
            <input [(ngModel)]="field.label" [name]="'label' + $index" placeholder="顯示名稱" required />

            <select [(ngModel)]="field.type" [name]="'type' + $index">
              @for (type of fieldTypes; track type) {
                <option [value]="type">{{ type }}</option>
              }
            </select>

            @if (field.type === 'Select') {
              <input
                [ngModel]="(field.options ?? []).join(',')"
                (ngModelChange)="setOptions(field, $event)"
                [name]="'options' + $index"
                placeholder="選項，以逗號分隔"
              />
            }

            <label><input type="checkbox" [(ngModel)]="field.required" [name]="'required' + $index" /> 必填</label>
            <label><input type="checkbox" [(ngModel)]="field.showOnCard" [name]="'card' + $index" /> 顯示於卡片</label>

            <button type="button" (click)="removeField($index)">移除</button>
          </fieldset>
        }

        <button type="button" (click)="addField()">新增欄位</button>

        <div class="editor__actions">
          <button type="submit">儲存</button>
          <button type="button" (click)="draft.set(null)">取消</button>
          @if (editingId()) {
            <button type="button" (click)="remove()">刪除品類</button>
          }
        </div>
      </form>
    }
  `,
  styles: `
    .categories { list-style: none; padding: 0; display: grid; gap: 0.35rem; }
    .editor { display: grid; gap: 0.75rem; max-width: 42rem; margin-top: 1.5rem; }
    .editor__field { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .editor__actions { display: flex; gap: 0.5rem; }
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
