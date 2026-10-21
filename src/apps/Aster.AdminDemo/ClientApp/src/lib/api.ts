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
  /** A standard hint from `UiHints` (docs/ui-hints.md), or a host-specific one. */
  uiHint?: string;
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
  /** Set by the Aster endpoints for a failing array element. */
  elementIndex?: number | null;
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
  /** Each attachment pins a shared aspect at `version`; `name` makes it a named attachment. */
  aspects: Array<{ aspectId: string; version: number; name?: string }>;
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

// Resource, definition and aspect operations go to the Aster.AspNetCore endpoints (#110); Studio's own
// endpoints serve only its view aggregates and the shared-aspect library.
const aster = "/api/aster";
const studio = "/api/studio";

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

    // Studio's endpoints answer with { error } or { diagnostics }; the Aster endpoints with problem details,
    // which carry the same diagnostics on a 400.
    if (response.status === 400 && isDiagnosticsEnvelope(payload)) {
      throw new ApiError(response.status, problemText(payload) ?? "Validation failed", payload.diagnostics);
    }

    if (isErrorEnvelope(payload)) {
      throw new ApiError(response.status, payload.error);
    }

    throw new ApiError(response.status, problemText(payload) ?? `Request failed with ${response.status}`);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

function isDiagnosticsEnvelope(value: unknown): value is { diagnostics: DiagnosticDto[] } {
  return typeof value === "object" && value !== null && Array.isArray((value as { diagnostics?: unknown }).diagnostics);
}

function isErrorEnvelope(value: unknown): value is { error: string } {
  return typeof value === "object" && value !== null && typeof (value as { error?: unknown }).error === "string";
}

function problemText(value: unknown): string | undefined {
  if (typeof value !== "object" || value === null) return undefined;
  const { detail, title } = value as { detail?: unknown; title?: unknown };
  if (typeof detail === "string" && detail) return detail;
  return typeof title === "string" && title ? title : undefined;
}

const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) });
const segment = encodeURIComponent;

// ── The Aster.AspNetCore contract ─────────────────────────────────────────────

type AsterFacetMetadata = {
  label?: string;
  description?: string;
  displayOrder?: number;
  defaultValue?: unknown;
  allowedValues?: unknown[];
  minLength?: number;
  maxLength?: number;
  minimum?: number;
  maximum?: number;
  pattern?: string;
  minItems?: number;
  maxItems?: number;
  uniqueItems?: boolean;
  uiHint?: string;
};

type AsterFacetDefinition = {
  facetDefinitionId: string;
  type: string;
  isRequired: boolean;
  metadata?: AsterFacetMetadata | null;
};

type AsterAspectDefinition = {
  aspectDefinitionId: string;
  id: string;
  version: number;
  requiresName: boolean;
  facetDefinitions: AsterFacetDefinition[];
  isShared: boolean;
};

type AsterDefinition = {
  definitionId: string;
  id: string;
  version: number;
  isSingleton: boolean;
  aspectDefinitions: Record<string, AsterAspectDefinition>;
};

type AsterResource = {
  resourceId: string;
  definitionId: string;
  version: number;
  created: string;
  aspects: Record<string, unknown>;
};

type AsterVersionHistory = {
  resourceId: string;
  versions: Array<{ version: number; created: string; isLatest: boolean; activeChannels: string[] }>;
};

// ── Mapping the contract to Studio's view types ───────────────────────────────

const fixedChannels = ["preview", "live"];

const ordinal = (left: string, right: string) => (left < right ? -1 : left > right ? 1 : 0);

function orderChannels(channels: Iterable<string>): string[] {
  const rank = (channel: string) => (fixedChannels.includes(channel) ? fixedChannels.indexOf(channel) : fixedChannels.length);
  return [...new Set(channels)].sort((left, right) => rank(left) - rank(right) || ordinal(left, right));
}

/** Mirrors the server's display names: "seo" reads "SEO", anything else gets a capital first letter. */
function toDisplayName(value: string): string {
  if (value.toLowerCase() === "seo") return "SEO";
  return value ? value[0].toUpperCase() + value.slice(1) : value;
}

// Studio's facet types name the UI control: "number" covers every numeric SDK type, and arrays map per element.
function toStudioFacetType(type: string): string {
  if (type.endsWith("[]")) return `${toStudioFacetType(type.slice(0, -2))}[]`;
  switch (type.toLowerCase()) {
    case "decimal":
    case "int":
    case "int32":
    case "int64":
    case "long":
      return "number";
    case "bool":
    case "boolean":
      return "boolean";
    case "datetime":
    case "datetimeoffset":
      return "datetime";
    default:
      return type.toLowerCase();
  }
}

function toFacetMetadata(metadata: AsterFacetMetadata | null | undefined): FacetMetadata | null {
  if (!metadata) return null;
  const { minimum, maximum, ...rest } = metadata;
  const mapped: FacetMetadata = { ...rest, minValue: minimum, maxValue: maximum };
  return Object.fromEntries(Object.entries(mapped).filter(([, value]) => value !== undefined && value !== null)) as FacetMetadata;
}

function toFacetDtos(facets: AsterFacetDefinition[]): FacetDto[] {
  return [...facets]
    .sort((left, right) =>
      (left.metadata?.displayOrder ?? Number.MAX_SAFE_INTEGER) - (right.metadata?.displayOrder ?? Number.MAX_SAFE_INTEGER)
      || ordinal(left.facetDefinitionId, right.facetDefinitionId))
    .map((facet) => ({
      facetId: facet.facetDefinitionId,
      type: toStudioFacetType(facet.type),
      isRequired: facet.isRequired,
      metadata: toFacetMetadata(facet.metadata),
    }));
}

function toDefinitionDetail(definition: AsterDefinition): DefinitionDetailDto {
  return {
    definitionId: definition.definitionId,
    version: definition.version,
    isSingleton: definition.isSingleton,
    displayName: toDisplayName(definition.definitionId),
    aspects: Object.entries(definition.aspectDefinitions)
      .sort(([left], [right]) => ordinal(left, right))
      .map(([key, aspect]) => ({
        aspectKey: key,
        displayName: toDisplayName(key),
        requiresName: aspect.requiresName,
        facets: toFacetDtos(aspect.facetDefinitions),
        sharedAspectId: aspect.isShared ? aspect.aspectDefinitionId : null,
        sharedVersion: aspect.isShared ? aspect.version : null,
      })),
  };
}

function toResourceDetail(resource: AsterResource, history: AsterVersionHistory): ResourceDetailDto {
  const activeChannels: Record<string, number[]> = {};
  for (const channel of orderChannels(history.versions.flatMap((version) => version.activeChannels))) {
    activeChannels[channel] = history.versions
      .filter((version) => version.activeChannels.includes(channel))
      .map((version) => version.version)
      .sort((left, right) => left - right);
  }

  return {
    resourceId: resource.resourceId,
    definitionId: resource.definitionId,
    version: resource.version,
    created: resource.created,
    aspects: resource.aspects as ResourceAspects,
    activeChannels,
  };
}

const resourcePath = (resourceId: string) => `${aster}/resources/${segment(resourceId)}`;
const getHistory = (resourceId: string) => request<AsterVersionHistory>(`${resourcePath(resourceId)}/versions`);

async function withHistory(resource: Promise<AsterResource>): Promise<ResourceDetailDto> {
  const loaded = await resource;
  return toResourceDetail(loaded, await getHistory(loaded.resourceId));
}

async function createDefinition(body: CreateDefinitionRequest): Promise<DefinitionDetailDto> {
  // The endpoint re-resolves each shared attachment from the aspect store by (id, version), so a stub is enough.
  const aspectDefinitions: Record<string, AsterAspectDefinition> = {};
  for (const { aspectId, version, name } of body.aspects) {
    const key = name ? `${aspectId}:${name}` : aspectId;
    if (key in aspectDefinitions) {
      throw new ApiError(400, "Validation failed", [
        { code: "duplicate-aspect-attachment", aspectKey: key, facetId: null, message: `Aspect '${key}' is attached more than once.` },
      ]);
    }

    aspectDefinitions[key] = { aspectDefinitionId: aspectId, id: "", version, requiresName: false, facetDefinitions: [], isShared: true };
  }

  try {
    // expectedLatestVersion=0: creating never appends a version to an existing content type.
    const registered = await request<AsterDefinition>(`${aster}/definitions?expectedLatestVersion=0`, json("POST", {
      definitionId: body.definitionId,
      id: crypto.randomUUID(),
      version: 0,
      isSingleton: body.isSingleton,
      aspectDefinitions,
    }));
    return toDefinitionDetail(registered);
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) throw new ApiError(409, `Content type '${body.definitionId}' already exists.`);
    throw error;
  }
}

export const api = {
  getAspects: () => request<SharedAspectDto[]>(`${studio}/aspects`),
  getAspect: (aspectId: string) => request<SharedAspectDto>(`${studio}/aspects/${segment(aspectId)}`),
  createAspect: (body: CreateAspectRequest) => request<SharedAspectDto>(`${studio}/aspects`, json("POST", body)),
  updateAspect: (aspectId: string, body: UpdateAspectRequest) => request<SharedAspectDto>(`${studio}/aspects/${segment(aspectId)}`, json("PUT", body)),
  previewAspectUpgrade: (aspectId: string) => request<AspectUpgradePreviewDto>(`${studio}/aspects/${segment(aspectId)}/upgrade-preview`),
  applyAspectUpgrade: (aspectId: string, targetVersion: number, definitionIds: string[]) =>
    request<AspectUpgradeResultDto>(`${studio}/aspects/${segment(aspectId)}/upgrade`, json("POST", { targetVersion, definitionIds })),
  getDefinitions: () => request<DefinitionSummaryDto[]>(`${studio}/definitions`),
  getDefinition: async (definitionId: string) => toDefinitionDetail(await request<AsterDefinition>(`${aster}/definitions/${segment(definitionId)}`)),
  createDefinition,
  getResources: (definitionId: string, skip = 0, take = 20, tags: string[] = []) =>
    request<ResourceListDto>(`${studio}/resources?definitionId=${segment(definitionId)}&skip=${skip}&take=${take}${tags.map((tag) => `&tag=${segment(tag)}`).join("")}`),
  getResource: (resourceId: string) => withHistory(request<AsterResource>(resourcePath(resourceId))),
  getResourceVersions: async (resourceId: string): Promise<VersionSummaryDto[]> =>
    (await getHistory(resourceId)).versions.map((version) => ({
      version: version.version,
      created: version.created,
      isLatest: version.isLatest,
      activeChannels: orderChannels(version.activeChannels),
    })),
  getResourceVersion: (resourceId: string, version: number) => withHistory(request<AsterResource>(`${resourcePath(resourceId)}?version=${version}`)),
  createResource: (body: CreateResourceRequest) => withHistory(request<AsterResource>(`${aster}/resources`, json("POST", body))),
  updateResource: (resourceId: string, body: UpdateResourceRequest) => withHistory(request<AsterResource>(resourcePath(resourceId), json("PUT", body))),
  activateResource: (resourceId: string, version: number, channel: Channel) => request<void>(`${resourcePath(resourceId)}/activate`, json("POST", { version, channel })),
  deactivateResource: (resourceId: string, version: number, channel: string) => request<void>(`${resourcePath(resourceId)}/deactivate`, json("POST", { version, channel })),
};
