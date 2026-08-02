import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { CatalogService, ItemWritePayload } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY, ProviderService } from '../../core/api/provider.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto, FetchedMetadataDto, ItemDto } from '../../core/models';
import { DynamicFormComponent } from '../../shared/dynamic-form/dynamic-form.component';
import { IgdbSearchDialogComponent } from '../../shared/igdb-search-dialog/igdb-search-dialog.component';
import { ImageUploaderComponent } from '../../shared/image-uploader/image-uploader.component';
import { TagInputComponent } from '../../shared/tag-input/tag-input.component';

@Component({
  selector: 'app-item-detail',
  imports: [
    FormsModule,
    DynamicFormComponent,
    IgdbSearchDialogComponent,
    ImageUploaderComponent,
    TagInputComponent,
  ],
  template: `
    <form class="detail" (ngSubmit)="save()">
      <header class="detail__header">
        <div>
          <div class="mc-eyebrow">OBJECT EDITOR</div>
          <h1>{{ itemId() ? '編輯品項' : '新增品項' }}</h1>
        </div>
        <div class="detail__actions">
          <button type="submit" [disabled]="!canSave() || busy()">
            {{ saving() ? '儲存中…' : '儲存' }}
          </button>
          @if (itemId()) {
            <button type="button" class="button--danger" [disabled]="busy()" (click)="remove()">
              {{ removing() ? '刪除中…' : '刪除' }}
            </button>
          }
        </div>
      </header>

      @if (!itemId()) {
        <fieldset class="detail__fetch mc-panel">
          <legend>從商品網址自動填表</legend>
          <input type="url" [(ngModel)]="fetchUrl" name="fetchUrl" placeholder="https://…" />
          <button type="button" (click)="fetchMetadata()" [disabled]="!fetchUrl || busy()">
            {{ fetching() ? '擷取中…' : '擷取' }}
          </button>
        </fieldset>

        @if (igdbAvailable()) {
          <div class="detail__igdb mc-panel">
            <button
              type="button"
              (click)="openIgdbSearch()"
              [disabled]="!categoryId || busy()"
              [title]="categoryId ? '' : '請先選擇品類'"
              data-igdb-open
            >
              從 IGDB 搜尋遊戲
            </button>
            <span class="hint">先選好上方的品類，搜尋結果才知道要填進哪些欄位。</span>
          </div>
        }
      }

      <section class="detail__panel mc-panel" data-item-core>
        <div class="mc-eyebrow">CORE METADATA</div>
        <label>
          品類
          <select [(ngModel)]="categoryId" name="categoryId" (ngModelChange)="onCategoryChanged()" required>
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

      @if (itemId() && igdbAvailable()) {
        <section class="detail__panel mc-panel" data-item-igdb>
          <div class="mc-eyebrow">IGDB</div>
          @if (igdbAddressable()) {
            <button type="button" (click)="refetchFromIgdb()" [disabled]="busy()" data-igdb-refetch>
              {{ enriching() ? '抓取中…' : '重新從 IGDB 抓取' }}
            </button>
          } @else {
            <button type="button" (click)="openIgdbSearch()" [disabled]="busy()" data-igdb-bind>
              從 IGDB 搜尋並綁定
            </button>
            <span class="hint">這筆品項還沒有對應的 IGDB 條目，綁定後才能自動更新。</span>
          }
        </section>
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

    <app-igdb-search-dialog (select)="applyMetadata($event, itemId() ? 'bind' : 'prefill')" />
  `,
  styles: `
    .detail { display: grid; gap: 1rem; max-width: 46rem; }
    .detail__header { display: flex; justify-content: space-between; align-items: center; }
    .detail__actions { display: flex; gap: 0.5rem; }
    .detail__panel { display: grid; gap: 1rem; }
    .detail label { display: grid; gap: 0.25rem; }
    .detail .hint { color: var(--mc-text-muted); font-size: 0.85rem; }
    .detail__checkbox { display: flex !important; gap: 0.5rem; align-items: center; }
    .detail__fetch { display: flex; gap: 0.5rem; align-items: center; }
    .detail__igdb { display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap; }
    .detail__acquisition { display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.5rem; }
    @media (max-width: 42rem) {
      .detail__fetch { display: grid; grid-template-columns: 1fr; align-items: stretch; }
      .detail__acquisition { grid-template-columns: 1fr; }
    }
  `,
})
export class ItemDetailComponent {
  private readonly catalog = inject(CatalogService);
  private readonly categoryApi = inject(CategoryService);
  private readonly ingestion = inject(IngestionService);
  private readonly providers = inject(ProviderService);
  private readonly notifications = inject(NotificationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly itemId = signal<string | null>(this.route.snapshot.paramMap.get('id'));
  readonly item = signal<ItemDto | null>(null);
  readonly categories = signal<CategoryDto[]>([]);
  readonly selectedCategory = signal<CategoryDto | null>(null);

  /**
   * 餵給 `app-dynamic-form` 的 `[value]`，也就是表單要重建成什麼樣子。
   * 三個寫入點：`hydrate()` 從後端載回、`onCategoryChanged()` 換品類清空、`applyMetadata()` 套用外部來源。
   * 不可改成 `attributes`：`DynamicFormComponent` 的 effect 同時讀 `fields()` 與 `value()`，
   * 若 `[value]` 綁到隨 `(valueChange)` 更新的狀態，每次打字都會重建整個 FormGroup。
   */
  readonly initialAttributes = signal<Record<string, unknown>>({});

  /** 使用者當下的表單值，只寫不讀進 `[value]`，送出時使用。 */
  readonly attributes = signal<Record<string, unknown>>({});
  readonly attributesValid = signal(true);
  readonly tags = signal<string[]>([]);

  readonly saving = signal(false);
  readonly removing = signal(false);
  readonly fetching = signal(false);
  readonly enriching = signal(false);

  /** 對話框在模板根、不在任何 `@if` 內，必定存在。required 讓「有人把它搬進 @if」直接爆而不是靜默沒反應。 */
  private readonly searchDialog = viewChild.required(IgdbSearchDialogComponent);

  /** IGDB 未設定時後端不會註冊它，整組入口不渲染。 */
  readonly igdbAvailable = computed(() => this.providers.supports(IGDB_PROVIDER_KEY, 'Search'));

  /**
   * 後端 ExternalIdFor 的規則：先看 marker，再退回 externalRef。
   * 必須檢查 provider === 'steam'——OpenGraph 品項也有 externalRef，
   * 但後端會組出 opengraph:xxx 這種 IGDB 兩個前綴都 parse 不了的識別碼，
   * 結果是進 failed（不是 skipped），而 job.Status 仍為 Succeeded。
   */
  readonly igdbAddressable = computed(() => {
    const item = this.item();

    return (
      item != null &&
      (item.attributes['igdbId'] != null || item.externalRef?.provider === 'steam')
    );
  });

  /** 任一改寫動作進行中就鎖住全部按鈕：同一筆品項不該有並行的改寫。 */
  readonly busy = computed(
    () => this.saving() || this.removing() || this.fetching() || this.enriching(),
  );

  categoryId = '';
  name = '';
  description = '';
  isShowcased = false;
  fetchUrl = '';
  acquiredAt = '';
  price: number | null = null;
  currency = 'TWD';
  vendor = '';

  constructor() {
    this.categoryApi.list().subscribe((categories) => {
      this.categories.set(categories);
      this.syncSelectedCategory();
    });

    const id = this.itemId();
    if (id) {
      this.catalog.get(id).subscribe((item) => this.hydrate(item));
    }
  }

  canSave(): boolean {
    return Boolean(this.categoryId) && this.name.trim().length > 0 && this.attributesValid();
  }

  onCategoryChanged(): void {
    this.syncSelectedCategory();

    // 換品類就清空屬性。表單重建本身不會觸發 valueChanges，所以在使用者實際輸入前，
    // attributes 仍是前一個品類的內容；此時存檔會送出新 schema 沒宣告的 key，
    // 後端 AttributeValidator 直接回 400。
    this.initialAttributes.set({});
    this.attributes.set({});
  }

  openIgdbSearch(): void {
    this.searchDialog().open();
  }

  fetchMetadata(): void {
    if (this.busy()) {
      return;
    }

    // 錯誤訊息由 errorInterceptor 顯示，這裡只負責解鎖讓使用者能換一個網址重試。
    this.fetching.set(true);
    this.ingestion
      .fetchByUrl(this.fetchUrl)
      .pipe(finalize(() => this.fetching.set(false)))
      .subscribe({
        next: (metadata) => this.applyMetadata(metadata, 'prefill'),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  /**
   * 兩個外部來源（OpenGraph 網址、IGDB 搜尋）共用的唯一套用路徑。
   *
   * prefill 用於新增品項——名稱與描述本來就是空的，覆寫沒有損失。
   * bind 用於既有品項——名稱是使用者在庫裡認得的那個，描述可能是他自己寫的心得，都不動。
   *
   * 屬性的政策刻意與 name/description 相反：兩個模式下外部值都蓋掉既有值。
   * 名稱與描述是使用者的話，屬性是遊戲的事實——綁定一個外部來源的目的就是刷新後者。
   * 展開順序（既有在前、外部在後）就是這條政策，反過來寫會靜默留著過期資料。
   */
  applyMetadata(metadata: FetchedMetadataDto, mode: 'prefill' | 'bind'): void {
    if (mode === 'prefill') {
      this.name = metadata.name;
      this.description = metadata.description ?? '';
    }

    const merged = { ...this.attributes(), ...this.declaredOnly(metadata.attributes) };

    this.initialAttributes.set(merged);
    this.attributes.set(merged);

    this.notifications.success('已帶入資料，請確認後儲存。');
  }

  refetchFromIgdb(): void {
    const id = this.itemId();

    if (!id || this.busy()) {
      return;
    }

    this.enriching.set(true);
    this.ingestion
      .enrich(IGDB_PROVIDER_KEY, [id])
      .pipe(finalize(() => this.enriching.set(false)))
      .subscribe({
        next: (job) => {
          // 誠實比樂觀重要：什麼都沒變時說「完成」，使用者會以為資料已更新。
          // failed 與 skipped 要分開講——一個是該重試，一個是重試也沒用。
          // 注意 job.Status 在這兩種情況下都還是 Succeeded（provider 內部接住了
          // ProviderException），所以 errorInterceptor 不會出手，只能靠這裡判斷。
          if (job.failed > 0) {
            this.notifications.error('IGDB 查詢失敗，請稍後再試。');
            return;
          }

          if (job.updated === 0) {
            this.notifications.error('IGDB 查無對應，未變更任何欄位。');
            return;
          }

          this.notifications.success('已從 IGDB 更新。');
          this.reloadItem(id);
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  /**
   * 品類沒宣告的 key 會被後端 AttributeValidator 擋掉，整筆儲存回 400。
   *
   * 不能倚賴 DynamicFormComponent 代為過濾：表單重建不會觸發 valueChanges，
   * 使用者若套用後直接儲存、中途沒編輯任何欄位，attributes 送出的就是這裡設進去的原值。
   *
   * 這條規則與後端 EnrichCommandHandler.ToEnrichment 是同一份政策，兩處要一起改。
   */
  private declaredOnly(source: Record<string, unknown>): Record<string, unknown> {
    const declared = new Set(this.selectedCategory()?.fields.map((f) => f.key) ?? []);

    return Object.fromEntries(Object.entries(source).filter(([key]) => declared.has(key)));
  }

  save(): void {
    if (this.busy()) {
      return;
    }

    const payload = this.toPayload();
    const id = this.itemId();

    const request = id ? this.catalog.update(id, payload) : this.catalog.create(payload);

    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        this.notifications.success('已儲存。');
        if (!id) {
          void this.router.navigate(['/items', saved.id]);
        } else {
          this.hydrate(saved);
        }
      },
      error: IGNORE_HANDLED_BY_INTERCEPTOR,
    });
  }

  remove(): void {
    const id = this.itemId();
    if (!id || this.busy()) {
      return;
    }

    this.removing.set(true);
    this.catalog
      .remove(id)
      .pipe(finalize(() => this.removing.set(false)))
      .subscribe({
        next: () => {
          this.notifications.success('已刪除。');
          void this.router.navigate(['/catalog']);
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  uploadImages(itemId: string, files: File[]): void {
    for (const file of files) {
      this.catalog.uploadImage(itemId, file).subscribe(() => this.reloadItem(itemId));
    }
  }

  removeImage(itemId: string, imageId: string): void {
    this.catalog.deleteImage(itemId, imageId).subscribe(() => this.reloadItem(itemId));
  }

  setPrimaryImage(itemId: string, imageId: string): void {
    this.catalog.setPrimaryImage(itemId, imageId).subscribe(() => this.reloadItem(itemId));
  }

  private reloadItem(itemId: string): void {
    this.catalog.get(itemId).subscribe((item) => this.hydrate(item));
  }

  private hydrate(item: ItemDto): void {
    this.item.set(item);
    this.itemId.set(item.id);
    this.categoryId = item.categoryId;
    this.name = item.name;
    this.description = item.description ?? '';
    this.isShowcased = item.isShowcased;
    this.tags.set(item.tags);
    this.initialAttributes.set(item.attributes);
    this.attributes.set(item.attributes);
    this.acquiredAt = item.acquisition?.acquiredAt?.slice(0, 10) ?? '';
    this.price = item.acquisition?.price?.amount ?? null;
    this.currency = item.acquisition?.price?.currency ?? 'TWD';
    this.vendor = item.acquisition?.vendor ?? '';
    this.syncSelectedCategory();
  }

  private syncSelectedCategory(): void {
    this.selectedCategory.set(this.categories().find((c) => c.id === this.categoryId) ?? null);
  }

  private toPayload(): ItemWritePayload {
    const hasAcquisition = Boolean(this.acquiredAt || this.price || this.vendor);

    return {
      categoryId: this.categoryId,
      name: this.name.trim(),
      description: this.description.trim() || null,
      tags: this.tags(),
      isShowcased: this.isShowcased,
      attributes: this.attributes(),
      // 讀是巢狀的 AcquisitionDto（price.amount / price.currency），寫是扁平的 AcquisitionInput。
      // 這裡的值在 hydrate() 已經從 item.acquisition?.price?.amount 明確取出攤平，不可用展開運算子。
      acquisition: hasAcquisition
        ? {
            acquiredAt: this.acquiredAt ? new Date(`${this.acquiredAt}T00:00:00Z`).toISOString() : null,
            amount: this.price,
            currency: this.currency || 'TWD',
            vendor: this.vendor || null,
          }
        : null,
    };
  }
}
