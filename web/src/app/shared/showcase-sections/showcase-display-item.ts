import { API_BASE } from '../../core/api-base';
import { CategoryDto, CategoryFieldDto, DisplayMode, ItemDto, PublicItemDto } from '../../core/models';

export interface ShowcaseCardAttribute {
  key: string;
  label: string;
  value: unknown;
}

/**
 * Hero / Stats / Collage 三個分區共用的顯示資料形狀，把內部 ItemDto 與公開頁 PublicItemDto
 * 的差異（storageLocation 只存在於內部、cardFields 來源不同）收斂成同一個介面，
 * 讓三個展示元件不用關心自己是被 showcase 還是 public-share 使用。
 */
export interface ShowcaseDisplayItem {
  id: string;
  name: string;
  description: string | null;
  imageUrl: string | null;
  effectiveDisplayMode: DisplayMode;
  acquiredAt: string | null;
  price: { amount: number; currency: string } | null;
  rating: number | null;
  storageLocation: string | null;
  attributes: Record<string, unknown>;
  cardAttributes: ShowcaseCardAttribute[];
}

function isPopulated(value: unknown): boolean {
  return value !== null && value !== undefined && value !== '';
}

/** 沿用 ItemCardComponent.cardAttributes 的規則：有 playtimeForever 時 psnProgress 交給 Stats 專屬的進度條顯示，不重複列出。 */
function cardAttributesFor(
  attributes: Record<string, unknown>,
  fields: CategoryFieldDto[],
): ShowcaseCardAttribute[] {
  const entries = fields
    .filter((f) => f.showOnCard)
    .map((f) => ({ key: f.key, label: f.label, value: attributes[f.key] }))
    .filter((entry) => isPopulated(entry.value));

  return entries.some((entry) => entry.key === 'playtimeForever')
    ? entries.filter((entry) => entry.key !== 'psnProgress')
    : entries;
}

/** 沿用 ItemCardComponent.imageUrl 的規則，Stats 用的遊戲視覺圖多一個 coverUrl 退回層。 */
function coverImageUrl(
  images: { cardPath: string; isPrimary: boolean }[],
  attributes: Record<string, unknown>,
  mediaBase = `${API_BASE}/media`,
): string | null {
  const primary = images.find((i) => i.isPrimary) ?? images[0];
  if (primary) {
    return `${mediaBase}/${primary.cardPath}`;
  }

  for (const key of ['headerUrl', 'coverUrl', 'iconUrl']) {
    const candidate = attributes[key];
    if (typeof candidate === 'string' && candidate.startsWith('http')) {
      return candidate;
    }
  }

  return null;
}

/** 內部頁（/showcase）使用：品類欄位定義要另外從 categories 查表。 */
export function toShowcaseDisplayItem(item: ItemDto, categories: CategoryDto[]): ShowcaseDisplayItem {
  const fields = categories.find((c) => c.id === item.categoryId)?.fields ?? [];

  return {
    id: item.id,
    name: item.name,
    description: item.description,
    imageUrl: coverImageUrl(item.images, item.attributes),
    effectiveDisplayMode: item.effectiveDisplayMode,
    acquiredAt: item.acquisition?.acquiredAt ?? null,
    price: item.acquisition?.price ?? null,
    rating: item.rating,
    storageLocation: item.storageLocation,
    attributes: item.attributes,
    cardAttributes: cardAttributesFor(item.attributes, fields),
  };
}

/** 公開分享頁（/p/:slug）使用：storageLocation 恆為 null（ADR-0008），cardFields 隨 PublicItemDto 附帶。 */
export function toPublicShowcaseDisplayItem(item: PublicItemDto, slug: string): ShowcaseDisplayItem {
  return {
    id: item.id,
    name: item.name,
    description: item.description,
    imageUrl: coverImageUrl(item.images, item.attributes, `${API_BASE}/public/${slug}/media`),
    effectiveDisplayMode: item.effectiveDisplayMode,
    acquiredAt: item.acquiredAt,
    price: item.price,
    rating: item.rating,
    storageLocation: null,
    attributes: item.attributes,
    cardAttributes: cardAttributesFor(item.attributes, item.cardFields),
  };
}
