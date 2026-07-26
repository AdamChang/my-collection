export interface UserDto {
  id: string;
  email: string;
  displayName: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: UserDto;
}

export type FieldType = 'Text' | 'Number' | 'Date' | 'Select' | 'Bool' | 'Url';

export interface CategoryFieldDto {
  key: string;
  label: string;
  type: FieldType;
  options: string[] | null;
  required: boolean;
  searchable: boolean;
  showOnCard: boolean;
}

export interface CategoryDto {
  id: string;
  name: string;
  icon: string;
  kind: 'Physical' | 'Digital';
  isSystem: boolean;
  fields: CategoryFieldDto[];
}

export interface ItemImageDto {
  id: string;
  path: string;
  cardPath: string;
  thumbPath: string;
  isPrimary: boolean;
  order: number;
}

export interface AcquisitionDto {
  acquiredAt: string | null;
  price: { amount: number; currency: string } | null;
  vendor: string | null;
}

export interface ItemDto {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  images: ItemImageDto[];
  tags: string[];
  isShowcased: boolean;
  source: 'Manual' | 'Steam' | 'OpenGraph';
  externalRef: { provider: string; externalId: string; url: string | null; lastSyncedAt: string } | null;
  acquisition: AcquisitionDto | null;
  locationId: string | null;
  attributes: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ShareLinkDto {
  id: string;
  slug: string;
  scope: 'Showcase' | 'Category';
  includeCategoryIds: string[];
  includePrice: boolean;
  expiresAt: string | null;
  createdAt: string;
}

export interface PublicItemDto {
  id: string;
  name: string;
  description: string | null;
  categoryName: string;
  tags: string[];
  images: { cardPath: string; thumbPath: string; isPrimary: boolean; order: number }[];
  attributes: Record<string, unknown>;
  price: { amount: number; currency: string } | null;
}

export interface PublicShareDto {
  ownerDisplayName: string;
  scope: string;
  items: PublicItemDto[];
}

export interface SyncJobDto {
  id: string;
  provider: string;
  status: 'Running' | 'Succeeded' | 'Failed';
  created: number;
  updated: number;
  failed: number;
  error: string | null;
  startedAt: string;
  finishedAt: string | null;
}

export interface ExternalAccountDto {
  provider: string;
  externalUserId: string;
  updatedAt: string;
}

export interface FetchedMetadataDto {
  provider: string;
  externalId: string;
  name: string;
  description: string | null;
  imageUrl: string | null;
  attributes: Record<string, unknown>;
}

/** RFC 9457 ProblemDetails。errors 只在 400 驗證失敗時出現。 */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
