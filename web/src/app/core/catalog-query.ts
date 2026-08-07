import { Params } from '@angular/router';

/** 庫存列表上同時生效的一組篩選條件。真實來源是網址的 query param。 */
export interface CatalogQuery {
  search: string;
  categoryId: string;
  tags: string[];
  attributes: Record<string, string>;
  missingAttributes: string[];
}

export const EMPTY_CATALOG_QUERY: CatalogQuery = {
  search: '',
  categoryId: '',
  tags: [],
  attributes: {},
  missingAttributes: [],
};

/**
 * 前綴只切掉固定長度，不可 `split('.')`——attribute 的 key 由品類宣告、可能含 `.`，
 * 後端 `ItemEndpoints` 用的是 `kv.Key[5..]`，兩端必須是同一條規則。
 */
const ATTRIBUTE_PREFIX = 'attr.';

/**
 * query param 的值是 `string | string[]`（重複 key 給陣列、單一 key 給字串），
 * 而網址是使用者可以隨手亂打的東西。每個值都要先收斂成確定形狀才進元件，
 * 比照 `showcase-view.ts` 對 `?view=` 的立場：壞值不讓它壞掉畫面。
 */
function single(value: unknown): string {
  if (Array.isArray(value)) {
    return typeof value[0] === 'string' ? value[0] : '';
  }

  return typeof value === 'string' ? value : '';
}

function multiple(value: unknown): string[] {
  const values = Array.isArray(value) ? value : [value];

  return values.filter((v): v is string => typeof v === 'string' && v !== '');
}

export function parseCatalogQuery(params: Params): CatalogQuery {
  const attributes: Record<string, string> = {};

  for (const [key, value] of Object.entries(params)) {
    if (!key.startsWith(ATTRIBUTE_PREFIX) || key.length === ATTRIBUTE_PREFIX.length) {
      continue;
    }

    // 空字串在前後端都是「不篩選」，讓它留在條件裡只會讓網址與「有無生效」的判斷說謊。
    const attributeValue = single(value);
    if (attributeValue !== '') {
      attributes[key.slice(ATTRIBUTE_PREFIX.length)] = attributeValue;
    }
  }

  return {
    search: single(params['search']),
    categoryId: single(params['categoryId']),
    tags: multiple(params['tags']),
    attributes,
    missingAttributes: multiple(params['missingAttrs']),
  };
}

/** 只放有值的 key：空條件不該在網址上留下 `?search=` 這種噪音。 */
export function toCatalogQueryParams(query: CatalogQuery): Params {
  const params: Params = {};

  if (query.search) {
    params['search'] = query.search;
  }
  if (query.categoryId) {
    params['categoryId'] = query.categoryId;
  }
  if (query.tags.length) {
    params['tags'] = query.tags;
  }
  if (query.missingAttributes.length) {
    params['missingAttrs'] = query.missingAttributes;
  }

  for (const [key, value] of Object.entries(query.attributes)) {
    if (value) {
      params[ATTRIBUTE_PREFIX + key] = value;
    }
  }

  return params;
}

export function isEmptyCatalogQuery(query: CatalogQuery): boolean {
  return (
    query.search === '' &&
    query.categoryId === '' &&
    query.tags.length === 0 &&
    query.missingAttributes.length === 0 &&
    Object.keys(query.attributes).length === 0
  );
}

/**
 * 給返回點比對用的正規化字串。刻意不直接比 `router.url`——同一組條件在網址上的
 * 排列順序不保證一致（使用者可以手打），排序過的鍵值對才問得出「是不是同一個列表」。
 *
 * 用 JSON 而不是 `key=value&…` 串接：條件的值是使用者自由輸入的字串，可能含
 * `&`、`=`、`,`。手工串接會讓 `{search: 'x&tags=RPG'}` 與 `{search: 'x', tags: ['RPG']}`
 * 壓成同一個字串，於是兩個不同的列表互相還原對方的頁數與錨點——這正是本機制
 * 唯一會默默給出錯誤結果的那條路。分隔符不可靠的理由與網址不用逗號串接完全相同。
 */
export function catalogQueryKey(query: CatalogQuery): string {
  const params = toCatalogQueryParams(query);

  const pairs = Object.keys(params)
    .sort()
    .map((key) => {
      const value = params[key];

      return [key, Array.isArray(value) ? [...value].sort() : value];
    });

  return JSON.stringify(pairs);
}
