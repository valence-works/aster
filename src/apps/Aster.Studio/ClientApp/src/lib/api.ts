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

export type LifecycleState = "none" | "archived" | "soft-deleted";
export type LifecycleFilter = "all" | "active" | "archived" | "soft-deleted";

export type ResourceSummaryDto = {
  resourceId: string;
  definitionId: string;
  version: number;
  created: string;
  title: string;
  activeChannels: string[];
  tags: string[];
  lifecycleState: LifecycleState;
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
  /** The definition version this resource version was saved against; its updates validate against it (#130). */
  definitionVersion: number | null;
  created: string;
  aspects: ResourceAspects;
  activeChannels: Record<string, number[]>;
};

export type VersionSummaryDto = {
  version: number;
  created: string;
  isLatest: boolean;
  activeChannels: string[];
  /** The definition version this resource version was saved against. */
  definitionVersion: number | null;
  lifecycleState: LifecycleState;
  isProtectedFromPruning: boolean;
  maintenanceDisposition: "protected" | "possible-candidate";
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

// ── Operations (#111): the SDK's lifecycle, schema and policy results, enums as kebab-case names ──────────

/** A diagnostic with its stable code, as the SDK's lifecycle, policy and portability services report it. */
export type CodedDiagnostic = {
  code: string;
  message: string;
  severity?: "info" | "warning" | "error";
  path?: string | null;
  policyId?: string | null;
  resourceId?: string | null;
  resourceVersion?: number | null;
};

export type DiagnosticCodeCount = { code: string; count: number };

export type LifecycleMarkerDto = {
  resourceId: string;
  state: LifecycleState;
  markedAt: string | null;
  reason: string | null;
};

export type LifecycleMarkerResultDto = {
  succeeded: boolean;
  marker: LifecycleMarkerDto | null;
  diagnostics: CodedDiagnostic[];
};

export type RestoreCandidateDto = {
  index: number;
  resourceId: string | null;
  expectedState: LifecycleState | null;
  status: "restorable" | "restored" | "already-restored" | "skipped" | "failed";
  marker: LifecycleMarkerDto | null;
  diagnostics: CodedDiagnostic[];
};

export type RestoreResultDto = {
  summary: { totalCount: number; failedCount: number; diagnosticCodeCounts: DiagnosticCodeCount[] };
  candidates: RestoreCandidateDto[];
};

export type SchemaStatusDto = {
  resourceId: string;
  resourceVersion: number;
  definitionId: string;
  recordedDefinitionVersion: number | null;
  latestDefinitionVersion: number | null;
  status: "current" | "older-than-latest" | "missing-definition" | "missing-definition-version" | "unknown-resource-lineage";
  message: string;
  /** The SDK's preview of upgrading the latest version to the latest definition. */
  upgrade: SchemaUpgradePreviewDto;
};

export type SchemaUpgradeStatus = "upgraded" | "no-op" | "rejected";

export type SchemaUpgradePreviewDto = {
  /** What the upgrade would report: "upgraded" when it would append a version. */
  status: SchemaUpgradeStatus;
  targetDefinitionVersion: number | null;
  /** Aspect keys the target definition doesn't declare; the upgrade carries them forward. */
  carriedForwardAspectKeys: string[];
  /** Why the upgrade would be rejected. */
  diagnostics: CodedDiagnostic[];
};

export type SchemaUpgradeResultDto = {
  status: SchemaUpgradeStatus;
  resourceVersion: number | null;
  sourceDefinitionVersion: number | null;
  targetDefinitionVersion: number;
  carriedForwardAspectKeys: string[];
  message: string;
};

export type PolicyKind = "retention" | "archival" | "soft-delete" | "version-pruning";
export type PolicyOutcome = "retain" | "archive" | "soft-delete" | "prune-preview";

export type PolicyDeclarationDto = {
  policyId: string;
  name: string | null;
  kind: PolicyKind;
  target: "resource" | "resource-version";
  outcome: PolicyOutcome;
  criteria: {
    /** A .NET TimeSpan, such as "365.00:00:00". */
    minimumAge: string | null;
    maximumRetainedVersions: number | null;
    activationState: "active" | "draft" | null;
    activationChannel: string | null;
    lifecycleState: LifecycleState | null;
  };
};

export type DefinitionPoliciesDto = {
  definitionId: string;
  definitionVersion: number;
  policies: PolicyDeclarationDto[];
  diagnostics: CodedDiagnostic[];
};

export type PolicyCandidateDto = {
  policyId: string;
  policyKind: PolicyKind;
  outcome: PolicyOutcome;
  resourceId: string;
  resourceVersion: number | null;
  reason: string;
};

export type PolicyPreviewDto = {
  summary: { evaluationTimestamp: string | null; totalCandidateCount: number; diagnosticCodeCounts: DiagnosticCodeCount[] };
  candidates: PolicyCandidateDto[];
  diagnostics: CodedDiagnostic[];
};

export type PolicyApplicationResultDto = {
  summary: { totalCount: number; appliedCount: number; alreadySatisfiedCount: number; failedCount: number; diagnosticCodeCounts: DiagnosticCodeCount[] };
  candidates: Array<{ index: number; status: "applied" | "already-satisfied" | "skipped" | "failed"; policyId: string | null; resourceId: string | null; resourceVersion: number | null; diagnostics: CodedDiagnostic[] }>;
};

export type PruningResultDto = {
  summary: { totalCount: number; prunedCount: number; alreadyPrunedCount: number; failedCount: number; diagnosticCodeCounts: DiagnosticCodeCount[] };
  candidates: Array<{ index: number; status: "pruned" | "already-pruned" | "skipped" | "failed"; policyId: string | null; resourceId: string | null; resourceVersion: number | null; diagnostics: CodedDiagnostic[] }>;
};

export type TenantDto = { tenantId: string; displayName: string };

// ── Portability (#112): the SDK's export, import preview and import results, each with its summary ─────────

export type ExportScopeMode = "definition-with-resources" | "definitions-only" | "selected-resources";
export type ExportVersionScope = "all-versions" | "latest-only" | "specific-versions";
export type CollisionMode = "strict" | "remap-divergent";

export type ExportRequest = {
  scopeMode: ExportScopeMode;
  definitionIds: string[];
  resourceIds: string[];
  resourceVersionScope: ExportVersionScope;
  specificResourceVersions: Array<{ resourceId: string; version: number }>;
  includeReferencedResources: boolean;
};

/** A portable snapshot, passed back to import as it was exported. */
export type PortableSnapshot = {
  formatVersion: number;
  sourceTenantScope: { tenantId: string };
  aspectDefinitions: unknown[];
  definitions: unknown[];
  resources: unknown[];
  activationStates: unknown[];
  lifecycleMarkers: unknown[];
};

export type PortableCounts = {
  aspectDefinitions: number;
  definitions: number;
  resources: number;
  resourceVersions: number;
  activationEntries: number;
  lifecycleMarkers: number;
  reusedIdenticalItems: number;
  remappedItems: number;
};

export type IdentityMapping = {
  entityKind: "definition" | "definition-version" | "resource" | "resource-version" | "activation-entry" | "lifecycle-marker" | "aspect-definition-version" | "reference-value";
  /** A JSON array of the identity's parts, such as ["page",1]; a plain resource ID for reference values. */
  sourceId: string;
  targetId: string;
  reason: "preserved" | "reused-identical" | "remapped-divergent" | "collided-divergent";
  /** For a rewritten reference value: resources/{resourceId}/{version}/{aspectKey}/{facetId}[/{index}]. */
  location: string | null;
};

export type ExportResultDto = {
  summary: { aspectDefinitionCount: number; definitionCount: number; resourceVersionCount: number; activationEntryCount: number; lifecycleMarkerCount: number; skippedActivationEntryCount: number; hasErrors: boolean };
  result: { snapshot: PortableSnapshot | null; diagnostics: CodedDiagnostic[] };
};

export type ImportPreviewDto = {
  summary: { totalPlannedItemCount: number; hasErrors: boolean };
  result: { canImport: boolean; counts: PortableCounts; identityMap: IdentityMapping[]; diagnostics: CodedDiagnostic[] };
};

export type ImportResultDto = {
  summary: { totalActualItemCount: number; hasErrors: boolean };
  result: { status: "imported" | "no-op" | "failed"; counts: PortableCounts; identityMap: IdentityMapping[]; diagnostics: CodedDiagnostic[] };
};

// ── Query builder (#112): IResourceQueryValidator failures and the provider's QueryCapabilityDescription ─────

export type ComparisonOperator = "equals" | "not-equals" | "in" | "contains" | "starts-with" | "range" | "exists" | "array-contains" | "array-contains-any" | "array-contains-all";
export type VersionScope = "latest" | "all-versions" | "active" | "draft";

/** One condition; condition i is the filter's operand i, so failures at Filter.Operands[i] belong to it. */
export type QueryCondition = {
  kind: "metadata" | "facet" | "exists" | "references";
  field?: string;
  aspectKey?: string;
  facetId?: string;
  operator: ComparisonOperator;
  /** A range is { min, max }; in and array operators take a list. */
  value?: unknown;
};

export type QueryRequest = {
  definitionId: string | null;
  scope: VersionScope;
  channel: string | null;
  lifecycle: LifecycleFilter;
  match: "and" | "or";
  conditions: QueryCondition[];
  sort: Array<{ field: string; aspectKey: string | null; descending: boolean }>;
  take: number;
};

export type QueryValidationFailure = { code: string; message: string; path: string | null; feature: string | null };

export type QueryValidationDto = {
  summary: { totalFailureCount: number; isValid: boolean };
  failures: QueryValidationFailure[];
};

export type QueryResultDto = {
  items: Array<{ resourceId: string; definitionId: string; version: number; created: string; title: string }>;
  hasMore: boolean;
};

export type QueryCapabilities = {
  providerKey: string;
  providerName: string;
  supportedScopes: VersionScope[];
  requiresActivationChannelForActiveScope: boolean;
  supportedFilterTypes: string[];
  supportedLogicalOperators: string[];
  supportedComparisonOperators: Partial<Record<"metadata" | "facet-value", ComparisonOperator[]>>;
  supportedMetadataFields: string[];
  metadataContainsFields: string[];
  supportsMetadataSorting: boolean;
  supportsFacetSorting: boolean;
  facetRangeSupport: string[];
  unsupportedFeatures: string[];
  maxArrayQueryElements: number | null;
  supportsLifecycleStateFiltering: boolean;
};

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

// Every request names the selected tenant; both the Aster endpoints and Studio's resolve it through the host's
// ITenantScopeAccessor. The choice persists across reloads.
const tenantStorageKey = "aster-studio.tenant";
let tenant = readStoredTenant();

function readStoredTenant() {
  try {
    return localStorage.getItem(tenantStorageKey) || "default";
  } catch {
    return "default";
  }
}

export const currentTenant = () => tenant;

export function selectTenant(next: string) {
  tenant = next;
  try {
    localStorage.setItem(tenantStorageKey, next);
  } catch {
    // The selection still holds for this page.
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    headers: {
      "Content-Type": "application/json",
      "X-Aster-Tenant": tenant,
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

    // Studio's endpoints answer with { error }, { diagnostics } or problem details; the Aster endpoints with problem
    // details. Problem details carry stable diagnostic codes under `diagnostics`.
    if (isDiagnosticsEnvelope(payload)) {
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
  definitionVersion?: number | null;
  version: number;
  created: string;
  aspects: Record<string, unknown>;
};

type AsterVersionHistory = {
  resourceId: string;
  versions: VersionSummaryDto[];
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
    definitionVersion: resource.definitionVersion ?? null,
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

const getDefinition = async (definitionId: string, version?: number) =>
  toDefinitionDetail(await request<AsterDefinition>(`${aster}/definitions/${segment(definitionId)}${version === undefined ? "" : `?version=${version}`}`));

/**
 * The definition version that governs a resource's next update, as the SDK resolves it (ADR 0002, #130): the version
 * the resource records, or the latest when it records none or that version is no longer stored. The editor renders
 * its form from this version so the form and the save's validation agree (#191).
 */
async function getRecordedDefinition(resource: Pick<ResourceDetailDto, "definitionId" | "definitionVersion">): Promise<DefinitionDetailDto> {
  if (resource.definitionVersion === null) return getDefinition(resource.definitionId);
  try {
    return await getDefinition(resource.definitionId, resource.definitionVersion);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return getDefinition(resource.definitionId);
    throw error;
  }
}

// Studio's facet types name the UI control; the store's are the SDK's. "number" is stored as decimal.
function toSdkFacetType(type: string): string {
  if (type.endsWith("[]")) return `${toSdkFacetType(type.slice(0, -2))}[]`;
  switch (type.toLowerCase()) {
    case "number":
      return "decimal";
    case "boolean":
      return "bool";
    default:
      return type.toLowerCase();
  }
}

function toAsterAspect(aspectId: string, requiresName: boolean, facets: FacetDto[]): AsterAspectDefinition {
  return {
    aspectDefinitionId: aspectId,
    id: crypto.randomUUID(),
    version: 0,
    requiresName,
    isShared: false,
    facetDefinitions: facets.map((facet) => {
      const { minValue, maxValue, ...rest } = facet.metadata ?? {};
      const metadata = Object.fromEntries(Object.entries({ ...rest, minimum: minValue, maximum: maxValue })
        .filter(([, value]) => value !== undefined && value !== null)) as AsterFacetMetadata;
      return {
        facetDefinitionId: facet.facetId,
        id: crypto.randomUUID(),
        version: 1,
        type: toSdkFacetType(facet.type),
        isRequired: facet.isRequired,
        metadata: facet.metadata ? metadata : null,
      };
    }),
  };
}

// The store appends on every registration, so a save that changes nothing is skipped. Compared as stored: SDK
// types, and metadata without empty values, in a fixed key order.
function shapeOf(requiresName: boolean, facets: FacetDto[]): string {
  const metadata = (value: FacetMetadata | null) => Object.entries(value ?? {})
    .filter(([, entry]) => entry !== undefined && entry !== null && entry !== "")
    .sort(([left], [right]) => ordinal(left, right));
  return JSON.stringify([requiresName, facets.map((facet) => [facet.facetId, toSdkFacetType(facet.type), facet.isRequired, metadata(facet.metadata)])]);
}

function checkAspect(aspectId: string, facets: FacetDto[]) {
  const fail = (code: string, message: string, facetId: string | null = null): never => {
    throw new ApiError(400, "Validation failed", [{ code, aspectKey: aspectId, facetId, message }]);
  };
  if (!/^[A-Za-z][A-Za-z0-9_-]*$/.test(aspectId)) fail("aspect-id-invalid", "Aspect id must start with a letter and contain only letters, digits, '-' or '_'.");
  if (facets.length === 0) fail("aspect-facets-required", "An aspect must declare at least one facet.");
  const seen = new Set<string>();
  for (const facet of facets) {
    if (!facet.facetId.trim()) fail("aspect-facet-id-required", "Every facet needs an id.");
    if (seen.has(facet.facetId)) fail("aspect-facet-id-duplicate", `Facet '${facet.facetId}' is declared more than once.`, facet.facetId);
    seen.add(facet.facetId);
  }
}

// Shared aspects register through the Aster endpoint's compare-and-set (#175): creating expects no version yet, and
// saving expects the version the form was loaded from, so a concurrent edit is never superseded.
async function createAspect(body: CreateAspectRequest): Promise<SharedAspectDto> {
  checkAspect(body.aspectId, body.facets);
  try {
    await request<AsterAspectDefinition>(`${aster}/aspects?expectedLatestVersion=0`, json("POST", toAsterAspect(body.aspectId, body.requiresName, body.facets)));
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) throw new ApiError(409, `Aspect '${body.aspectId}' already exists.`);
    throw error;
  }

  return getAspect(body.aspectId);
}

async function updateAspect(aspectId: string, body: UpdateAspectRequest): Promise<SharedAspectDto> {
  checkAspect(aspectId, body.facets);
  const latest = await getAspect(aspectId);
  if (latest.version === body.baseVersion && shapeOf(latest.requiresName, latest.facets) === shapeOf(body.requiresName, body.facets)) return latest;

  try {
    await request<AsterAspectDefinition>(`${aster}/aspects?expectedLatestVersion=${body.baseVersion}`, json("POST", toAsterAspect(aspectId, body.requiresName, body.facets)));
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      throw new ApiError(409, `Aspect '${aspectId}' was changed since you opened it. Reload and reapply your changes.`);
    }
    throw error;
  }

  return getAspect(aspectId);
}

const getAspect = (aspectId: string) => request<SharedAspectDto>(`${studio}/aspects/${segment(aspectId)}`);

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
  getTenants: () => request<TenantDto[]>(`${studio}/tenants`),
  getAspects: () => request<SharedAspectDto[]>(`${studio}/aspects`),
  getAspect,
  createAspect,
  updateAspect,
  previewAspectUpgrade: (aspectId: string) => request<AspectUpgradePreviewDto>(`${studio}/aspects/${segment(aspectId)}/upgrade-preview`),
  applyAspectUpgrade: (aspectId: string, targetVersion: number, definitionIds: string[]) =>
    request<AspectUpgradeResultDto>(`${studio}/aspects/${segment(aspectId)}/upgrade`, json("POST", { targetVersion, definitionIds })),
  getDefinitions: () => request<DefinitionSummaryDto[]>(`${studio}/definitions`),
  /** The latest version of a definition, or the one named by `version`. */
  getDefinition,
  getRecordedDefinition,
  createDefinition,
  getResources: (definitionId: string, skip = 0, take = 20, tags: string[] = [], lifecycle: LifecycleFilter = "all") =>
    request<ResourceListDto>(`${studio}/resources?definitionId=${segment(definitionId)}&skip=${skip}&take=${take}&lifecycle=${lifecycle}${tags.map((tag) => `&tag=${segment(tag)}`).join("")}`),
  getResource: (resourceId: string) => withHistory(request<AsterResource>(resourcePath(resourceId))),
  getResourceVersions: async (resourceId: string): Promise<VersionSummaryDto[]> =>
    (await getHistory(resourceId)).versions.map((version) => ({ ...version, activeChannels: orderChannels(version.activeChannels) })),
  getResourceVersion: (resourceId: string, version: number) => withHistory(request<AsterResource>(`${resourcePath(resourceId)}?version=${version}`)),
  createResource: (body: CreateResourceRequest) => withHistory(request<AsterResource>(`${aster}/resources`, json("POST", body))),
  updateResource: (resourceId: string, body: UpdateResourceRequest) => withHistory(request<AsterResource>(resourcePath(resourceId), json("PUT", body))),
  activateResource: (resourceId: string, version: number, channel: Channel) => request<void>(`${resourcePath(resourceId)}/activate`, json("POST", { version, channel })),
  deactivateResource: (resourceId: string, version: number, channel: string) => request<void>(`${resourcePath(resourceId)}/deactivate`, json("POST", { version, channel })),
  getLifecycle: (resourceId: string) => request<LifecycleMarkerDto>(`${studio}/resources/${segment(resourceId)}/lifecycle`),
  markResource: (resourceId: string, state: Exclude<LifecycleState, "none">, reason: string) =>
    request<LifecycleMarkerResultDto>(`${studio}/resources/${segment(resourceId)}/lifecycle`, json("POST", { state, reason })),
  previewRestore: (resourceId: string, expectedState: LifecycleState) =>
    request<RestoreResultDto>(`${studio}/resources/${segment(resourceId)}/restore-preview`, json("POST", { expectedState })),
  restoreResource: (resourceId: string, expectedState: LifecycleState) =>
    request<RestoreResultDto>(`${studio}/resources/${segment(resourceId)}/restore`, json("POST", { expectedState })),
  getSchemaStatus: (resourceId: string) => request<SchemaStatusDto>(`${studio}/resources/${segment(resourceId)}/schema`),
  upgradeSchema: (resourceId: string, baseVersion: number) =>
    request<SchemaUpgradeResultDto>(`${studio}/resources/${segment(resourceId)}/schema-upgrade`, json("POST", { baseVersion })),
  getPolicies: (definitionId: string) => request<DefinitionPoliciesDto>(`${studio}/definitions/${segment(definitionId)}/policies`),
  previewPolicies: (definitionId: string, evaluationTimestamp: string | null) =>
    request<PolicyPreviewDto>(`${studio}/definitions/${segment(definitionId)}/policies/preview`, json("POST", { evaluationTimestamp })),
  applyPolicyOutcomes: (candidates: PolicyCandidateDto[]) =>
    request<PolicyApplicationResultDto>(`${studio}/policies/apply`, json("POST", { candidates: candidates.map(toApplicationCandidate) })),
  pruneVersions: (candidates: PolicyCandidateDto[]) =>
    request<PruningResultDto>(`${studio}/policies/prune`, json("POST", { candidates: candidates.map(toApplicationCandidate) })),
  exportSnapshot: (body: ExportRequest) => request<ExportResultDto>(`${studio}/portability/export`, json("POST", body)),
  previewImport: (snapshot: PortableSnapshot, collisionMode: CollisionMode) =>
    request<ImportPreviewDto>(`${studio}/portability/preview`, json("POST", { snapshot, collisionMode })),
  importSnapshot: (snapshot: PortableSnapshot, collisionMode: CollisionMode) =>
    request<ImportResultDto>(`${studio}/portability/import`, json("POST", { snapshot, collisionMode })),
  getQueryCapabilities: () => request<QueryCapabilities>(`${studio}/query/capabilities`),
  validateQuery: (query: QueryRequest) => request<QueryValidationDto>(`${studio}/query/validate`, json("POST", query)),
  runQuery: (query: QueryRequest) => request<QueryResultDto>(`${studio}/query`, json("POST", query)),
};

// Application re-checks a preview candidate by its identity: the policy, the outcome and the previewed version.
function toApplicationCandidate({ policyId, policyKind, outcome, resourceId, resourceVersion }: PolicyCandidateDto) {
  return { policyId, policyKind, outcome, resourceId, resourceVersion };
}
