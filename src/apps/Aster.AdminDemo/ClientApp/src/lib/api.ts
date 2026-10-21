export type FacetMetadata = {
  label?: string;
  description?: string;
  displayOrder?: number;
  defaultValue?: unknown;
  allowedValues?: unknown[];
  minLength?: number;
  maxLength?: number;
  minValue?: number;
  maxValue?: number;
  pattern?: string;
  minItems?: number;
  maxItems?: number;
  uniqueItems?: boolean;
  uiHint?: "text" | "multiline" | "richtext" | "slug" | "email" | "url" | "date" | "datetime" | "number" | "toggle" | "select" | "tags" | string;
};

export type FacetDto = {
  facetId: string;
  type: string;
  isRequired: boolean;
  metadata: FacetMetadata | null;
};

export type AspectDto = {
  aspectKey: string;
  displayName: string;
  requiresName: boolean;
  facets: FacetDto[];
  sharedAspectId: string | null;
  sharedVersion: number | null;
};

export type DiagnosticDto = {
  code: string;
  aspectKey: string | null;
  facetId: string | null;
  message: string;
};

export type AspectUsageDto = {
  definitionId: string;
  definitionVersion: number;
  attachmentKey: string;
  pinnedVersion: number;
};

export type SharedAspectDto = {
  aspectId: string;
  displayName: string;
  version: number;
  requiresName: boolean;
  facets: FacetDto[];
  versions: number[];
  usedBy: AspectUsageDto[];
};

export type CreateAspectRequest = {
  aspectId: string;
  requiresName: boolean;
  facets: FacetDto[];
};

export type UpdateAspectRequest = {
  baseVersion: number;
  requiresName: boolean;
  facets: FacetDto[];
};

export type AspectFacetChangeDto = {
  facetId: string;
  kind: "added" | "removed" | "type-changed" | "required-changed" | "metadata-changed";
  from: string | null;
  to: string | null;
};

export type AspectUpgradeCandidateDto = {
  definitionId: string;
  definitionVersion: number;
  attachmentKey: string;
  pinnedVersion: number;
  changes: AspectFacetChangeDto[];
};

export type AspectUpgradePreviewDto = {
  aspectId: string;
  targetVersion: number | null;
  candidates: AspectUpgradeCandidateDto[];
  diagnostics: DiagnosticDto[];
};

export type AspectUpgradeResultDto = {
  targetVersion: number | null;
  outcomes: Array<{
    definitionId: string;
    status: "upgraded" | "not-applicable" | "failed";
    newDefinitionVersion: number | null;
    attachmentKeys: string[];
    diagnostics: DiagnosticDto[];
  }>;
  diagnostics: DiagnosticDto[];
};

export type DefinitionSummaryDto = {
  definitionId: string;
  version: number;
  isSingleton: boolean;
  displayName: string;
  aspectCount: number;
  resourceCount: number;
};

export type DefinitionDetailDto = {
  definitionId: string;
  version: number;
  isSingleton: boolean;
  displayName: string;
  aspects: AspectDto[];
};

export type CreateDefinitionRequest = {
  definitionId: string;
  isSingleton: boolean;
  aspects: Array<{ aspectId: string; version?: number; name?: string }>;
};

export type ResourceSummaryDto = {
  resourceId: string;
  definitionId: string;
  version: number;
  created: string;
  title: string;
  activeChannels: string[];
  tags: string[];
};

export type ResourceListDto = {
  items: ResourceSummaryDto[];
  totalCount: number;
};

export type ResourceAspects = Record<string, Record<string, unknown>>;

export type ResourceDetailDto = {
  resourceId: string;
  definitionId: string;
  version: number;
  created: string;
  aspects: ResourceAspects;
  activeChannels: Record<string, number[]>;
};

export type VersionSummaryDto = {
  version: number;
  created: string;
  isLatest: boolean;
  activeChannels: string[];
};

export type CreateResourceRequest = {
  definitionId: string;
  aspects: ResourceAspects;
};

export type UpdateResourceRequest = {
  baseVersion: number;
  aspects: ResourceAspects;
};

export type Channel = "preview" | "live";

export class ApiError extends Error {
  status: number;
  diagnostics?: DiagnosticDto[];

  constructor(status: number, message: string, diagnostics?: DiagnosticDto[]) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.diagnostics = diagnostics;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
    ...init,
  });

  if (!response.ok) {
    let payload: unknown = undefined;
    try {
      payload = await response.json();
    } catch {
      payload = undefined;
    }

    if (response.status === 400 && isDiagnosticsEnvelope(payload)) {
      throw new ApiError(response.status, "Validation failed", payload.diagnostics);
    }

    if (isErrorEnvelope(payload)) {
      throw new ApiError(response.status, payload.error);
    }

    throw new ApiError(response.status, `Request failed with ${response.status}`);
  }

  return (await response.json()) as T;
}

function isDiagnosticsEnvelope(value: unknown): value is { diagnostics: DiagnosticDto[] } {
  return typeof value === "object" && value !== null && Array.isArray((value as { diagnostics?: unknown }).diagnostics);
}

function isErrorEnvelope(value: unknown): value is { error: string } {
  return typeof value === "object" && value !== null && typeof (value as { error?: unknown }).error === "string";
}

export const api = {
  getAspects: () => request<SharedAspectDto[]>("/api/aspects"),
  getAspect: (aspectId: string) => request<SharedAspectDto>(`/api/aspects/${encodeURIComponent(aspectId)}`),
  createAspect: (body: CreateAspectRequest) => request<SharedAspectDto>("/api/aspects", { method: "POST", body: JSON.stringify(body) }),
  updateAspect: (aspectId: string, body: UpdateAspectRequest) => request<SharedAspectDto>(`/api/aspects/${encodeURIComponent(aspectId)}`, { method: "PUT", body: JSON.stringify(body) }),
  previewAspectUpgrade: (aspectId: string) => request<AspectUpgradePreviewDto>(`/api/aspects/${encodeURIComponent(aspectId)}/upgrade-preview`),
  applyAspectUpgrade: (aspectId: string, targetVersion: number, definitionIds: string[]) =>
    request<AspectUpgradeResultDto>(`/api/aspects/${encodeURIComponent(aspectId)}/upgrade`, { method: "POST", body: JSON.stringify({ targetVersion, definitionIds }) }),
  getDefinitions: () => request<DefinitionSummaryDto[]>("/api/definitions"),
  getDefinition: (definitionId: string) => request<DefinitionDetailDto>(`/api/definitions/${encodeURIComponent(definitionId)}`),
  createDefinition: (body: CreateDefinitionRequest) => request<DefinitionDetailDto>("/api/definitions", { method: "POST", body: JSON.stringify(body) }),
  getResources: (definitionId: string, skip = 0, take = 20, tags: string[] = []) =>
    request<ResourceListDto>(`/api/resources?definitionId=${encodeURIComponent(definitionId)}&skip=${skip}&take=${take}${tags.map((tag) => `&tag=${encodeURIComponent(tag)}`).join("")}`),
  getResource: (resourceId: string) => request<ResourceDetailDto>(`/api/resources/${encodeURIComponent(resourceId)}`),
  getResourceVersions: (resourceId: string) => request<VersionSummaryDto[]>(`/api/resources/${encodeURIComponent(resourceId)}/versions`),
  getResourceVersion: (resourceId: string, version: number) => request<ResourceDetailDto>(`/api/resources/${encodeURIComponent(resourceId)}/versions/${version}`),
  createResource: (body: CreateResourceRequest) => request<ResourceDetailDto>("/api/resources", { method: "POST", body: JSON.stringify(body) }),
  updateResource: (resourceId: string, body: UpdateResourceRequest) => request<ResourceDetailDto>(`/api/resources/${encodeURIComponent(resourceId)}`, { method: "PUT", body: JSON.stringify(body) }),
  activateResource: (resourceId: string, version: number, channel: Channel) => request<{ status: "activated" }>(`/api/resources/${encodeURIComponent(resourceId)}/activate`, { method: "POST", body: JSON.stringify({ version, channel }) }),
  deactivateResource: (resourceId: string, version: number, channel: string) => request<{ status: "deactivated" }>(`/api/resources/${encodeURIComponent(resourceId)}/deactivate`, { method: "POST", body: JSON.stringify({ version, channel }) }),
};
